using System.Collections.Generic;

namespace xStunit.Interpreter
{
    // STRUCT value: a named-field container of Cells, the same shape as
    // FbInstance.Fields but with no method table.
    public sealed class StructInstance
    {
        public string TypeName { get; }
        public Dictionary<string, Cell> Fields { get; } = new Dictionary<string, Cell>();

        // Declared IEC type text per field, populated once in
        // BuildStructDefault and never touched afterward. Same rationale as
        // FbInstance.FieldTypeNames: a REF= binding of a struct member
        // (stWidget.ipHandler REF= fbHandler) replaces that field's entry in
        // Fields wholesale, so Cell.DeclaredTypeName afterwards describes the
        // target, not the member's own declaration. __ISVALIDREF needs the
        // latter.
        public Dictionary<string, string> FieldTypeNames { get; } = new Dictionary<string, string>();

        // Non-null exactly for a UNION instance, whose members are views onto
        // one buffer rather than the independent Cells a STRUCT's fields are.
        // The byte model reads the union's value off here, because packing the
        // members one at a time would write every one of them to offset 0 and
        // leave only the last declared.
        internal UnionStorage Overlay { get; set; }

        public StructInstance(string typeName)
        {
            TypeName = typeName;
        }
    }
}
