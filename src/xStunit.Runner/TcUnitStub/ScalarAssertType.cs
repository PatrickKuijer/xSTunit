using System;
using System.Collections.Generic;

namespace xStunit.Runner.TcUnitStub
{
    // Table-driven registry backing FB_TestSuite.AssertEqualsScalar's
    // per-type dispatch (TcXunit-gd2.11). Before this, each scalar type
    // (INT/BOOL/STRING/REAL) needed its own hand-written AssertEquals_<TYPE>
    // method repeated across FB_TestSuite, TcUnitSuiteHost and
    // NativeMethodBridge - adding the ~17 remaining IEC scalar types the old
    // way would mean ~51 new methods. Each entry here captures what used to
    // be one hand-written method's behavior: how to compare (with any
    // type-specific wrapping, e.g. INT's 16-bit wraparound truncation),
    // whether the type takes a Delta argument (REAL) or compares exactly,
    // and how to format expected/actual for the Fail() message. Adding a new
    // scalar type means adding one entry here, not new methods in three
    // files.
    public sealed class ScalarAssertType
    {
        public string Name { get; }

        // True when the type compares within a tolerance (Delta arg
        // required), false when it compares exactly.
        public bool HasDelta { get; }

        // Compares expected/actual (applying any type-specific wrap/rounding
        // rule), consulting delta only when HasDelta is true. Returns true
        // when the assertion should PASS.
        public Func<object, object, object, bool> AreEqual { get; }

        // Formats the expected value (and delta, when HasDelta) for the
        // Fail() message.
        public Func<object, object, string> FormatExpected { get; }

        // Formats the actual value for the Fail() message.
        public Func<object, string> FormatActual { get; }

        private ScalarAssertType(
            string name,
            bool hasDelta,
            Func<object, object, object, bool> areEqual,
            Func<object, object, string> formatExpected,
            Func<object, string> formatActual)
        {
            Name = name;
            HasDelta = hasDelta;
            AreEqual = areEqual;
            FormatExpected = formatExpected;
            FormatActual = formatActual;
        }

        private static string FormatBool(bool value) => value ? "TRUE" : "FALSE";

