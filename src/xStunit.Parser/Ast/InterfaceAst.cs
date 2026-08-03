using System.Collections.Generic;

namespace xStunit.Parser
{
    /// <summary>
    /// A parsed INTERFACE: the members an implementing POU must provide, with
    /// no bodies behind any of them.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="PouAst"/> rather than a flag on it, because an
    /// interface is not instantiable and not runnable: nothing may build one,
    /// invoke one, or discover a suite through one.
    /// </remarks>
    public sealed class InterfaceAst
    {
        public string Name { get; }

        public string DeclarationText { get; }

        /// <summary>
        /// Declared method headers. Each carries its signature text and an
        /// empty implementation - an interface method has no body to hold.
        /// </summary>
        public IReadOnlyList<MethodAst> Methods { get; }

        /// <summary>
        /// Declared properties. Their accessor bodies are empty, so
        /// <see cref="PropertyAst.HasGet"/>/<see cref="PropertyAst.HasSet"/>
        /// are what say which accessors the contract demands.
        /// </summary>
        public IReadOnlyList<PropertyAst> Properties { get; }

        public InterfaceAst(
            string name,
            string declarationText,
            IReadOnlyList<MethodAst> methods,
            IReadOnlyList<PropertyAst> properties)
        {
            Name = name;
            DeclarationText = declarationText;
            Methods = methods ?? new List<MethodAst>();
            Properties = properties ?? new List<PropertyAst>();
        }
    }
}
