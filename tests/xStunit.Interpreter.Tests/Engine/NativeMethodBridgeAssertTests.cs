using System.Collections.Generic;
using System.Linq;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // End-to-end cover that each assert on the suite base type is reachable
    // from interpreted ST: an assert implemented on FB_TestSuite but missing a
    // native-bridge case throws "not supported" at the call site instead.
    public class NativeMethodBridgeAssertTests
    {
        private static Engine NewSuiteEngine(string implementationText)
        {
            var suite = new PouAst("FB_MySuite", "TcUnit.FB_TestSuite", "", implementationText, new List<MethodAst>());
            return new Engine(new TypeRegistry(new[] { suite }));
        }

        [Fact]
        public void RunSuite_AssertFalse_ReachableThroughInterpreter()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertFalse(Condition := (1 = 2), Message := 'ok');\n" +
                "TEST_FINISHED();");

            var results = engine.RunSuite("FB_MySuite");

            Assert.True(Assert.Single(results).Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsBool_ReachableAndReportsFailure()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_BOOL(Expected := (1 = 1), Actual := (1 = 2), Message := 'mismatch');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            Assert.Contains("EXP: TRUE, ACT: FALSE", result.Failures[0].Message);
        }

        [Fact]
        public void RunSuite_AssertEqualsString_ReachableThroughInterpreter()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_STRING(Expected := 'abc', Actual := 'abc', Message := 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        // A WSTRING literal is double-quoted, where a STRING literal is
        // single-quoted.
        [Fact]
        public void RunSuite_AssertEqualsWString_ReachableThroughInterpreter()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_WSTRING(Expected := \"abc\", Actual := \"abc\", Message := 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsWString_ReachableAndReportsFailure()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_WSTRING(Expected := \"abc\", Actual := \"xyz\", Message := 'mismatch');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            Assert.Contains("EXP: 'abc', ACT: 'xyz'", result.Failures[0].Message);
        }

        [Fact]
        public void RunSuite_AssertEqualsReal_ReachableAndRespectsDelta()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_REAL(Expected := 1.0, Actual := 1.05, Delta := 0.1, Message := 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        // Real ST calls these positionally as often as by name, so every
        // assert has to accept both argument styles.
        [Fact]
        public void RunSuite_AssertTrue_PositionalArgs_ReachableThroughInterpreter()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertTrue((1 = 1), 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_AssertFalse_PositionalArgs_ReachableThroughInterpreter()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertFalse((1 = 2), 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsInt_PositionalArgs_ReachableThroughInterpreter()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_INT(1, 1, 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsReal_PositionalArgs_ReachableThroughInterpreter()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_REAL(1.0, 1.05, 0.1, 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsBool_PositionalArgs_ReachableThroughInterpreter()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_BOOL((1 = 1), (1 = 1), 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsString_PositionalArgs_ReachableThroughInterpreter()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_STRING('abc', 'abc', 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_AssertTrue_MixedNamedAndPositionalArgs_ReachableThroughInterpreter()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertTrue((1 = 1), Message := 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsDint_NamedArgs_ReachableThroughInterpreter()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_DINT(Expected := 2147483647, Actual := 2147483647, Message := 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsDint_PositionalArgs_ReachableThroughInterpreter()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_DINT(1, 1, 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsDint_ReportsFailureWithExpAct()
        {
            // Deliberately not DINT#MIN: negation is applied to a positive
            // literal, so 2147483648 overflows on parse before the sign is
            // ever reached. That gap is separate from what this pins.
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_DINT(Expected := -2147483647, Actual := 2147483647, Message := 'mismatch');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            Assert.Contains("EXP: -2147483647, ACT: 2147483647", result.Failures[0].Message);
        }

        [Fact]
        public void RunSuite_AssertEqualsLreal_ReachableAndRespectsDelta()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_LREAL(Expected := 1.0, Actual := 1.05, Delta := 0.1, Message := 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        // A TIME literal evaluates to a boxed uint of milliseconds and an
        // LTIME literal to a boxed ulong of nanoseconds, which is what the
        // raw numbers in the failure messages below are.
        [Fact]
        public void RunSuite_AssertEqualsTime_NamedArgs_ReachableThroughInterpreter()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_TIME(Expected := T#1s500ms, Actual := TIME#1s500ms, Message := 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsTime_PositionalArgs_ReportsFailureWithExpAct()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_TIME(T#1s500ms, T#2s, 'mismatch');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            Assert.Contains("EXP: 1500, ACT: 2000", result.Failures[0].Message);
        }

        [Fact]
        public void RunSuite_AssertEqualsLtime_NamedArgs_ReachableThroughInterpreter()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_LTIME(Expected := LTIME#1s2us44ns, Actual := LT#1s2us44ns, Message := 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsLtime_PositionalArgs_ReportsFailureWithExpAct()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_LTIME(LTIME#1ms, LTIME#2ms, 'mismatch');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            Assert.Contains("EXP: 1000000, ACT: 2000000", result.Failures[0].Message);
        }

        // D#/DT# literals evaluate to boxed uint epoch-seconds and TOD# to
        // boxed uint milliseconds since midnight, which is what the raw
        // numbers in the failure messages below are.
        [Fact]
        public void RunSuite_AssertEqualsDate_NamedArgs_ReachableThroughInterpreter()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_DATE(Expected := D#2024-01-01, Actual := DATE#2024-01-01, Message := 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsDate_PositionalArgs_ReportsFailureWithExpAct()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_DATE(D#2024-01-01, D#2024-01-02, 'mismatch');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            Assert.Contains("EXP: 1704067200, ACT: 1704153600", result.Failures[0].Message);
        }

        [Fact]
        public void RunSuite_AssertEqualsDateAndTime_NamedArgs_ReachableThroughInterpreter()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_DATE_AND_TIME(Expected := DT#2024-01-01-10:00:00, Actual := DATE_AND_TIME#2024-01-01-10:00:00, Message := 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsDateAndTime_PositionalArgs_ReportsFailureWithExpAct()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_DATE_AND_TIME(DT#2024-01-01-10:00:00, DT#2024-01-01-11:00:00, 'mismatch');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            Assert.Contains("EXP: 1704103200, ACT: 1704106800", result.Failures[0].Message);
        }

        [Fact]
        public void RunSuite_AssertEqualsTimeOfDay_NamedArgs_ReachableThroughInterpreter()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_TIME_OF_DAY(Expected := TOD#10:00:00, Actual := TIME_OF_DAY#10:00:00, Message := 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsTimeOfDay_PositionalArgs_ReportsFailureWithExpAct()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_TIME_OF_DAY(TOD#10:00:00, TOD#10:00:00.500, 'mismatch');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            Assert.Contains("EXP: 36000000, ACT: 36000500", result.Failures[0].Message);
        }
    }
}
