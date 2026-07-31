using System.Collections.Generic;

namespace xStunit.Parser
{
    /// <summary>
    /// A parsed POU (function block or program): its identity, declaration
    /// and implementation text, and its methods and properties.
    /// </summary>
    public sealed class PouAst
    {
        /// <summary>The POU's name, e.g. "FB_MyTests".</summary>
        public string Name { get; }

        /// <summary>
        /// The base type named in an EXTENDS clause (e.g. "TcUnit.FB_TestSuite"),
        /// or null when the POU declares none.
        /// </summary>
        public string BaseTypeName { get; }

        /// <summary>The raw declaration text (VAR blocks), unparsed.</summary>
        public string DeclarationText { get; }

        /// <summary>The raw ST implementation body text for the POU's own top-level body, unparsed.</summary>
        public string ImplementationText { get; }

        /// <summary>The POU's declared METHODs, in declaration order.</summary>
        public IReadOnlyList<MethodAst> Methods { get; }

        /// <summary>The POU's declared PROPERTYs, in declaration order.</summary>
        public IReadOnlyList<PropertyAst> Properties { get; }

        /// <summary>
        /// 1-based line, in the originating .TcPOU file, of the first line of
        /// <see cref="ImplementationText"/> - see <see cref="MethodAst.BodyStartLine"/>
        /// for the same contract applied to the POU's own body rather than a method's.
        /// </summary>
        public int BodyStartLine { get; }

        /// <summary>Constructs a POU AST from its parsed identity, text, methods, properties, and originating file line.</summary>
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
