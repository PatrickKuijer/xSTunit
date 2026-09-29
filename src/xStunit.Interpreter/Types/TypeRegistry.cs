using System;
using System.Collections.Generic;
using xStunit.Parser;

namespace xStunit.Interpreter
{
    // Every type parsed out of the loaded sources. A type absent from here is
    // not necessarily missing: TcUnit.FB_TestSuite and the other native stubs
    // are deliberately unresolved, being backed by C# hosts instead.
    public sealed class TypeRegistry
    {
        private readonly Dictionary<string, PouAst> _types = new Dictionary<string, PouAst>(IecIdentifier.Comparer);
        private readonly Dictionary<string, StructAst> _structTypes = new Dictionary<string, StructAst>(IecIdentifier.Comparer);
        private readonly Dictionary<string, IReadOnlyList<VarDecl>> _gvls = new Dictionary<string, IReadOnlyList<VarDecl>>(IecIdentifier.Comparer);
        private readonly Dictionary<string, string> _aliases = new Dictionary<string, string>(IecIdentifier.Comparer);

        // Kept out of _types on purpose. An interface is a contract, not a
        // runnable body: sharing the POU map would let NewInstance build one,
        // let DefaultValue seed a field with one, and offer it to
        // SuiteDiscovery's ancestry walk.
        private readonly Dictionary<string, InterfaceAst> _interfaceTypes = new Dictionary<string, InterfaceAst>(IecIdentifier.Comparer);

        // User-defined ENUM DUTs: enum name -> member name -> int value, the
        // source-defined counterpart to BuiltinEnums.Types. Kept apart from
        // _aliases so that resolving EnumType.Member does not disturb the
        // SIZEOF()/ResolveAlias map.
        private readonly Dictionary<string, IReadOnlyDictionary<string, int>> _enumMembers =
            new Dictionary<string, IReadOnlyDictionary<string, int>>(IecIdentifier.Comparer);

        // Parse-once caches. CallMethod and StepCycles otherwise re-parse the
        // same body text on every invocation and every cycle. Keyed on the
        // text rather than the owning PouAst/MethodAst, since parsing is a
        // pure function of the text and two declarations sharing it share an
        // AST.
        private readonly Dictionary<string, IReadOnlyList<Stmt>> _statementCache = new Dictionary<string, IReadOnlyList<Stmt>>();
        private readonly Dictionary<string, IReadOnlyList<VarDecl>> _declCache = new Dictionary<string, IReadOnlyList<VarDecl>>();
        private readonly Dictionary<string, string> _returnTypeCache = new Dictionary<string, string>();

        // A cyclic ALIAS chain is malformed rather than expected, but a
        // malformed DUT set still must not hang ResolveAlias.
        private const int MaxAliasChainDepth = 16;

        public TypeRegistry(
            IEnumerable<PouAst> types,
            IEnumerable<StructAst> structTypes = null,
            IEnumerable<GvlAst> gvls = null,
            IEnumerable<KeyValuePair<string, string>> aliases = null,
            IEnumerable<KeyValuePair<string, IReadOnlyDictionary<string, int>>> enumMembers = null,
            IEnumerable<InterfaceAst> interfaceTypes = null)
        {
            foreach (var type in types)
                _types[type.Name] = type;

            if (structTypes != null)
                foreach (var structType in structTypes)
                    _structTypes[structType.Name] = structType;

            if (interfaceTypes != null)
                foreach (var interfaceType in interfaceTypes)
                    _interfaceTypes[interfaceType.Name] = interfaceType;

            // Parsed once here rather than on every global field access;
            // VAR_GLOBAL is just another VarBlockParser section.
            if (gvls != null)
                foreach (var gvl in gvls)
                    _gvls[gvl.Name] = VarBlockParser.Parse(gvl.DeclarationText);

            // ALIAS DUTs (e.g. "TYPE T_MaxString : STRING(255); END_TYPE"),
            // held as plain name -> underlying-type-text so ResolveAlias can
            // be threaded through the existing type-name lookups without any
            // of them knowing aliases exist.
            if (aliases != null)
                foreach (var alias in aliases)
                    _aliases[alias.Key] = alias.Value;

            if (enumMembers != null)
                foreach (var enumMember in enumMembers)
                    _enumMembers[enumMember.Key] = IecIdentifier.CopyOf(enumMember.Value);
        }

