using System;
using System.Collections.Generic;

namespace xStunit.Runner.TcUnitStub
{
    // Table-driven registry backing FB_TestSuite.AssertEqualsScalar's per-type
    // dispatch. Each entry carries everything one hand-written
    // AssertEquals_<TYPE> method used to: how to compare (with any
    // type-specific wrapping, e.g. INT's 16-bit wraparound), whether the type
    // takes a Delta argument, and how to format expected/actual for the Fail()
    // message. A hand-written method has to be repeated across FB_TestSuite,
    // SuiteHost and NativeMethodBridge, so a new scalar type costs three
    // methods that way and one entry this way.
    public sealed class ScalarAssertType
    {
        public string Name { get; }

        // True when the type compares within a tolerance (Delta arg required),
        // false when it compares exactly.
        public bool HasDelta { get; }

        // (expected, actual, delta) -> true when the assertion should PASS.
        // Applies any type-specific wrap/rounding rule; consults delta only
        // when HasDelta.
        public Func<object, object, object, bool> AreEqual { get; }

        // (expected, delta) -> the EXP text of the Fail() message; the delta is
        // folded into it only when HasDelta.
        public Func<object, object, string> FormatExpected { get; }

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

        // InvariantCulture, not ToString()'s CurrentCulture: on e.g. de-DE the
        // latter renders "." as ",", silently corrupting the EXP/ACT text and
        // making a real numeric difference look like a formatting artifact.
        // Assertion messages are diagnostic text, not locale-facing output, and
        // every other IEC type here is culture-invariant already.
        private static string FormatDouble(object value) =>
            Convert.ToDouble(value).ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Keyed by the IEC type name suffix from AssertEquals_<TYPE>
        // (e.g. "INT", "BOOL", "STRING", "REAL"), in any case, as IEC type
        // names are; Name is the canonical spelling of the entry matched.
        public static readonly IReadOnlyDictionary<string, ScalarAssertType> Registry =
            new Dictionary<string, ScalarAssertType>(StringComparer.OrdinalIgnoreCase)
            {
                // IEC INT is signed 16-bit, so a real INT variable is already
                // truncated to that range by the time an assert sees it.
                // Values stay boxed C# int here (this codebase has no narrower
                // representation), so the compare wraps both operands to 16
                // bits itself - otherwise two out-of-range values that would
                // collide as INT would compare unequal.
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

                // Identical to STRING but for the type name: C# string is
                // already UTF-16, so there is nothing narrower to model.
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

                // Identical to REAL but for the type name: both arrive boxed as
                // C# double, there being no narrower float representation here.
                ["LREAL"] = new ScalarAssertType(
                    "LREAL",
                    hasDelta: true,
                    areEqual: (expected, actual, delta) =>
                        Math.Abs(Convert.ToDouble(expected) - Convert.ToDouble(actual)) <= Convert.ToDouble(delta),
                    formatExpected: (expected, delta) =>
                        $"{FormatDouble(expected)} +/- {FormatDouble(delta)}",
                    formatActual: actual => FormatDouble(actual)),

                // Integer-family types. No entry may unbox to a specific CLR
                // integer type: the same IEC type reaches an assert boxed
                // differently depending on the caller - an ST integer literal
                // is always boxed C# int whatever IEC type it is headed for,
                // an interpreted Cell uses IecNumericType.cs' own choice of
                // int/long/ulong, and a C# fixture boxes whatever CLR type
                // AssertEquals_<TYPE> declares. AsLong64/AsULong64 accept all
                // of those shapes; each entry then wraps to its own
                // width/signedness, the way a real IEC variable would already
                // be truncated by the time an assert sees it.
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

                // TIME is uint milliseconds and LTIME ulong nanoseconds per
                // TimeLiteral.cs, so both slot into the integer-family
                // helpers. Exact equality, no delta: upstream has no
                // tolerance-based AssertEquals_TIME/_LTIME either, durations
                // being compared exactly.
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

                // All three are uint per DateTimeLiteral.cs - DATE and
                // DATE_AND_TIME as seconds since the 1970-01-01 epoch,
                // TIME_OF_DAY as milliseconds since midnight - so they too
                // compare exactly through the uint helpers.
                //
                // Known limitation: a failure message prints that raw uint,
                // not "2024-01-01" or "10:00:00.500". Calendar formatting is
                // deferred until a fixture actually needs a readable EXP/ACT
                // for these types.
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

        // Accepts any boxed integer shape a Cell or C# fixture parameter might
        // use and widens it to signed 64-bit for narrowing/comparison.
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
