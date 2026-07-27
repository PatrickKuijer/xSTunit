namespace TcXunit.Interpreter
{
    public sealed class VarDecl
    {
        public string Name { get; }
        public string TypeName { get; }
        public string DefaultValueText { get; }
        public VarSection Section { get; }

        public VarDecl(string name, string typeName, string defaultValueText, VarSection section)
        {
            Name = name;
            TypeName = typeName;
            DefaultValueText = defaultValueText;
            Section = section;
        }
    }
}
