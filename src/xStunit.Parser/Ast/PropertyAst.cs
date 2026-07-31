namespace xStunit.Parser
{
    /// <summary>
    /// A parsed TwinCAT PROPERTY (Get/Set accessor pair) member.
    /// </summary>
    /// <remarks>
    /// Either accessor's implementation text is null when the .TcPOU didn't
    /// declare that accessor (e.g. a get-only property has no &lt;Set&gt;
    /// element) - <see cref="HasGet"/>/<see cref="HasSet"/> let callers
    /// distinguish "no accessor" from "declared with an empty body".
    /// </remarks>
    public sealed class PropertyAst
    {
        /// <summary>The property's name, e.g. "Value".</summary>
        public string Name { get; }

        /// <summary>The raw declaration text, unparsed.</summary>
        public string DeclarationText { get; }

        /// <summary>The raw ST implementation body text of the Get accessor, or null when the property has none.</summary>
        public string GetImplementationText { get; }

        /// <summary>The raw ST implementation body text of the Set accessor, or null when the property has none.</summary>
        public string SetImplementationText { get; }

        /// <summary>Whether the property declares a Get accessor.</summary>
        public bool HasGet => GetImplementationText != null;

        /// <summary>Whether the property declares a Set accessor.</summary>
        public bool HasSet => SetImplementationText != null;

        /// <summary>Constructs a property AST from its parsed name, declaration text, and accessor implementation texts.</summary>
        public PropertyAst(string name, string declarationText, string getImplementationText, string setImplementationText)
        {
            Name = name;
            DeclarationText = declarationText;
            GetImplementationText = getImplementationText;
            SetImplementationText = setImplementationText;
        }
    }
}