        // REAL/LREAL formatting (TcXunit-gd2.7 fix): plain ToString() uses
        // CurrentCulture, which renders "." as "," on e.g. de-DE - silently
        // corrupting the EXP/ACT failure message (and, for the array
        // dispatcher's per-element message, making a real numeric
        // difference look like a formatting artifact). Assertion messages
        // are diagnostic text, not user-locale-facing output, so format
        // with InvariantCulture like every other IEC type here already
        // does implicitly (integers don't vary by culture).
        private static string FormatDouble(object value) =>
            Convert.ToDouble(value).ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Keyed by the IEC type name suffix from AssertEquals_<TYPE>
        // (e.g. "INT", "BOOL", "STRING", "REAL").
        public static readonly IReadOnlyDictionary<string, ScalarAssertType> Registry =
            new Dictionary<string, ScalarAssertType>
            {
                // Upstream operates on IEC 61131-3 INT, a signed 16-bit
                // type - a real INT variable would already be truncated/
                // wrapped to that range by the time it reaches this assert.
                // Values stay boxed C# int (the rest of this codebase has no
                // narrower INT representation), but the compare wraps both
                // operands to 16 bits first so out-of-range values that
                // would collide as INT compare equal here too (TcXunit-k28.4).
                // Uses WrapShort (AsLong64-based) rather than a direct
                // (short)(int) cast: unlike every other integer-family entry
                // below, this one used to unbox straight to int and threw
                // InvalidCastException (Int64->Int32) whenever expected/
                // actual was actually a long-boxed value (e.g. a UDINT/DWORD
                // value or long-promoted arithmetic result reaching an INT
                // assert) - TcXunit-vh7.
                ["INT"] = new ScalarAssertType(
                    "INT",
                    hasDelta: false,
                    areEqual: (expected, actual, delta) => WrapShort(expected) == WrapShort(actual),
                    formatExpected: (expected, delta) => WrapShort(expected).ToString(),
                    formatActual: actual => WrapShort(actual).ToString()),

                ["BOOL"] = new ScalarAssertType(
                    "BOOL",
                    hasDelta: false,
                    areEqual: (expected, actual, delta) => (bool)expected == (bool)actual,
                    formatExpected: (expected, delta) => FormatBool((bool)expected),
                    formatActual: actual => FormatBool((bool)actual)),

                ["STRING"] = new ScalarAssertType(
                    "STRING",
                    hasDelta: false,
                    areEqual: (expected, actual, delta) => (string)expected == (string)actual,
                    formatExpected: (expected, delta) => $"'{expected}'",
                    formatActual: actual => $"'{actual}'"),

                // WSTRING (TcXunit-gd2.4): the interpreter has no narrower
                // wide-char representation than C# string (already UTF-16),
                // so this is identical to STRING's entry - only the IEC type
                // name differs.
                ["WSTRING"] = new ScalarAssertType(
                    "WSTRING",
                    hasDelta: false,
                    areEqual: (expected, actual, delta) => (string)expected == (string)actual,
                    formatExpected: (expected, delta) => $"'{expected}'",
                    formatActual: actual => $"'{actual}'"),

                ["REAL"] = new ScalarAssertType(
                    "REAL",
                    hasDelta: true,
                    areEqual: (expected, actual, delta) =>
                        Math.Abs(Convert.ToDouble(expected) - Convert.ToDouble(actual)) <= Convert.ToDouble(delta),
                    formatExpected: (expected, delta) =>
                        $"{FormatDouble(expected)} +/- {FormatDouble(delta)}",
                    formatActual: actual => FormatDouble(actual)),

                // LREAL (TcXunit-gd2.2): the 64-bit delta-based twin of REAL.
                // Both REAL and LREAL are boxed as C# double by the time they
                // reach a native call (there's no narrower float representation
                // in this codebase), so the comparison logic is identical to
                // REAL's - only the IEC type name differs.
                ["LREAL"] = new ScalarAssertType(
                    "LREAL",
                    hasDelta: true,
                    areEqual: (expected, actual, delta) =>
                        Math.Abs(Convert.ToDouble(expected) - Convert.ToDouble(actual)) <= Convert.ToDouble(delta),
                    formatExpected: (expected, delta) =>
                        $"{FormatDouble(expected)} +/- {FormatDouble(delta)}",
                    formatActual: actual => FormatDouble(actual)),

                // Integer-family types (TcXunit-gd2.1). The interpreter boxes
                // these Cell values per IecNumericType.cs - SINT/USINT/BYTE/
                // WORD/UINT/DINT as C# int, DWORD/UDINT/LINT as C# long, and
                // LWORD/ULINT as C# ulong - but an integer literal reaching a
                // native call from interpreted ST is *always* boxed C# int
                // regardless of the IEC type it's headed for (Parser.
                // Expressions.cs IntLiteralExpr), and a C# fixture calling
                // AssertEquals_<TYPE> directly boxes whatever CLR parameter
                // type that method declares. AsLong64/AsULong64 below accept
                // any of those boxed shapes and each entry's compare/format
                // then wraps to its own width/signedness (mirroring INT's
                // pre-existing 16-bit wrap), the same way a real IEC variable
                // would already be truncated to that width by the time an
                // assert sees it.
                ["SINT"] = new ScalarAssertType(
                    "SINT",
                    hasDelta: false,
                    areEqual: (expected, actual, delta) => WrapSByte(expected) == WrapSByte(actual),
                    formatExpected: (expected, delta) => WrapSByte(expected).ToString(),
                    formatActual: actual => WrapSByte(actual).ToString()),

                ["USINT"] = new ScalarAssertType(
                    "USINT",
                    hasDelta: false,
                    areEqual: (expected, actual, delta) => WrapByte(expected) == WrapByte(actual),
                    formatExpected: (expected, delta) => WrapByte(expected).ToString(),
                    formatActual: actual => WrapByte(actual).ToString()),

                ["BYTE"] = new ScalarAssertType(
                    "BYTE",
                    hasDelta: false,
                    areEqual: (expected, actual, delta) => WrapByte(expected) == WrapByte(actual),
                    formatExpected: (expected, delta) => WrapByte(expected).ToString(),
                    formatActual: actual => WrapByte(actual).ToString()),

                ["WORD"] = new ScalarAssertType(
                    "WORD",
                    hasDelta: false,
                    areEqual: (expected, actual, delta) => WrapUShort(expected) == WrapUShort(actual),
                    formatExpected: (expected, delta) => WrapUShort(expected).ToString(),
                    formatActual: actual => WrapUShort(actual).ToString()),

                ["UINT"] = new ScalarAssertType(
                    "UINT",
                    hasDelta: false,
                    areEqual: (expected, actual, delta) => WrapUShort(expected) == WrapUShort(actual),
                    formatExpected: (expected, delta) => WrapUShort(expected).ToString(),
                    formatActual: actual => WrapUShort(actual).ToString()),

                ["DINT"] = new ScalarAssertType(
                    "DINT",
                    hasDelta: false,
                    areEqual: (expected, actual, delta) => WrapInt(expected) == WrapInt(actual),
                    formatExpected: (expected, delta) => WrapInt(expected).ToString(),
                    formatActual: actual => WrapInt(actual).ToString()),

                ["DWORD"] = new ScalarAssertType(
                    "DWORD",
                    hasDelta: false,
                    areEqual: (expected, actual, delta) => WrapUInt(expected) == WrapUInt(actual),
                    formatExpected: (expected, delta) => WrapUInt(expected).ToString(),
                    formatActual: actual => WrapUInt(actual).ToString()),

                ["UDINT"] = new ScalarAssertType(
                    "UDINT",
                    hasDelta: false,
                    areEqual: (expected, actual, delta) => WrapUInt(expected) == WrapUInt(actual),
                    formatExpected: (expected, delta) => WrapUInt(expected).ToString(),
                    formatActual: actual => WrapUInt(actual).ToString()),

                ["LINT"] = new ScalarAssertType(
                    "LINT",
                    hasDelta: false,
                    areEqual: (expected, actual, delta) => AsLong64(expected) == AsLong64(actual),
                    formatExpected: (expected, delta) => AsLong64(expected).ToString(),
                    formatActual: actual => AsLong64(actual).ToString()),

                ["LWORD"] = new ScalarAssertType(
                    "LWORD",
                    hasDelta: false,
                    areEqual: (expected, actual, delta) => AsULong64(expected) == AsULong64(actual),
                    formatExpected: (expected, delta) => AsULong64(expected).ToString(),
                    formatActual: actual => AsULong64(actual).ToString()),

                ["ULINT"] = new ScalarAssertType(
                    "ULINT",
                    hasDelta: false,
                    areEqual: (expected, actual, delta) => AsULong64(expected) == AsULong64(actual),
                    formatExpected: (expected, delta) => AsULong64(expected).ToString(),
                    formatActual: actual => AsULong64(actual).ToString()),

                // TIME/LTIME (TcXunit-gd2.3): TimeLiteral.cs already gives the
                // interpreter a numeric representation for both - TIME as
                // uint milliseconds, LTIME as ulong nanoseconds - so, like
                // DWORD/UDINT and LWORD/ULINT above, they slot straight into
                // the integer-family compare/format helpers. Upstream TcUnit
                // has no delta-based AssertEquals_TIME/LTIME (durations
                // compare exactly, not within tolerance), so these use exact
                // equality the same way DWORD/LWORD do rather than REAL's
                // delta-based compare.
                ["TIME"] = new ScalarAssertType(
                    "TIME",
                    hasDelta: false,
                    areEqual: (expected, actual, delta) => WrapUInt(expected) == WrapUInt(actual),
                    formatExpected: (expected, delta) => WrapUInt(expected).ToString(),
                    formatActual: actual => WrapUInt(actual).ToString()),

                ["LTIME"] = new ScalarAssertType(
                    "LTIME",
                    hasDelta: false,
                    areEqual: (expected, actual, delta) => AsULong64(expected) == AsULong64(actual),
                    formatExpected: (expected, delta) => AsULong64(expected).ToString(),
                    formatActual: actual => AsULong64(actual).ToString()),

                // DATE/DATE_AND_TIME/TIME_OF_DAY (TcXunit-gd2.13):
                // DateTimeLiteral.cs gives the interpreter a uint
                // representation for all three (DATE/DATE_AND_TIME as
                // seconds since the 1970-01-01 epoch, TIME_OF_DAY as
                // milliseconds since midnight), so - like TIME above - they
                // slot straight into the uint compare/format helpers, with
                // exact equality rather than a delta (matching upstream:
                // no AssertEquals_DATE/_DT/_TOD tolerance argument).
                //
                // Known limitation: FormatExpected/FormatActual print the
                // raw uint value, not a calendar/clock string (e.g.
                // "2024-01-01" or "10:00:00.500") - same as TIME/LTIME's
                // existing raw-integer failure-message formatting. Adding
                // calendar-string formatting is deferred until a fixture
                // actually needs a human-readable EXP/ACT message for these
                // types (grow-on-demand).
                ["DATE"] = new ScalarAssertType(
                    "DATE",
                    hasDelta: false,
                    areEqual: (expected, actual, delta) => WrapUInt(expected) == WrapUInt(actual),
                    formatExpected: (expected, delta) => WrapUInt(expected).ToString(),
                    formatActual: actual => WrapUInt(actual).ToString()),

                ["DATE_AND_TIME"] = new ScalarAssertType(
                    "DATE_AND_TIME",
                    hasDelta: false,
                    areEqual: (expected, actual, delta) => WrapUInt(expected) == WrapUInt(actual),
                    formatExpected: (expected, delta) => WrapUInt(expected).ToString(),
                    formatActual: actual => WrapUInt(actual).ToString()),

                ["TIME_OF_DAY"] = new ScalarAssertType(
                    "TIME_OF_DAY",
                    hasDelta: false,
                    areEqual: (expected, actual, delta) => WrapUInt(expected) == WrapUInt(actual),
                    formatExpected: (expected, delta) => WrapUInt(expected).ToString(),
                    formatActual: actual => WrapUInt(actual).ToString()),
            };

