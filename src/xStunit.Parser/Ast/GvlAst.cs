namespace xStunit.Parser
{
    /// <summary>
    /// A parsed .TcGVL file's name plus its raw, UNPARSED declaration text -
    /// the "VAR_GLOBAL ... END_VAR" body with any pragmas and
    /// CONSTANT/RETAIN/PERSISTENT modifiers still in it.
    /// </summary>
    /// <remarks>
    /// Turning that text into individual variable declarations is the
    /// caller's job (xStunit.Interpreter's VarBlockParser), the same split
    /// <see cref="DutAst"/> uses.
    /// </remarks>
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
