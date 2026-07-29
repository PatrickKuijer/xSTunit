using System.Collections.Generic;

namespace TcXunit.Interpreter
{
    // STRUCT value: a named-field container of Cells, same shape as
    // FbInstance.Fields but without a method table (T7's struct/array Cell
    // shape decision - TcXunit-w5x.15.6).
    public sealed class StructInstance
    {
        public string TypeName { get; }
        public Dictionary<string, Cell> Fields { get; } = new Dictionary<string, Cell>();

        // Declared IEC type text for each entry in Fields, indexed by name -
        // populated once in BuildStructDefault and never touched afterward,
        // same rationale as FbInstance.FieldTypeNames (TcXunit-6t0): a REF=
        // binding of a struct member (stWidget.ipHandler REF= fbHandler)
        // replaces that field's Cell in Fields wholesale, which would
        // otherwise erase the field's own declared type in favor of
        // whatever it now points at. __ISVALIDREF needs the former, not the
        // latter, to validate that the *member being asked about* was
        // actually declared REFERENCE TO/POINTER TO.
        public Dictionary<string, string> FieldTypeNames { get; } = new Dictionary<string, string>();

        public StructInstance(string typeName)
        {
            TypeName = typeName;
        }
    }
}
