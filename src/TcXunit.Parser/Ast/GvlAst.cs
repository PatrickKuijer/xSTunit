namespace TcXunit.Parser
{
    // A parsed .TcGVL file's identity + raw declaration text (the
    // "VAR_GLOBAL ... END_VAR" body, alongside any {attribute ...} pragmas
    // and/or CONSTANT/RETAIN/PERSISTENT modifiers). Interpreting that
    // declaration text into VarDecl entries is left to callers
    // (TcXunit.Interpreter's VarBlockParser), same split as DutAst/
    // StructDeclParser (TcXunit-71o).
    public readonly struct GvlAst
    {
        public GvlAst(string name, string declarationText)
        {
            Name = name;
            DeclarationText = declarationText;
        }

        public string Name { get; }
        public string DeclarationText { get; }
    }
}
