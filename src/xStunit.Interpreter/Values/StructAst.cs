using System.Collections.Generic;

namespace xStunit.Interpreter
{
    // A parsed TYPE ... STRUCT ... END_STRUCT END_TYPE declaration - PouAst's
    // equivalent for struct types.
    public sealed class StructAst
    {
        public string Name { get; }
        public IReadOnlyList<VarDecl> Fields { get; }

        // From the {attribute 'pack_mode' := 'N'} pragma preceding the TYPE
        // header: a cap, in bytes, on the alignment any field may impose, so
        // pack_mode 1 byte-packs the struct entirely. 0 means no pragma was
        // present and each field keeps its natural alignment. Consumed by
        // Engine.SizeOf.cs and Engine.ByteLayout.cs.
        public int PackMode { get; }

        public StructAst(string name, IReadOnlyList<VarDecl> fields, int packMode = 0)
        {
            Name = name;
            Fields = fields;
            PackMode = packMode;
        }
    }
}
