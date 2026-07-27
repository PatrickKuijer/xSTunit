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

        // User-defined ENUM DUT member tables (enum-name -> member-name ->
        // int-value, TcXunit-rk3), populated from DutEnumLoader.Load
        // alongside the _aliases merge above - lets Engine.Expressions.cs
        // resolve a qualified enum literal (EnumType.Member) the same way
        // it already resolves BuiltinEnums.Types entries, without disturbing
        // the existing SIZEOF()/ResolveAlias alias map.
        private readonly Dictionary<string, IReadOnlyDictionary<string, int>> _enumMembers =
            new Dictionary<string, IReadOnlyDictionary<string, int>>();

        // Parse-once caches (TcXunit-6af.4): Parser.ParseStatements/
        // VarBlockParser.Parse are pure functions of their input text, but
        // CallMethod/StepCycles/RunSuite re-parsed the same POU/method
        // ImplementationText/DeclarationText string on every single
        // invocation/cycle. Cache keyed on the text itself (not the owning
        // PouAst/MethodAst instance) since the mapping is content -> AST
        // regardless of which declaration the text came from.
        private readonly Dictionary<string, IReadOnlyList<Stmt>> _statementCache = new Dictionary<string, IReadOnlyList<Stmt>>();
        private readonly Dictionary<string, IReadOnlyList<VarDecl>> _declCache = new Dictionary<string, IReadOnlyList<VarDecl>>();

        // Chained-alias (alias-of-alias) resolution depth cap (TcXunit-6hg):
        // no real IEC 61131-3 project defines a self-referential/cyclic
        // ALIAS chain, but a malformed .TcDUT set shouldn't be able to hang
        // ResolveAlias in an infinite loop either.
        private const int MaxAliasChainDepth = 16;

        public TypeRegistry(
            IEnumerable<PouAst> types,
            IEnumerable<StructAst> structTypes = null,
            IEnumerable<GvlAst> gvls = null,
            IEnumerable<KeyValuePair<string, string>> aliases = null,
            IEnumerable<KeyValuePair<string, IReadOnlyDictionary<string, int>>> enumMembers = null)
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

            if (enumMembers != null)
                foreach (var enumMember in enumMembers)
                    _enumMembers[enumMember.Key] = enumMember.Value;
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

        // Looks up a user-defined ENUM DUT's member-name -> int-value table
        // by enum type name (TcXunit-rk3) - the DUT-sourced counterpart to
        // BuiltinEnums.Types, consulted by Engine.Expressions.cs's
        // FieldAccessExpr case for a qualified enum literal.
        public bool TryGetEnumMembers(string typeName, out IReadOnlyDictionary<string, int> members) =>
            _enumMembers.TryGetValue(typeName, out members);

        public IReadOnlyList<VarDecl> GetGvlDecls(string name) =>
            _gvls.TryGetValue(name, out var decls) ? decls : null;

        public IEnumerable<string> GvlNames => _gvls.Keys;

        // Cached equivalent of Parser.ParseStatements(implementationText) -
        // parses once per distinct implementation text, reused on every
        // subsequent call with the same text.
        public IReadOnlyList<Stmt> GetStatements(string implementationText)
        {
            if (!_statementCache.TryGetValue(implementationText, out var statements))
            {
                statements = Parser.ParseStatements(implementationText);
                _statementCache[implementationText] = statements;
            }
            return statements;
        }

        // Cached equivalent of VarBlockParser.Parse(declarationText) - same
        // parse-once rationale as GetStatements.
        public IReadOnlyList<VarDecl> GetDecls(string declarationText)
        {
            if (!_declCache.TryGetValue(declarationText, out var decls))
            {
                decls = VarBlockParser.Parse(declarationText);
                _declCache[declarationText] = decls;
            }
            return decls;
        }
    }
}