        // A library qualifier is transparent, same as it is for a native
        // function/FB/constant lookup (Engine.NativeHost.UnqualifiedTail):
        // real source spells a user-defined ancestor both with and without
        // one (TcUnit.FB_TestSuiteWithClock vs FB_TestSuiteWithClock), and
        // this registry only ever keys off the bare name. Tried only after
        // the spelling as written has missed, so a name that happens to
        // contain a dot but isn't actually qualified still resolves first
        // by its own literal spelling.
        public PouAst Get(string name)
        {
            if (_types.TryGetValue(name, out var type))
                return type;

            var tail = Engine.UnqualifiedTail(name);
            return tail != null && _types.TryGetValue(tail, out type) ? type : null;
        }

        // Follows alias-of-alias chains through to the underlying type text.
        // Returns its input unchanged (including null) when that is not a
        // known alias, so a call site can pipe any type name through this
        // without first asking whether it is one.
        public string ResolveAlias(string typeName)
        {
            var current = typeName;
            for (var i = 0; i < MaxAliasChainDepth; i++)
            {
                if (current == null || !TryResolveOneStep(current, out var underlying))
                    return current;
                current = underlying;
            }

            return current;
        }

        // A parsed ALIAS outranks a vendor one of the same name: sources we
        // loaded describe the code under test, the builtin table only fills in
        // for a library we never see.
        private bool TryResolveOneStep(string typeName, out string underlying) =>
            _aliases.TryGetValue(typeName, out underlying)
            || BuiltinAliases.TryGetUnderlyingType(typeName, out underlying);

        public StructAst GetStruct(string name) => _structTypes.TryGetValue(name, out var structType) ? structType : null;

        // Null for any name no loaded .TcIO declared, which includes an
        // interface whose file was never among the loaded directories: a miss
        // means "not known to be an interface", never "not an interface".
        public InterfaceAst GetInterface(string name) =>
            _interfaceTypes.TryGetValue(name, out var interfaceType) ? interfaceType : null;

        public bool TryGetEnumMembers(string typeName, out IReadOnlyDictionary<string, int> members) =>
            _enumMembers.TryGetValue(typeName, out members);

        public IReadOnlyList<VarDecl> GetGvlDecls(string name) =>
            _gvls.TryGetValue(name, out var decls) ? decls : null;

        public IEnumerable<string> GvlNames => _gvls.Keys;

        // Cached equivalent of Parser.ParseStatements.
        public IReadOnlyList<Stmt> GetStatements(string implementationText)
        {
            if (!_statementCache.TryGetValue(implementationText, out var statements))
            {
                statements = Parser.ParseStatements(implementationText);
                _statementCache[implementationText] = statements;
            }
            return statements;
        }

        // Cached equivalent of VarBlockParser.Parse.
        public IReadOnlyList<VarDecl> GetDecls(string declarationText)
        {
            if (!_declCache.TryGetValue(declarationText, out var decls))
            {
                decls = VarBlockParser.Parse(declarationText);
                _declCache[declarationText] = decls;
            }
            return decls;
        }

        // Cached equivalent of CallableReturnTypeParser.TryGetReturnTypeName.
        // Null means the callable declares no return type, or the text is not
        // a callable header at all.
        public string GetReturnTypeName(string declarationText)
        {
            if (!_returnTypeCache.TryGetValue(declarationText, out var typeName))
            {
                CallableReturnTypeParser.TryGetReturnTypeName(declarationText, out typeName);
                _returnTypeCache[declarationText] = typeName;
            }
            return typeName;
        }
    }
}