        // Accepts any boxed integer shape a Cell or C# fixture parameter
        // might use (int/long/uint/ulong/etc, per IecNumericType.cs) and
        // widens to a signed 64-bit value for narrowing/comparison.
        private static long AsLong64(object value) => value switch
        {
            long l => l,
            int i => i,
            uint u => u,
            ulong ul => unchecked((long)ul),
            _ => Convert.ToInt64(value),
        };

        // Same as AsLong64 but for the unsigned 64-bit types (LWORD/ULINT),
        // whose full range doesn't fit in a signed long.
        private static ulong AsULong64(object value) => value switch
        {
            ulong ul => ul,
            long l => unchecked((ulong)l),
            uint u => u,
            int i => unchecked((ulong)(long)i),
            _ => Convert.ToUInt64(value),
        };

        private static sbyte WrapSByte(object value) => unchecked((sbyte)AsLong64(value));

        private static byte WrapByte(object value) => unchecked((byte)AsLong64(value));

        private static ushort WrapUShort(object value) => unchecked((ushort)AsLong64(value));

        private static short WrapShort(object value) => unchecked((short)AsLong64(value));

        private static int WrapInt(object value) => unchecked((int)AsLong64(value));

        private static uint WrapUInt(object value) => unchecked((uint)AsLong64(value));
    }
}
