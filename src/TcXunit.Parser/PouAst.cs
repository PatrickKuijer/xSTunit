namespace TcXunit.Parser
{
    public sealed class PouAst
    {
        public string Name { get; }
        public string BaseTypeName { get; }
        public string DeclarationText { get; }
        public string ImplementationText { get; }

        public PouAst(string name, string baseTypeName, string declarationText, string implementationText)
        {
            Name = name;
            BaseTypeName = baseTypeName;
            DeclarationText = declarationText;
            ImplementationText = implementationText;
        }
    }
}
