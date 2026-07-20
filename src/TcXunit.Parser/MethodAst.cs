namespace TcXunit.Parser
{
    public sealed class MethodAst
    {
        public string Name { get; }
        public string DeclarationText { get; }
        public string ImplementationText { get; }

        public MethodAst(string name, string declarationText, string implementationText)
        {
            Name = name;
            DeclarationText = declarationText;
            ImplementationText = implementationText;
        }
    }
}
