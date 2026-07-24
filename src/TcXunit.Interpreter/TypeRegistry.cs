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
        private readonly Dictionary<string, IReadOnlyList<VarDecl>> _gvls = new Dictionary<string, IReadOnlyList<VarDecl>>();

        public TypeRegistry(IEnumerable<PouAst> types, IEnumerable<StructAst> structTypes = null, IEnumerable<GvlAst> gvls = null)
        {
            foreach (var type in types)
                _types[type.Name] = type;

            if (structTypes != null)
                foreach (var structType in structTypes)
                    _structTypes[structType.Name] = structType;

            // GVL declaration text is parsed into VarDecl entries once here
            // (VAR_GLOBAL is just another VarBlockParser section,
            // TcXunit-71o), rather than re-parsing on every field access.
            if (gvls != null)
                foreach (var gvl in gvls)
                    _gvls[gvl.Name] = VarBlockParser.Parse(gvl.DeclarationText);
        }

        public PouAst Get(string name) => _types.TryGetValue(name, out var type) ? type : null;

        public StructAst GetStruct(string name) => _structTypes.TryGetValue(name, out var structType) ? structType : null;

        public IReadOnlyList<VarDecl> GetGvlDecls(string name) =>
            _gvls.TryGetValue(name, out var decls) ? decls : null;

        public IEnumerable<string> GvlNames => _gvls.Keys;
    }
}
