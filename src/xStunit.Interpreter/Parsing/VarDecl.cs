namespace xStunit.Interpreter
{
    public sealed class VarDecl
    {
        public string Name { get; }
        public string TypeName { get; }
        public string DefaultValueText { get; }
        public VarSection Section { get; }
        public string InitArgumentsText { get; }

        // The declaration as read from the source: trimmed and stripped of
        // leading pragmas and comments, otherwise verbatim. Null for a
        // VarDecl not read from source.
        public string SourceText { get; }

        public VarDecl(string name, string typeName, string defaultValueText, VarSection section)
            : this(name, typeName, defaultValueText, section, null, null)
        {
        }

        public VarDecl(
            string name,
            string typeName,
            string defaultValueText,
            VarSection section,
            string initArgumentsText,
            string sourceText)
        {
            Name = name;
            TypeName = typeName;
            DefaultValueText = defaultValueText;
            Section = section;
            InitArgumentsText = initArgumentsText;
            SourceText = sourceText;
        }
    }
}
