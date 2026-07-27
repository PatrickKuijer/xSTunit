using System.Collections.Generic;
using System.Linq;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-k28.6: AssertFalse/AssertEquals_BOOL/AssertEquals_STRING/
    // AssertEquals_REAL were already implemented on FB_TestSuite but had no
    // NativeMethodBridge case or TcUnitSuiteHost wrapper, so an interpreted
    // suite calling them threw "not supported" - wires them up.
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
            // No TRUE/FALSE literal keywords in the interpreter's expression
            // grammar yet (separate, pre-existing gap) - a comparison
            // expression yields the same BOOL value.
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

        // TcXunit-gd2.4: WSTRING assert, wired the same way STRING is -
        // WSTRING literals use "..." rather than STRING's '...'.
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

        // TcXunit-bda: real TcUnit/ST code frequently calls these
        // positionally (e.g. AssertTrue(cond, 'msg')) - previously threw
        // KeyNotFoundException because the switch only ever read named[...].
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

        // TcXunit-gd2.1: AssertEquals_DINT is the highest-priority
        // integer-family type added by the ScalarAssertType registry - this
        // is the E2E proof it's reachable through NativeMethodBridge from
        // interpreted ST, both passing and reporting a clear failure.
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
            // Not int.MinValue here: the interpreter's literal parser negates
            // a positive literal (Parser.Expressions.cs), and 2147483648
            // itself overflows Int32.Parse before negation ever applies -
            // a pre-existing, separate parser gap unrelated to this ticket.
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_DINT(Expected := -2147483647, Actual := 2147483647, Message := 'mismatch');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            Assert.Contains("EXP: -2147483647, ACT: 2147483647", result.Failures[0].Message);
        }

        // TcXunit-gd2.2: AssertEquals_LREAL is the 64-bit delta-based twin
        // of AssertEquals_REAL - this is the E2E proof it's reachable
        // through NativeMethodBridge from interpreted ST.
        [Fact]
        public void RunSuite_AssertEqualsLreal_ReachableAndRespectsDelta()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_LREAL(Expected := 1.0, Actual := 1.05, Delta := 0.1, Message := 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        // TcXunit-gd2.3: AssertEquals_TIME/LTIME - E2E proof they're
        // reachable through NativeMethodBridge from interpreted ST, driven
        // by real TIME#/LTIME# literals (see TimeTypeTests.cs for how the
        // interpreter evaluates these to boxed uint ms / ulong ns).
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

        // TcXunit-gd2.13: AssertEquals_DATE/_DATE_AND_TIME/_TIME_OF_DAY -
        // E2E proof they're reachable through NativeMethodBridge from
        // interpreted ST, driven by real D#/DT#/TOD# literals (see
        // DateTimeTypeTests.cs for how the interpreter evaluates these to
        // boxed uint epoch-seconds/midnight-ms).
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
