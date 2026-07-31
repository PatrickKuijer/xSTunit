using System.Collections.Generic;
using System.Linq;

namespace xStunit.Interpreter
{
    // The discovery convention: an FB is a test suite when following its
    // EXTENDS chain through the TypeRegistry reaches TcUnit.FB_TestSuite,
    // directly or transitively. That base type is a native boundary with no
    // registered definition of its own, so the walk matches it by name and
    // stops as soon as a link is missing from the registry.
    public static class SuiteDiscovery
    {
        private const string TestSuiteBaseType = "TcUnit.FB_TestSuite";

        public static IReadOnlyList<string> FindSuiteTypeNames(TypeRegistry registry, IEnumerable<string> candidateTypeNames) =>
            candidateTypeNames.Where(name => IsSuiteType(registry, name)).ToList();

        public static bool IsSuiteType(TypeRegistry registry, string typeName)
        {
            var current = typeName;
            while (current != null)
            {
                if (current == TestSuiteBaseType)
                    return true;

                var def = registry.Get(current);
                if (def == null)
                    return false;

                current = def.BaseTypeName;
            }

            return false;
        }
    }
}
