namespace xStunit.Parser
{
    /// <summary>
    /// A parsed METHOD: its name, declaration text, and implementation (ST body) text.
    /// </summary>
    public sealed class MethodAst
    {
        /// <summary>The method's name, e.g. "TestSomething".</summary>
        public string Name { get; }

        /// <summary>The raw declaration text (parameter/return-type block), unparsed.</summary>
        public string DeclarationText { get; }

        /// <summary>The raw ST implementation body text, unparsed.</summary>
        public string ImplementationText { get; }

        /// <summary>
        /// 1-based line, in the originating .TcPOU file, of the first line of
        /// <see cref="ImplementationText"/>.
        /// </summary>
        /// <remarks>
        /// <c>BodyStartLine + zeroBasedLineWithinBody</c> is the real file
        /// line, so an in-body line the interpreter reports can be mapped
        /// back to the file the user actually edits. Defaults to 1 for
        /// hand-built ASTs, where the body is the whole "file" and the
        /// identity mapping is the honest answer.
        /// </remarks>
        public int BodyStartLine { get; }

        /// <summary>Constructs a method AST from its parsed name, declaration text, implementation text, and originating file line.</summary>
        public MethodAst(string name, string declarationText, string implementationText, int bodyStartLine = 1)
        {
            Name = name;
            DeclarationText = declarationText;
            ImplementationText = implementationText;
            BodyStartLine = bodyStartLine;
        }
    }
}
