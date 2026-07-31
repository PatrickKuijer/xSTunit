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

        public bool HasGet => GetImplementationText != null;

        public bool HasSet => SetImplementationText != null;

        public PropertyAst(string name, string declarationText, string getImplementationText, string setImplementationText)
        {
            Name = name;
            DeclarationText = declarationText;
            GetImplementationText = getImplementationText;
            SetImplementationText = setImplementationText;
        }
    }
}
