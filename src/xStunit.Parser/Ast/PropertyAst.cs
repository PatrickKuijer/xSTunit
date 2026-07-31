namespace xStunit.Parser
{
    // TwinCAT PROPERTY (Get/Set) member (TcXunit-sxv). Either accessor's
    // implementation text is null when the .TcPOU didn't declare that
    // accessor (e.g. a get-only property has no <Set> element) - HasGet/
    // HasSet let callers distinguish "no accessor" from "empty body".
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
