using System;
using System.Collections.Generic;

namespace TcXunit.Runner.TcUnitStub
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
                ["INT"] = new ScalarAssertType(
                    "INT",
                    hasDelta: false,
                    areEqual: (expected, actual, delta) =>
                        unchecked((short)(int)expected) == unchecked((short)(int)actual),
                    formatExpected: (expected, delta) => unchecked((short)(int)expected).ToString(),
                    formatActual: actual => unchecked((short)(int)actual).ToString()),

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

                ["REAL"] = new ScalarAssertType(
                    "REAL",
                    hasDelta: true,
                    areEqual: (expected, actual, delta) =>
                        Math.Abs(Convert.ToDouble(expected) - Convert.ToDouble(actual)) <= Convert.ToDouble(delta),
                    formatExpected: (expected, delta) => $"{Convert.ToDouble(expected)} +/- {Convert.ToDouble(delta)}",
                    formatActual: actual => Convert.ToDouble(actual).ToString()),
            };
    }
}
