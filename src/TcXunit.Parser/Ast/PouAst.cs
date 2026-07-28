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
        public IReadOnlyList<PropertyAst> Properties { get; }

        public PouAst(
            string name,
            string baseTypeName,
            string declarationText,
            string implementationText,
            IReadOnlyList<MethodAst> methods,
            IReadOnlyList<PropertyAst> properties = null)
        {
            Name = name;
            BaseTypeName = baseTypeName;
            DeclarationText = declarationText;
            ImplementationText = implementationText;
            Methods = methods;
            Properties = properties ?? new List<PropertyAst>();
        }
    }
}
