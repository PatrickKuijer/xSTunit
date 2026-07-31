using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-w5x.15.7: TON/TOF/FB_Pulse native stubs driven by Engine.Clock.
    // Each test wraps the native timer in an interpreted FB whose body does
    // the bare invocation (fbTon(IN:=.., PT:=..)) and reads Q/ET back via
    // plain field access, exercising the whole ST->native->ST round trip.
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

            Step(engine, instance); // baseline call - initializes clock tracking, no elapsed yet

            engine.Clock.Advance(499);
            Step(engine, instance);
            Assert.Equal(false, instance.Fields["measuredQ"].Value);
            Assert.Equal(499u, instance.Fields["measuredEt"].Value);

            engine.Clock.Advance(1);
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
            engine.Clock.Advance(500);
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
            Step(engine, instance); // falling edge - Q still true, ET starts from 0

            engine.Clock.Advance(299);
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);
            Assert.Equal(299u, instance.Fields["measuredEt"].Value);

            engine.Clock.Advance(1);
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

            Step(engine, instance); // rising edge - pulse starts
            Assert.Equal(true, instance.Fields["measuredQ"].Value);

            engine.Clock.Advance(199);
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);
            Assert.Equal(199u, instance.Fields["measuredEt"].Value);

            engine.Clock.Advance(1);
            Step(engine, instance); // IN still true, but pulse elapses on its own
            Assert.Equal(false, instance.Fields["measuredQ"].Value);
            Assert.Equal(200u, instance.Fields["measuredEt"].Value);

            instance.Fields["inVar"].Value = false;
            Step(engine, instance);
            instance.Fields["inVar"].Value = true;
            Step(engine, instance); // fresh rising edge re-triggers
            Assert.Equal(true, instance.Fields["measuredQ"].Value);
            Assert.Equal(0u, instance.Fields["measuredEt"].Value);
        }

        // TcXunit-tzeg.1: TP is the real IEC 61131-3 spelling of the pulse
        // timer that FB_Pulse aliases, so declaring one by either name must
        // walk the identical Q/ET trace - this mirrors the FB_Pulse test
        // above assertion for assertion.
        [Fact]
        public void Tp_FiresFixedWidthPulseOnRisingEdge_IdenticallyToFbPulse()
        {
            var engine = NewWrapperEngine("TP");
            var instance = engine.NewInstance("FB_Wrapper");
            instance.Fields["inVar"].Value = true;
            instance.Fields["ptVar"].Value = 200u;

            Step(engine, instance); // rising edge - pulse starts
            Assert.Equal(true, instance.Fields["measuredQ"].Value);

            engine.Clock.Advance(199);
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);
            Assert.Equal(199u, instance.Fields["measuredEt"].Value);

            engine.Clock.Advance(1);
            Step(engine, instance); // IN still true, but pulse elapses on its own
            Assert.Equal(false, instance.Fields["measuredQ"].Value);
            Assert.Equal(200u, instance.Fields["measuredEt"].Value);

            instance.Fields["inVar"].Value = false;
            Step(engine, instance);
            instance.Fields["inVar"].Value = true;
            Step(engine, instance); // fresh rising edge re-triggers
            Assert.Equal(true, instance.Fields["measuredQ"].Value);
            Assert.Equal(0u, instance.Fields["measuredEt"].Value);
        }

        // TcXunit-x5pt moved the Clock's base unit to ns for LTON/LTOF/LTP.
        // A TIME timer's ET is uint milliseconds and cannot represent the
        // remainder, so it truncates: sub-ms advances are still accumulated in
        // the clock (nothing is lost), they just don't surface in ET until a
        // whole millisecond has gone by.
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
                Step(engine, instance); // baseline call for each, both at clock 0
            }

            engine.Clock.Advance(700);
            Step(engine, b);
            Step(engine, a);

            Assert.Equal(700u, a.Fields["measuredEt"].Value);
            Assert.Equal(700u, b.Fields["measuredEt"].Value);
        }
    }
}
