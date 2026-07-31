namespace xStunit.Parser
{
    /// <summary>
    /// A parsed .TcGVL file's identity plus its raw declaration text.
    /// </summary>
    /// <remarks>
    /// <see cref="DeclarationText"/> is the "VAR_GLOBAL ... END_VAR" body,
    /// alongside any {attribute ...} pragmas and/or CONSTANT/RETAIN/PERSISTENT
    /// modifiers. Interpreting that text into individual variable
    /// declarations is left to callers (xStunit.Interpreter's
    /// VarBlockParser) - the same declaration-text-only split
    /// <see cref="DutAst"/> uses for STRUCT/ENUM/alias DUTs.
    /// </remarks>
    public readonly struct GvlAst
    {
        /// <summary>Constructs a GVL AST from its parsed name and raw declaration text.</summary>
        public GvlAst(string name, string declarationText)
        {
            Name = name;
            DeclarationText = declarationText;
        }

        /// <summary>The GVL's name, e.g. "GVL_Constants".</summary>
        public string Name { get; }

        /// <summary>The raw "VAR_GLOBAL ... END_VAR" declaration text, unparsed.</summary>
        public string DeclarationText { get; }
    }
}
