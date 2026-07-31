using System.Collections.Generic;

namespace xStunit.Interpreter
{
    // TYPE ... STRUCT ... END_STRUCT END_TYPE declaration - PouAst's
    // equivalent for struct types (TcXunit-w5x.15.6 / T7's design).
    public sealed class StructAst
    {
        public string Name { get; }
        public IReadOnlyList<VarDecl> Fields { get; }

        // The {attribute 'pack_mode' := 'N'} pragma preceding the TYPE
        // header (TcXunit-eub): the max byte alignment any field may impose,
        // 0 meaning "no pragma" - full natural alignment, same as TwinCAT's
        // own default. Consumed by Engine.SizeOf.cs/Engine.ByteLayout.cs as
        // a cap on each field's natural alignment (pack_mode 1 caps every
        // field to 1-byte alignment, i.e. fully byte-packed).
        public int PackMode { get; }

        public StructAst(string name, IReadOnlyList<VarDecl> fields, int packMode = 0)
        {
            Name = name;
            Fields = fields;
            PackMode = packMode;
        }
    }
}
