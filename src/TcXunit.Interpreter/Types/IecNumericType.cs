using System;
using System.Collections.Generic;

namespace TcXunit.Interpreter
{
    // Single source of truth for IEC 61131-3 numeric type -> CLR
    // representation (TcXunit-6af.1): default/zero value and documented
    // min/max, one entry per type name. Previously Engine.DefaultValue,
    // IecNumericBounds, and StructBoundaryBuilder each re-derived this table
    // and disagreed (e.g. UDINT defaulted to C# int 0 via Engine.DefaultValue
    // but boxed as long via StructBoundaryBuilder/IecNumericBounds).
    // SINT/USINT/BYTE/INT/UINT/WORD/DINT are boxed as C# int (fits Int32);
    // UDINT/DWORD/LINT/ULINT/LWORD exceed Int32 range and are boxed as their
    // natural wider CLR type instead.
    internal static class IecNumericType
    {
        // TcXunit-fzm: IEC 61131-3 type names are case-insensitive (a VAR
        // declared 'lreal' or 'LReal' is exactly as valid as 'LREAL'), so
        // this table - and every other type-name lookup in the interpreter -
        // compares with StringComparer.OrdinalIgnoreCase rather than the
        // default ordinal comparer.
        private static readonly Dictionary<string, (object Default, object Min, object Max)> Types =
            new Dictionary<string, (object Default, object Min, object Max)>(StringComparer.OrdinalIgnoreCase)
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
