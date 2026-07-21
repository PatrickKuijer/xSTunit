using System.Collections.Generic;
using TcXunit.Parser;

namespace TcXunit.Interpreter
{
    // Types not present here (TcUnit.FB_TestSuite) are the native-stub
    // boundary (TcXunit-w5x.7) - not missing types, deliberately unresolved.
    public sealed class TypeRegistry
    {
        private readonly Dictionary<string, PouAst> _types = new Dictionary<string, PouAst>();
        private readonly Dictionary<string, StructAst> _structTypes = new Dictionary<string, StructAst>();

        public TypeRegistry(IEnumerable<PouAst> types, IEnumerable<StructAst> structTypes = null)
        {
            foreach (var type in types)
                _types[type.Name] = type;

            if (structTypes != null)
                foreach (var structType in structTypes)
                    _structTypes[structType.Name] = structType;
        }

        public PouAst Get(string name) => _types.TryGetValue(name, out var type) ? type : null;

        public StructAst GetStruct(string name) => _structTypes.TryGetValue(name, out var structType) ? structType : null;
    }
}
