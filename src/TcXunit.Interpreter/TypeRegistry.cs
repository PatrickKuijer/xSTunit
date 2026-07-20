using System.Collections.Generic;
using TcXunit.Parser;

namespace TcXunit.Interpreter
{
    // Types not present here (TcUnit.FB_TestSuite) are the native-stub
    // boundary (TcXunit-w5x.7) - not missing types, deliberately unresolved.
    public sealed class TypeRegistry
    {
        private readonly Dictionary<string, PouAst> _types = new Dictionary<string, PouAst>();

        public TypeRegistry(IEnumerable<PouAst> types)
        {
            foreach (var type in types)
                _types[type.Name] = type;
        }

        public PouAst Get(string name) => _types.TryGetValue(name, out var type) ? type : null;
    }
}
