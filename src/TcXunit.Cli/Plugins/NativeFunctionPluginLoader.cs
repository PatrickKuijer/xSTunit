using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using TcXunit.Interpreter.Extensibility;
using TcXunit.Parser;

namespace TcXunit.Cli.Plugins
{
    // Loads ITcXunitNativeFunction implementations from a directory of plugin
    // assemblies (TcXunit-6k2).
    //
    // Lives in the CLI rather than the interpreter for a hard reason, not a
    // stylistic one: AssemblyLoadContext doesn't exist in netstandard2.0, which
    // TcXunit.Interpreter targets so the VSIX can consume it. The interpreter
    // therefore owns the contract and the registry; each host owns how it fills
    // that registry. A host with no plugin story at all simply doesn't call
    // this.
    //
    // Resilience matches the rest of the run pipeline (TcXunit-iyd.7): a
    // malformed, unmanaged, or type-load-failing DLL in the plugin folder is
    // skipped and reported, never fatal. Losing one plugin should degrade the
    // suites that needed it - with the clear "no native function is registered"
    // error from Engine - not take down every other suite in the tree.
    internal static class NativeFunctionPluginLoader
    {
        // Assemblies that must never be loaded *as plugins* even if a build
        // drops copies of them next to one. Loading a second copy of
        // TcXunit.Interpreter into the plugin context would create a second,
        // non-identical ITcXunitNativeFunction type, and every plugin in that
        // DLL would then silently fail the interface check with no obvious
        // reason. PluginLoadContext already redirects these to the host (see
        // Load below); skipping them here as well means the failure never even
        // gets the chance to be confusing.
        private static readonly HashSet<string> HostAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TcXunit.Interpreter", "TcXunit.Parser", "TcXunit.Runner", "tcxunit",
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
                    // A native DLL or otherwise non-managed file that happens to
                    // share the folder - not an error worth reporting loudly,
                    // but worth reporting.
                    skipped.Add(new SkippedFile(dll, "not a managed assembly"));
                }
                catch (ReflectionTypeLoadException ex)
                {
                    // Typically a plugin built against a different TcXunit
                    // version: name the first underlying loader error, which is
                    // the one that actually says which type failed to resolve.
                    var detail = ex.LoaderExceptions.FirstOrDefault()?.Message ?? ex.Message;
                    skipped.Add(new SkippedFile(dll, $"plugin types could not be loaded: {detail}"));
                }
                catch (Exception ex)
                {
                    // Includes a duplicate-name InvalidOperationException from
                    // NativeFunctionRegistry.Register: the offending DLL is
                    // skipped, the run continues with whatever registered
                    // first. Anything already registered from this same DLL
                    // before the clash stays registered - partial, but strictly
                    // better than dropping working functions, and the skip line
                    // says which file was involved.
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
                .Where(t => typeof(ITcXunitNativeFunction).IsAssignableFrom(t))
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .Where(t => t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToList();

            var count = 0;
            foreach (var type in pluginTypes)
            {
                var function = (ITcXunitNativeFunction)Activator.CreateInstance(type);
                registry.Register(function, $"{Path.GetFileName(dll)}!{type.FullName}");
                count++;
            }

            return count;
        }

        // Per-plugin load context, isolating each plugin's private dependencies
        // from every other plugin's (two plugins may legitimately ship
        // different versions of some helper package).
        //
        // isCollectible: true so a long-lived host - the VSIX, if this
        // eventually moves there - can unload a plugin set between runs rather
        // than pinning every plugin ever loaded for the process lifetime.
        private sealed class PluginLoadContext : AssemblyLoadContext
        {
            private readonly AssemblyDependencyResolver _resolver;

            public PluginLoadContext(string pluginPath)
                : base(name: $"TcXunitPlugin:{Path.GetFileName(pluginPath)}", isCollectible: true)
            {
                _resolver = new AssemblyDependencyResolver(pluginPath);
            }

            protected override Assembly Load(AssemblyName assemblyName)
            {
                // THE load-context rule that makes plugins work at all: any
                // assembly the host has already loaded must resolve to the
                // host's copy, never to a copy sitting beside the plugin.
                //
                // Returning null defers to the default context. If instead this
                // loaded a private copy of TcXunit.Interpreter, the plugin's
                // ITcXunitNativeFunction would be a *different type* from the
                // host's despite the identical name, so
                // `typeof(ITcXunitNativeFunction).IsAssignableFrom(pluginType)`
                // would be false and the plugin would be silently ignored -
                // the single most confusing failure mode this design has.
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
