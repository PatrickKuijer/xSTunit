using System.Collections.Generic;

namespace xStunit.Interpreter
{
    // A parsed TYPE ... STRUCT ... END_STRUCT END_TYPE declaration - PouAst's
    // equivalent for struct types. A UNION declaration lands here too, as a
    // field list that differs only in how it is laid out.
    public sealed class StructAst
    {
        public string Name { get; }
        public IReadOnlyList<VarDecl> Fields { get; }

        // From the {attribute 'pack_mode' := 'N'} pragma preceding the TYPE
        // header: a cap, in bytes, on the alignment any field may impose, so
        // pack_mode 1 byte-packs the struct entirely. 0 means no pragma was
        // present and each field keeps its natural alignment. Consumed by
        // TypeLayout.PackBound.
        public int PackMode { get; }

        // A UNION overlays its fields: every one starts at offset 0, and the
        // type is as wide as its widest field and imposes that field's
        // alignment on whatever holds it. Nothing else about the declaration
        // says so - the field list alone reads as a struct.
        public bool IsUnion { get; }

        public StructAst(string name, IReadOnlyList<VarDecl> fields, int packMode = 0, bool isUnion = false)
        {
            Name = name;
            Fields = fields;
            PackMode = packMode;
            IsUnion = isUnion;
        }
    }
}
