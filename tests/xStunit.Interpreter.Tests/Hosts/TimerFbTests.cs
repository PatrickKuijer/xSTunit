using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // Drives the TON/TOF/TP native stubs through the full ST -> native -> ST
    // round trip: an interpreted wrapper FB does the bare invocation and reads
    // Q/ET back by plain field access.
    //
    // Unlike the edge triggers and counters, a timer is CLOCK-driven, not
    // call-driven: elapsed time is the difference between clock totals across
    // two calls. So the first Step of each test only establishes the baseline,
    // stepping again without advancing the clock is a no-op rather than a
    // double count, and a clock advance is observed in full on the next Step
    // however many cycles later.
    public class TimerFbTests
    {
        private static Engine NewWrapperEngine(string timerTypeName)
        {
            var pou = new PouAst(
                "FB_Wrapper",
                null,
                "VAR\n\tfbTimer : " + timerTypeName + ";\n\tinVar : BOOL;\n\tptVar : TIME;\n\tmeasuredQ : BOOL;\n\tmeasuredEt : TIME;\nEND_VAR",
                "fbTimer(IN:=inVar, PT:=ptVar);\nmeasuredQ := fbTimer.Q;\nmeasuredEt := fbTimer.ET;",
                new List<MethodAst>());

            return new Engine(new TypeRegistry(new[] { pou }));
        }

        private static void Step(Engine engine, FbInstance instance) =>
            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

        [Fact]
        public void Ton_DoesNotFireBeforePt_ThenFiresAtPt()
        {
            var engine = NewWrapperEngine("TON");
            var instance = engine.NewInstance("FB_Wrapper");
            instance.Fields["inVar"].Value = true;
            instance.Fields["ptVar"].Value = 500u;

            Step(engine, instance);

            engine.Clock.AdvanceMs(499);
            Step(engine, instance);
            Assert.Equal(false, instance.Fields["measuredQ"].Value);
            Assert.Equal(499u, instance.Fields["measuredEt"].Value);

            engine.Clock.AdvanceMs(1);
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);
            Assert.Equal(500u, instance.Fields["measuredEt"].Value);
        }

        [Fact]
        public void Ton_FallingEdge_ResetsImmediately()
        {
            var engine = NewWrapperEngine("TON");
            var instance = engine.NewInstance("FB_Wrapper");
            instance.Fields["inVar"].Value = true;
            instance.Fields["ptVar"].Value = 500u;

            Step(engine, instance);
            engine.Clock.AdvanceMs(500);
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);

            instance.Fields["inVar"].Value = false;
            Step(engine, instance);

            Assert.Equal(false, instance.Fields["measuredQ"].Value);
            Assert.Equal(0u, instance.Fields["measuredEt"].Value);
        }

        [Fact]
        public void Tof_HoldsQAfterFallingEdgeUntilPt_ThenDrops()
        {
            var engine = NewWrapperEngine("TOF");
            var instance = engine.NewInstance("FB_Wrapper");
            instance.Fields["inVar"].Value = true;
            instance.Fields["ptVar"].Value = 300u;

            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);

            instance.Fields["inVar"].Value = false;
            Step(engine, instance); // TOF holds Q true past the falling edge, and ET starts there

            engine.Clock.AdvanceMs(299);
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);
            Assert.Equal(299u, instance.Fields["measuredEt"].Value);

            engine.Clock.AdvanceMs(1);
            Step(engine, instance);
            Assert.Equal(false, instance.Fields["measuredQ"].Value);
            Assert.Equal(300u, instance.Fields["measuredEt"].Value);
        }

        [Fact]
        public void Pulse_FiresFixedWidthPulseOnRisingEdge_IgnoringInAfterwards()
        {
            var engine = NewWrapperEngine("FB_Pulse");
            var instance = engine.NewInstance("FB_Wrapper");
            instance.Fields["inVar"].Value = true;
            instance.Fields["ptVar"].Value = 200u;

            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);

            engine.Clock.AdvanceMs(199);
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);
            Assert.Equal(199u, instance.Fields["measuredEt"].Value);

            engine.Clock.AdvanceMs(1);
            Step(engine, instance); // pulse width is fixed, so Q drops while IN is still true
            Assert.Equal(false, instance.Fields["measuredQ"].Value);
            Assert.Equal(200u, instance.Fields["measuredEt"].Value);

            instance.Fields["inVar"].Value = false;
            Step(engine, instance);
            instance.Fields["inVar"].Value = true;
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);
            Assert.Equal(0u, instance.Fields["measuredEt"].Value);
        }

        // Deliberately duplicates the FB_Pulse test above assertion for
        // assertion. TP is the real IEC 61131-3 spelling and FB_Pulse only this
        // project's alias for it, so the two names must walk an identical Q/ET
        // trace - collapsing this into one test would stop proving that.
        [Fact]
        public void Tp_FiresFixedWidthPulseOnRisingEdge_IdenticallyToFbPulse()
        {
            var engine = NewWrapperEngine("TP");
            var instance = engine.NewInstance("FB_Wrapper");
            instance.Fields["inVar"].Value = true;
            instance.Fields["ptVar"].Value = 200u;

            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);

            engine.Clock.AdvanceMs(199);
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);
            Assert.Equal(199u, instance.Fields["measuredEt"].Value);

            engine.Clock.AdvanceMs(1);
            Step(engine, instance); // pulse width is fixed, so Q drops while IN is still true
            Assert.Equal(false, instance.Fields["measuredQ"].Value);
            Assert.Equal(200u, instance.Fields["measuredEt"].Value);

            instance.Fields["inVar"].Value = false;
            Step(engine, instance);
            instance.Fields["inVar"].Value = true;
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);
            Assert.Equal(0u, instance.Fields["measuredEt"].Value);
        }

        // The Clock counts in ns so the LTIME timers can use it, but a TIME
        // timer's ET is uint milliseconds and cannot hold the remainder. It
        // truncates rather than losing it: sub-ms advances still accumulate in
        // the clock, they just don't surface in ET until a whole millisecond
        // has gone by, so two sub-ms steps summing to 1 ms do fire a 1 ms PT.
        [Fact]
        public void Ton_SubMillisecondAdvances_AccumulateButOnlySurfaceAsWholeMs()
        {
            var engine = NewWrapperEngine("TON");
            var instance = engine.NewInstance("FB_Wrapper");
            instance.Fields["inVar"].Value = true;
            instance.Fields["ptVar"].Value = 1u;

            Step(engine, instance);

            engine.Clock.AdvanceNs(600_000);
            Step(engine, instance);
            Assert.Equal(false, instance.Fields["measuredQ"].Value);
            Assert.Equal(0u, instance.Fields["measuredEt"].Value);

            engine.Clock.AdvanceNs(400_000);
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);
            Assert.Equal(1u, instance.Fields["measuredEt"].Value);
        }

        [Fact]
        public void Clock_MultiInstanceSteppingOrder_DoesNotStealElapsedTime()
        {
            var engine = NewWrapperEngine("TON");
            var a = engine.NewInstance("FB_Wrapper");
            var b = engine.NewInstance("FB_Wrapper");
            foreach (var instance in new[] { a, b })
            {
                instance.Fields["inVar"].Value = true;
                instance.Fields["ptVar"].Value = 1000u;
                Step(engine, instance);
            }

            engine.Clock.AdvanceMs(700);
            Step(engine, b);
            Step(engine, a);

            Assert.Equal(700u, a.Fields["measuredEt"].Value);
            Assert.Equal(700u, b.Fields["measuredEt"].Value);
        }
    }
}
