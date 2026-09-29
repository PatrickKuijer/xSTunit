using System.Collections.Generic;
using System.Linq;
using xStunit.Parser;

namespace xStunit.Interpreter
{
    // Drops every POU whose IMPLEMENTS clause names a loaded interface it does
    // not fully provide, reporting each as a skipped file. An FB whose EXTENDS
    // chain leaves the loaded set, other than at a known native base, is not
    // checked.
    internal static class ImplementsConformance
    {
        public static List<LoadedPou> Filter(
            IReadOnlyList<LoadedPou> loaded,
            IReadOnlyList<InterfaceAst> interfaces,
            List<SkippedFile> skipped)
        {
            var interfacesByName = interfaces.ToDictionary(i => i.Name, IecIdentifier.Comparer);
            var registry = new TypeRegistry(loaded.Select(l => l.Pou));
            var conformant = new List<LoadedPou>();

            foreach (var entry in loaded)
            {
                var violations = FindViolations(entry.Pou, interfacesByName, registry);
                if (violations.Count == 0)
                {
                    conformant.Add(entry);
                    continue;
                }

                skipped.Add(new SkippedFile(
                    entry.FilePath,
                    $"{entry.Pou.Name} does not conform to its IMPLEMENTS clause: {string.Join("; ", violations)}"));
            }

            return conformant;
        }

        private static List<string> FindViolations(
            PouAst pou, IReadOnlyDictionary<string, InterfaceAst> interfacesByName, TypeRegistry registry)
        {
            var violations = new List<string>();
            if (pou.ImplementedInterfaces.Count == 0)
                return violations;

            if (!TryCollectChain(pou, registry, out var chain))
                return violations;

            foreach (var interfaceName in pou.ImplementedInterfaces)
            {
                if (!TryResolveInterface(interfaceName, interfacesByName, out var itf))
                    continue;

                var missing = MissingMembers(itf, chain);
                if (missing.Count > 0)
                    violations.Add($"{itf.Name} (missing {string.Join(", ", missing)})");
            }

            return violations;
        }

        private static bool TryCollectChain(PouAst pou, TypeRegistry registry, out List<PouAst> chain)
        {
            chain = new List<PouAst>();
            var current = pou;
            while (current != null)
            {
                if (chain.Contains(current))
                    return false;

                chain.Add(current);
                if (current.BaseTypeName == null)
                    return true;

                var baseTypeName = current.BaseTypeName;
                current = registry.Get(baseTypeName);
                if (current == null)
                    return IsKnownNativeBase(baseTypeName);
            }

            return false;
        }

        private static bool IsKnownNativeBase(string typeName)
        {
            var bareName = Engine.UnqualifiedTail(typeName) ?? typeName;
            return IecIdentifier.Matches(typeName, SuiteDiscovery.TestSuiteBaseType)
                || IecIdentifier.Matches(bareName, SuiteDiscovery.TestSuiteBaseTypeBareName)
                || Engine.IsBuiltinNativeFbTypeName(bareName);
        }

        private static bool TryResolveInterface(
            string name, IReadOnlyDictionary<string, InterfaceAst> interfacesByName, out InterfaceAst itf)
        {
            if (interfacesByName.TryGetValue(name, out itf))
                return true;

            var tail = Engine.UnqualifiedTail(name);
            return tail != null && interfacesByName.TryGetValue(tail, out itf);
        }

        private static List<string> MissingMembers(InterfaceAst itf, List<PouAst> chain)
        {
            var missing = new List<string>();

            foreach (var method in itf.Methods)
            {
                if (!chain.Any(p => p.Methods.Any(m => IecIdentifier.Matches(m.Name, method.Name))))
                    missing.Add($"method {method.Name}");
            }

            foreach (var property in itf.Properties)
            {
                var provided = chain
                    .SelectMany(p => p.Properties)
                    .Where(p => IecIdentifier.Matches(p.Name, property.Name))
                    .ToList();

                if (provided.Count == 0)
                {
                    missing.Add($"property {property.Name}");
                    continue;
                }

                if (property.HasGet && !provided.Any(p => p.HasGet))
                    missing.Add($"property {property.Name} accessor Get");
                if (property.HasSet && !provided.Any(p => p.HasSet))
                    missing.Add($"property {property.Name} accessor Set");
            }

            return missing;
        }
    }
}
