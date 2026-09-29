namespace xStunit.Parser
{
    /// <summary>
    /// A parsed TwinCAT PROPERTY (Get/Set accessor pair).
    /// </summary>
    /// <remarks>
    /// An accessor's implementation text is null when the .TcPOU didn't
    /// declare that accessor at all (a get-only property has no
    /// &lt;Set&gt; element), which is NOT the same as an accessor declared
    /// with an empty body - <see cref="HasGet"/>/<see cref="HasSet"/> are how
    /// callers tell those two apart.
    /// </remarks>
    public sealed class PropertyAst
    {
        public string Name { get; }

        public string DeclarationText { get; }

        public string GetImplementationText { get; }

        public string SetImplementationText { get; }

        /// <summary>
        /// The accessor's own VAR block, holding the locals its body may write.
        /// Empty (never null) when the accessor declares none or is absent.
        /// </summary>
        public string GetDeclarationText { get; }

        /// <inheritdoc cref="GetDeclarationText"/>
        public string SetDeclarationText { get; }

        public bool HasGet => GetImplementationText != null;

        public bool HasSet => SetImplementationText != null;

        /// <summary>
        /// Builds a property whose accessors declare no locals of their own.
        /// </summary>
        public PropertyAst(string name, string declarationText, string getImplementationText, string setImplementationText)
            : this(name, declarationText, getImplementationText, setImplementationText, string.Empty, string.Empty)
        {
        }

        /// <summary>
        /// Builds a property with each accessor's own VAR block; pass an empty
        /// string, not null, for an accessor that declares none.
        /// </summary>
        public PropertyAst(
            string name,
            string declarationText,
            string getImplementationText,
            string setImplementationText,
            string getDeclarationText,
            string setDeclarationText)
        {
            Name = name;
            DeclarationText = declarationText;
            GetImplementationText = getImplementationText;
            SetImplementationText = setImplementationText;
            GetDeclarationText = getDeclarationText;
            SetDeclarationText = setDeclarationText;
        }
    }
}
