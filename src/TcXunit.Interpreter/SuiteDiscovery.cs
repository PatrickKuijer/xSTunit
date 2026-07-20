using System.Collections.Generic;
using System.Linq;

namespace TcXunit.Interpreter
{
    // Finds FB types whose ancestry reaches the TcUnit.FB_TestSuite native
    // boundary (TcXunit-w5x.7's discovery convention: any FB extending
    // FB_TestSuite, directly or transitively, is a suite).
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
