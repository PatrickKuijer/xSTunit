using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using xStunit.Interpreter.Extensibility;
using xStunit.Parser;

namespace xStunit.Cli.Plugins
{
    // Lives in the CLI rather than the interpreter for a hard reason, not a
    // stylistic one: AssemblyLoadContext doesn't exist in netstandard2.0, which
    // xStunit.Interpreter targets so the VSIX can consume it. The interpreter
    // owns the contract and the registry; each host owns how it fills that
    // registry, and a host with no plugin story simply never calls this.
    //
    // A bad DLL in the plugin folder is skipped and reported, never fatal:
    // losing one plugin should degrade only the suites that needed it, with
    // Engine's clear "no native function is registered" error.
    internal static class NativeFunctionPluginLoader
    {
        // Never loaded *as plugins*, even when a build drops copies of them
        // beside one: a second copy of xStunit.Interpreter in the plugin
        // context defines a second, non-identical IXstunitNativeFunction, and
        // every plugin in that DLL then silently fails the interface check.
        // PluginLoadContext.Load already redirects these to the host; skipping
        // them here too denies that failure its second chance to happen.
        private static readonly HashSet<string> HostAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "xStunit.Interpreter", "xStunit.Parser", "xStunit.Runner", "xstunit",
        };

        public static NativeFunctionRegistry Load(
            string pluginDirectory, out List<SkippedFile> skipped, out List<string> loadedFrom)
        {
            skipped = new List<SkippedFile>();
            loadedFrom = new List<string>();
            var registry = new NativeFunctionRegistry();

            if (string.IsNullOrWhiteSpace(pluginDirectory))
                return registry;

            if (!Directory.Exists(pluginDirectory))
            {
                skipped.Add(new SkippedFile(pluginDirectory, "plugin directory does not exist"));
                return registry;
            }

            // Ordered so a run is reproducible: which DLL wins a duplicate-name
            // conflict must not depend on the filesystem's enumeration order.
            var candidates = Directory.GetFiles(pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);

            foreach (var dll in candidates)
            {
                if (HostAssemblyNames.Contains(Path.GetFileNameWithoutExtension(dll)))
                    continue;

                try
                {
                    var count = LoadFrom(dll, registry);
                    if (count > 0)
                        loadedFrom.Add($"{Path.GetFileName(dll)} ({count} function(s))");
                }
                catch (BadImageFormatException)
                {
                    skipped.Add(new SkippedFile(dll, "not a managed assembly"));
                }
                catch (ReflectionTypeLoadException ex)
                {
                    // The first loader exception is the one that names the type
                    // that failed to resolve; the outer message does not.
                    var detail = ex.LoaderExceptions.FirstOrDefault()?.Message ?? ex.Message;
                    skipped.Add(new SkippedFile(dll, $"plugin types could not be loaded: {detail}"));
                }
                catch (Exception ex)
                {
                    // Catches broadly ON PURPOSE: one bad DLL must not stop the
                    // remaining plugins from loading. Includes the
                    // duplicate-name InvalidOperationException from
                    // NativeFunctionRegistry.Register, where whatever this DLL
                    // registered before the clash stays registered - partial,
                    // but better than dropping working functions.
                    skipped.Add(new SkippedFile(dll, ex.Message));
                }
            }

            return registry;
        }

        private static int LoadFrom(string dll, NativeFunctionRegistry registry)
        {
            var context = new PluginLoadContext(dll);
            var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(dll));

            var pluginTypes = assembly.GetExportedTypes()
                .Where(t => typeof(IXstunitNativeFunction).IsAssignableFrom(t))
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .Where(t => t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToList();

            var count = 0;
            foreach (var type in pluginTypes)
            {
                var function = (IXstunitNativeFunction)Activator.CreateInstance(type);
                registry.Register(function, $"{Path.GetFileName(dll)}!{type.FullName}");
                count++;
            }

            return count;
        }

        // One context per plugin, so each plugin's private dependencies stay
        // isolated from every other plugin's - two plugins may legitimately
        // ship different versions of the same helper package.
        //
        // isCollectible so a long-lived host can unload a plugin set between
        // runs instead of pinning every plugin ever loaded for the process
        // lifetime.
        private sealed class PluginLoadContext : AssemblyLoadContext
        {
            private readonly AssemblyDependencyResolver _resolver;

            public PluginLoadContext(string pluginPath)
                : base(name: $"XstunitPlugin:{Path.GetFileName(pluginPath)}", isCollectible: true)
            {
                _resolver = new AssemblyDependencyResolver(pluginPath);
            }

            protected override Assembly Load(AssemblyName assemblyName)
            {
                // THE rule that makes plugins work at all: an assembly the host
                // has already loaded must resolve to the host's copy, never to
                // a copy sitting beside the plugin (returning null defers to
                // the default context). A private copy of xStunit.Interpreter
                // would give the plugin a *different* IXstunitNativeFunction
                // type despite the identical name, IsAssignableFrom would be
                // false, and the plugin would be silently ignored.
                if (Default.Assemblies.Any(a => string.Equals(
                        a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase)))
                    return null;

                var path = _resolver.ResolveAssemblyToPath(assemblyName);
                return path != null ? LoadFromAssemblyPath(path) : null;
            }

            protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
            {
                var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
                return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
            }
        }
    }
}
