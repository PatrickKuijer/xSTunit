using System.Collections.Generic;
using System.Linq;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // AdvanceClock(dt) is the ST-visible counterpart to engine.Clock.AdvanceMs/
    // AdvanceNs: an interpreted body advances the shared clock itself, without
    // a C# harness stepping in between StepCycles calls. Duration's CLR shape
    // picks the unit - ulong (an LTIME literal) is nanoseconds, anything else
    // (a TIME literal or a bare integer) is milliseconds.
    public class ClockAdvanceIntrinsicTests
    {
        private static (Engine Engine, FbInstance Instance, Frame Frame) NewHolder(string varBlock = "VAR\nEND_VAR")
        {
            var fb = new PouAst("FB_Holder", null, varBlock, "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            return (engine, instance, new Frame(instance, "FB_Holder"));
        }

        [Fact]
        public void AdvanceClock_WithTimeLiteral_AdvancesMilliseconds()
        {
            var (engine, _, frame) = NewHolder();

            engine.Evaluate(Parser.ParseExpression("AdvanceClock(T#100ms)"), frame);

            Assert.Equal(100, engine.Clock.TotalMs);
        }

        [Fact]
        public void AdvanceClock_WithLtimeLiteral_AdvancesNanoseconds()
        {
            var (engine, _, frame) = NewHolder();

            engine.Clock.AdvanceMs(2);
            engine.Evaluate(Parser.ParseExpression("AdvanceClock(LTIME#1us500ns)"), frame);

            Assert.Equal(2_001_500, engine.Clock.TotalNs);
            Assert.Equal(2, engine.Clock.TotalMs);
        }

        [Fact]
        public void AdvanceClock_WithBareInteger_TreatsItAsMilliseconds()
        {
            var (engine, _, frame) = NewHolder();

            engine.Evaluate(Parser.ParseExpression("AdvanceClock(42)"), frame);

            Assert.Equal(42, engine.Clock.TotalMs);
        }

        [Fact]
        public void AdvanceClock_NamedDurationArgument_Resolves()
        {
            var (engine, _, frame) = NewHolder();

            engine.Evaluate(Parser.ParseExpression("AdvanceClock(Duration := T#50ms)"), frame);

            Assert.Equal(50, engine.Clock.TotalMs);
        }

        // Drives a TON through the same ST -> native -> ST round trip as
        // TimerFbTests, but the clock is advanced from INSIDE the interpreted
        // body rather than from the C# harness between StepCycles calls.
        [Fact]
        public void AdvanceClock_CalledFromAPlainFbBody_LetsATonFireOnItsOwnNextCycle()
        {
            var pou = new PouAst(
                "FB_Wrapper",
                null,
                "VAR\n\tfbTimer : TON;\n\tmeasuredQ : BOOL;\n\tmeasuredEt : TIME;\nEND_VAR",
                "fbTimer(IN := TRUE, PT := T#500ms);\n" +
                "AdvanceClock(T#500ms);\n" +
                "fbTimer(IN := TRUE, PT := T#500ms);\n" +
                "measuredQ := fbTimer.Q;\n" +
                "measuredEt := fbTimer.ET;",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_Wrapper");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            Assert.Equal(true, instance.Fields["measuredQ"].Value);
            Assert.Equal(500u, instance.Fields["measuredEt"].Value);
        }

        // A TcUnit suite written entirely in ST - no C# harness stepping the
        // clock - must be able to both arm and observe a TON: this fails if
        // AdvanceClock is a no-op or if it moves the wrong unit.
        [Fact]
        public void RunSuite_CallsAdvanceClockThenObservesTonFire()
        {
            var testCase = new MethodAst(
                "TimerFiresAfterAdvanceClock",
                "METHOD PRIVATE TimerFiresAfterAdvanceClock",
                "TEST('TimerFiresAfterAdvanceClock');\n" +
                "fbTimer(IN := TRUE, PT := T#100ms);\n" +
                "AssertFalse(Condition := fbTimer.Q, Message := 'fired before PT elapsed');\n" +
                "AdvanceClock(T#100ms);\n" +
                "fbTimer(IN := TRUE, PT := T#100ms);\n" +
                "AssertTrue(Condition := fbTimer.Q, Message := 'did not fire once the clock moved past PT');\n" +
                "TEST_FINISHED();");

            var suite = new PouAst(
                "FB_ClockSuite",
                "TcUnit.FB_TestSuite",
                "FUNCTION_BLOCK FB_ClockSuite EXTENDS TcUnit.FB_TestSuite\nVAR\n\tfbTimer : TON;\nEND_VAR",
                "TimerFiresAfterAdvanceClock();",
                new List<MethodAst> { testCase });

            var results = new Engine(new TypeRegistry(new[] { suite })).RunSuite("FB_ClockSuite");

            var result = Assert.Single(results);
            Assert.True(result.Passed, string.Join("; ", result.Failures.Select(f => f.Message)));
        }
    }
}
