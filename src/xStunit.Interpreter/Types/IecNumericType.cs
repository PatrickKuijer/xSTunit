using System;
using System.Collections.Generic;

namespace xStunit.Interpreter
{
    // Single source of truth for how each IEC 61131-3 numeric type is
    // represented in the CLR: zero value and min/max, one entry per type name.
    // The boxed CLR type is what the Default entry's own type says, and it is
    // the widest thing every value of that IEC type fits in, not a
    // width-for-width match: SINT/USINT/BYTE/INT/UINT/WORD/DINT all box as
    // int, so a BYTE and a DINT are indistinguishable once boxed, while
    // UDINT/DWORD/LINT box as long and ULINT/LWORD as ulong because their
    // ranges overflow Int32. Anything keying behaviour off the declared IEC
    // type (range checks, byte layout) must carry the type name, not the box.
    internal static class IecNumericType
    {
        private static readonly Dictionary<string, (object Default, object Min, object Max)> Types =
            new Dictionary<string, (object Default, object Min, object Max)>(IecIdentifier.Comparer)
            {
                ["SINT"] = (0, (int)sbyte.MinValue, (int)sbyte.MaxValue),
                ["USINT"] = (0, (int)byte.MinValue, (int)byte.MaxValue),
                ["BYTE"] = (0, (int)byte.MinValue, (int)byte.MaxValue),
                ["INT"] = (0, (int)short.MinValue, (int)short.MaxValue),
                ["UINT"] = (0, (int)ushort.MinValue, (int)ushort.MaxValue),
                ["WORD"] = (0, (int)ushort.MinValue, (int)ushort.MaxValue),
                ["DINT"] = (0, int.MinValue, int.MaxValue),
                ["UDINT"] = (0L, (long)uint.MinValue, (long)uint.MaxValue),
                ["DWORD"] = (0L, (long)uint.MinValue, (long)uint.MaxValue),
                ["LINT"] = (0L, long.MinValue, long.MaxValue),
                ["ULINT"] = (0UL, ulong.MinValue, ulong.MaxValue),
                ["LWORD"] = (0UL, ulong.MinValue, ulong.MaxValue),
                ["REAL"] = (0f, float.MinValue, float.MaxValue),
                ["LREAL"] = (0d, double.MinValue, double.MaxValue),
            };

        public static bool TryGetDefault(string typeName, out object defaultValue)
        {
            if (Types.TryGetValue(typeName, out var entry))
            {
                defaultValue = entry.Default;
                return true;
            }

            defaultValue = null;
            return false;
        }

        public static bool TryGetBounds(string typeName, out (object Min, object Max) bounds)
        {
            if (Types.TryGetValue(typeName, out var entry))
            {
                bounds = (entry.Min, entry.Max);
                return true;
            }

            bounds = default;
            return false;
        }
    }
}
