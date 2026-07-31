using System.Collections.Generic;

namespace xStunit.Parser
{
    public sealed class PouAst
    {
        public string Name { get; }
        public string BaseTypeName { get; }
        public string DeclarationText { get; }
        public string ImplementationText { get; }
        public IReadOnlyList<MethodAst> Methods { get; }
        public IReadOnlyList<PropertyAst> Properties { get; }

        // See MethodAst.BodyStartLine - same contract, for the POU's own body
        // (TcXunit-p3t.3).
        public int BodyStartLine { get; }

        public PouAst(
            string name,
            string baseTypeName,
            string declarationText,
            string implementationText,
            IReadOnlyList<MethodAst> methods,
            IReadOnlyList<PropertyAst> properties = null, int bodyStartLine = 1)
        {
            Name = name;
            BaseTypeName = baseTypeName;
            DeclarationText = declarationText;
            ImplementationText = implementationText;
            Methods = methods;
            Properties = properties ?? new List<PropertyAst>();
            BodyStartLine = bodyStartLine;
        }
    }
}
