namespace TcXunit.Parser
{
    public sealed class MethodAst
    {
        public string Name { get; }
        public string DeclarationText { get; }
        public string ImplementationText { get; }

        // 1-based line, in the originating .TcPOU file, of the FIRST line of
        // ImplementationText: BodyStartLine + zeroBasedLineWithinBody is the
        // real file line, so an in-body line from the interpreter can be
        // reported against the file the user actually edits (TcXunit-p3t.3).
        // Defaults to 1 for hand-built ASTs, where the body is the whole
        // "file" and the identity mapping is the honest answer.
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
