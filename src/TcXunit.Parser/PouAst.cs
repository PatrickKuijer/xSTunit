namespace TcXunit.Parser
{
    public sealed class PouAst
    {
        public string Name { get; }
        public string DeclarationText { get; }
        public string ImplementationText { get; }

        public PouAst(string name, string declarationText, string implementationText)
        {
            Name = name;
            DeclarationText = declarationText;
            ImplementationText = implementationText;
        }
    }
}
