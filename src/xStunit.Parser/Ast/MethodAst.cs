namespace xStunit.Parser
{
    /// <summary>
    /// A parsed METHOD. <see cref="DeclarationText"/> and
    /// <see cref="ImplementationText"/> are raw, unparsed text.
    /// </summary>
    public sealed class MethodAst
    {
        public string Name { get; }

        public string DeclarationText { get; }

        public string ImplementationText { get; }

        /// <summary>
        /// 1-based line, in the originating .TcPOU file, of the first line of
        /// <see cref="ImplementationText"/>, such that
        /// <c>BodyStartLine + zeroBasedLineWithinBody</c> is the real file line.
        /// </summary>
        /// <remarks>
        /// Defaults to 1 for hand-built ASTs, where the body is the whole
        /// "file" and the identity mapping is the honest answer.
        /// </remarks>
        public int BodyStartLine { get; }

        public MethodAst(string name, string declarationText, string implementationText, int bodyStartLine = 1)
        {
            Name = name;
            DeclarationText = declarationText;
            ImplementationText = implementationText;
            BodyStartLine = bodyStartLine;
        }
    }
}
