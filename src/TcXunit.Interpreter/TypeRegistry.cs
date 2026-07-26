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
        private readonly Dictionary<string, string> _aliases = new Dictionary<string, string>();

        // Chained-alias (alias-of-alias) resolution depth cap (TcXunit-6hg):
        // no real IEC 61131-3 project defines a self-referential/cyclic
        // ALIAS chain, but a malformed .TcDUT set shouldn't be able to hang
        // ResolveAlias in an infinite loop either.
        private const int MaxAliasChainDepth = 16;

        public TypeRegistry(
            IEnumerable<PouAst> types,
            IEnumerable<StructAst> structTypes = null,
            IEnumerable<GvlAst> gvls = null,
            IEnumerable<KeyValuePair<string, string>> aliases = null)
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

            // ALIAS DUTs (e.g. "TYPE T_MaxString : STRING(255); END_TYPE",
            // TcXunit-6hg): stored as a plain name -> underlying-type-text
            // map so ResolveAlias can be threaded through every existing
            // type-name lookup (scalar defaults, StringTypeInfo, GetStruct,
            // ...) without those call sites needing to know aliases exist.
            if (aliases != null)
                foreach (var alias in aliases)
                    _aliases[alias.Key] = alias.Value;
        }

        public PouAst Get(string name) => _types.TryGetValue(name, out var type) ? type : null;

        // Resolves a possibly-aliased type name through to its underlying
        // type text, following alias-of-alias chains. Returns the input
        // unchanged (including null) when it isn't a known alias, so every
        // call site can unconditionally run its result through this method
        // first without a separate "is this an alias" branch.
        public string ResolveAlias(string typeName)
        {
            var current = typeName;
            for (var i = 0; i < MaxAliasChainDepth; i++)
            {
                if (current == null || !_aliases.TryGetValue(current, out var underlying))
                    return current;
                current = underlying;
            }

            return current;
        }

        public StructAst GetStruct(string name) => _structTypes.TryGetValue(name, out var structType) ? structType : null;

        public IReadOnlyList<VarDecl> GetGvlDecls(string name) =>
            _gvls.TryGetValue(name, out var decls) ? decls : null;

        public IEnumerable<string> GvlNames => _gvls.Keys;
    }
}
