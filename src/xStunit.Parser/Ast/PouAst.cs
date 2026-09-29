using System.Collections.Generic;

namespace xStunit.Parser
{
    /// <summary>
    /// A parsed POU (function block or program). <see cref="DeclarationText"/>
    /// and <see cref="ImplementationText"/> are raw, unparsed text.
    /// </summary>
    public sealed class PouAst
    {
        public string Name { get; }

        /// <summary>
        /// The type named in the POU's EXTENDS clause (e.g.
        /// "TcUnit.FB_TestSuite"), or null when it declares none.
        /// </summary>
        public string BaseTypeName { get; }

        /// <summary>
        /// The interfaces named in the POU's IMPLEMENTS clause, in declared
        /// order; empty when it declares none.
        /// </summary>
        public IReadOnlyList<string> ImplementedInterfaces { get; }

        /// <summary>
        /// What the declaration header names. A declaration with no
        /// recognisable header is taken to be a <see cref="PouKind.FunctionBlock"/>.
        /// </summary>
        public PouKind Kind { get; }

        public string DeclarationText { get; }

        public string ImplementationText { get; }

        public IReadOnlyList<MethodAst> Methods { get; }

        public IReadOnlyList<PropertyAst> Properties { get; }

        /// <summary>
        /// Same contract as <see cref="MethodAst.BodyStartLine"/>, for the
        /// POU's own body rather than a method's.
        /// </summary>
        public int BodyStartLine { get; }

        public PouAst(
            string name,
            string baseTypeName,
            string declarationText,
            string implementationText,
            IReadOnlyList<MethodAst> methods,
            IReadOnlyList<PropertyAst> properties = null, int bodyStartLine = 1,
            IReadOnlyList<string> implementedInterfaces = null,
            PouKind kind = PouKind.FunctionBlock)
        {
            Kind = kind;
            ImplementedInterfaces = implementedInterfaces ?? new List<string>();
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
