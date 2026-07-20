using System.Collections.Generic;

namespace TcXunit.Parser
{
    public sealed class PouAst
    {
        public string Name { get; }
        public string BaseTypeName { get; }
        public string DeclarationText { get; }
        public string ImplementationText { get; }
        public IReadOnlyList<MethodAst> Methods { get; }

        public PouAst(string name, string baseTypeName, string declarationText, string implementationText, IReadOnlyList<MethodAst> methods)
        {
            Name = name;
            BaseTypeName = baseTypeName;
            DeclarationText = declarationText;
            ImplementationText = implementationText;
            Methods = methods;
        }
    }
}
