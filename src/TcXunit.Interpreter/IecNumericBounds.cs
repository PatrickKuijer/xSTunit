using System.Collections.Generic;

namespace TcXunit.Interpreter
{
    // Documented IEC 61131-3 min/max per numeric type name, for
    // StructBoundaryBuilder (TcXunit-w5x.15.10 / T7's design: "no such bounds
    // table exists yet"). SINT/USINT/BYTE/INT/UINT/WORD/DINT are boxed as
    // C# int to match the rest of the interpreter's unchecked-int treatment
    // of narrow integer types (Engine.DefaultValue); UDINT/DWORD/LINT/ULINT/
    // LWORD exceed Int32 range and are boxed as their natural wider CLR type
    // instead, since no other interpreter path assumes int for those yet.
    internal static class IecNumericBounds
    {
        private static readonly Dictionary<string, (object Min, object Max)> Bounds =
            new Dictionary<string, (object Min, object Max)>
            {
                ["SINT"] = ((int)sbyte.MinValue, (int)sbyte.MaxValue),
                ["USINT"] = ((int)byte.MinValue, (int)byte.MaxValue),
                ["BYTE"] = ((int)byte.MinValue, (int)byte.MaxValue),
                ["INT"] = ((int)short.MinValue, (int)short.MaxValue),
                ["UINT"] = ((int)ushort.MinValue, (int)ushort.MaxValue),
                ["WORD"] = ((int)ushort.MinValue, (int)ushort.MaxValue),
                ["DINT"] = (int.MinValue, int.MaxValue),
                ["UDINT"] = ((long)uint.MinValue, (long)uint.MaxValue),
                ["DWORD"] = ((long)uint.MinValue, (long)uint.MaxValue),
                ["LINT"] = (long.MinValue, long.MaxValue),
                ["ULINT"] = (ulong.MinValue, ulong.MaxValue),
                ["LWORD"] = (ulong.MinValue, ulong.MaxValue),
                ["REAL"] = (float.MinValue, float.MaxValue),
                ["LREAL"] = (double.MinValue, double.MaxValue),
            };

        public static bool TryGetBounds(string typeName, out (object Min, object Max) bounds) =>
            Bounds.TryGetValue(typeName, out bounds);
    }
}
