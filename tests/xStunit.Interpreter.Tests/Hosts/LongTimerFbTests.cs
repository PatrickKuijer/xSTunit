using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // LTON/LTOF/LTP are the 64-bit siblings of TON/TOF/TP: same IN/PT->Q/ET
    // contract TimerFbTests exercises, but PT/ET are LTIME (ulong NANOSECONDS)
    // rather than TIME (uint milliseconds). So these tests are deliberately
    // written at the two scales the ms timers cannot express at all:
    //   - sub-millisecond PT (1500 ns), which a ms field truncates to 0;
    //   - PT/ET past uint.MaxValue ns (~4.29 s), which a 32-bit field wraps.
    // Anything provable at ms width belongs in TimerFbTests instead.
    //
    // Elapsed time comes from the shared Clock, not from being called: the
    // first Step of each test only establishes the baseline, stepping again
    // without advancing the clock is a no-op, and a clock advance is observed
    // in full on the next Step however many cycles later.
    public class LongTimerFbTests
    {
        // Elapsed nanoseconds for one hour: 3.6e12, ~838x uint.MaxValue.
        private const ulong OneHourNs = 3_600_000_000_000UL;

        private static Engine NewWrapperEngine(string timerTypeName)
        {
            var pou = new PouAst(
                "FB_Wrapper",
                null,
                "VAR\n\tfbTimer : " + timerTypeName + ";\n\tinVar : BOOL;\n\tptVar : LTIME;\n\tmeasuredQ : BOOL;\n\tmeasuredEt : LTIME;\nEND_VAR",
                "fbTimer(IN:=inVar, PT:=ptVar);\nmeasuredQ := fbTimer.Q;\nmeasuredEt := fbTimer.ET;",
                new List<MethodAst>());

            return new Engine(new TypeRegistry(new[] { pou }));
        }

        private static void Step(Engine engine, FbInstance instance) =>
            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

        [Fact]
        public void Lton_SeedsPtAndEtAsLtimeZero_NotTimeZero()
        {
            var engine = NewWrapperEngine("LTON");
            var instance = engine.NewInstance("FB_Wrapper");
            var fbTimer = (FbInstance)instance.Fields["fbTimer"].Value;

            Assert.NotNull(fbTimer.NativeTimerHost);
            Assert.Null(fbTimer.NativeSuiteHost);
            Assert.Equal(0UL, fbTimer.Fields["PT"].Value);
            Assert.Equal(0UL, fbTimer.Fields["ET"].Value);
        }

        [Fact]
        public void Lton_CountsAtNanosecondPrecision_BelowOneMillisecond()
        {
            var engine = NewWrapperEngine("LTON");
            var instance = engine.NewInstance("FB_Wrapper");
            instance.Fields["inVar"].Value = true;
            instance.Fields["ptVar"].Value = 1500UL;

            Step(engine, instance);

            engine.Clock.AdvanceNs(1499);
            Step(engine, instance);
            Assert.Equal(false, instance.Fields["measuredQ"].Value);
            Assert.Equal(1499UL, instance.Fields["measuredEt"].Value);

            engine.Clock.AdvanceNs(1);
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);
            Assert.Equal(1500UL, instance.Fields["measuredEt"].Value);
        }

        [Fact]
        public void Lton_PtBeyond32BitNanoseconds_DoesNotFireEarly_AndEtDoesNotWrap()
        {
            var engine = NewWrapperEngine("LTON");
            var instance = engine.NewInstance("FB_Wrapper");
            instance.Fields["inVar"].Value = true;
            instance.Fields["ptVar"].Value = OneHourNs;

            Step(engine, instance);

            // 5 s: past uint.MaxValue ns, where a 32-bit ET would have wrapped.
            engine.Clock.AdvanceMs(5_000);
            Step(engine, instance);
            Assert.Equal(false, instance.Fields["measuredQ"].Value);
            Assert.Equal(5_000_000_000UL, instance.Fields["measuredEt"].Value);

            engine.Clock.AdvanceMs(3_595_000); // brings the total to one hour
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);
            Assert.Equal(OneHourNs, instance.Fields["measuredEt"].Value);
        }

        [Fact]
        public void Lton_FallingEdge_ResetsImmediately()
        {
            var engine = NewWrapperEngine("LTON");
            var instance = engine.NewInstance("FB_Wrapper");
            instance.Fields["inVar"].Value = true;
            instance.Fields["ptVar"].Value = 500UL;

            Step(engine, instance);
            engine.Clock.AdvanceNs(500);
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);

            instance.Fields["inVar"].Value = false;
            Step(engine, instance);

            Assert.Equal(false, instance.Fields["measuredQ"].Value);
            Assert.Equal(0UL, instance.Fields["measuredEt"].Value);
        }

        [Fact]
        public void Ltof_HoldsQAfterFallingEdgeUntilPt_ThenDrops()
        {
            var engine = NewWrapperEngine("LTOF");
            var instance = engine.NewInstance("FB_Wrapper");
            instance.Fields["inVar"].Value = true;
            instance.Fields["ptVar"].Value = 300UL;

            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);

            instance.Fields["inVar"].Value = false;
            Step(engine, instance);

            engine.Clock.AdvanceNs(299);
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);
            Assert.Equal(299UL, instance.Fields["measuredEt"].Value);

            engine.Clock.AdvanceNs(1);
            Step(engine, instance);
            Assert.Equal(false, instance.Fields["measuredQ"].Value);
            Assert.Equal(300UL, instance.Fields["measuredEt"].Value);
        }

        [Fact]
        public void Ltof_PtBeyond32BitNanoseconds_HoldsQForTheWholeDelay()
        {
            var engine = NewWrapperEngine("LTOF");
            var instance = engine.NewInstance("FB_Wrapper");
            instance.Fields["inVar"].Value = true;
            instance.Fields["ptVar"].Value = 10_000_000_000UL; // 10 s in ns

            Step(engine, instance);
            instance.Fields["inVar"].Value = false;
            Step(engine, instance);

            engine.Clock.AdvanceMs(9_999);
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);
            Assert.Equal(9_999_000_000UL, instance.Fields["measuredEt"].Value);

            engine.Clock.AdvanceMs(1);
            Step(engine, instance);
            Assert.Equal(false, instance.Fields["measuredQ"].Value);
            Assert.Equal(10_000_000_000UL, instance.Fields["measuredEt"].Value);
        }

        [Fact]
        public void Ltp_FiresFixedWidthPulseOnRisingEdge_IgnoringInAfterwards()
        {
            var engine = NewWrapperEngine("LTP");
            var instance = engine.NewInstance("FB_Wrapper");
            instance.Fields["inVar"].Value = true;
            instance.Fields["ptVar"].Value = 6_000_000_000UL; // 6 s in ns, past uint.MaxValue

            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);

            engine.Clock.AdvanceMs(5_999);
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);
            Assert.Equal(5_999_000_000UL, instance.Fields["measuredEt"].Value);

            engine.Clock.AdvanceMs(1);
            Step(engine, instance);
            Assert.Equal(false, instance.Fields["measuredQ"].Value);
            Assert.Equal(6_000_000_000UL, instance.Fields["measuredEt"].Value);

            instance.Fields["inVar"].Value = false;
            Step(engine, instance);
            instance.Fields["inVar"].Value = true;
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);
            Assert.Equal(0UL, instance.Fields["measuredEt"].Value);
        }

        // Both timer families share one Clock, so an LTIME timer has to see an
        // ms advance at its full ns weight (TimerFbTests pins the other half:
        // a TIME timer is unaffected by the finer base unit).
        [Fact]
        public void Lton_MsClockAdvance_CountsAsWholeMillionsOfNanoseconds()
        {
            var engine = NewWrapperEngine("LTON");
            var instance = engine.NewInstance("FB_Wrapper");
            instance.Fields["inVar"].Value = true;
            instance.Fields["ptVar"].Value = 2_000_000UL; // 2 ms expressed in ns

            Step(engine, instance);

            engine.Clock.AdvanceMs(1);
            Step(engine, instance);
            Assert.Equal(false, instance.Fields["measuredQ"].Value);
            Assert.Equal(1_000_000UL, instance.Fields["measuredEt"].Value);

            engine.Clock.AdvanceMs(1);
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);
            Assert.Equal(2_000_000UL, instance.Fields["measuredEt"].Value);
        }

        [Fact]
        public void LongTimers_MultiInstanceSteppingOrder_DoesNotStealElapsedTime()
        {
            var engine = NewWrapperEngine("LTON");
            var a = engine.NewInstance("FB_Wrapper");
            var b = engine.NewInstance("FB_Wrapper");
            foreach (var instance in new[] { a, b })
            {
                instance.Fields["inVar"].Value = true;
                instance.Fields["ptVar"].Value = 10_000_000_000UL;
                Step(engine, instance);
            }

            engine.Clock.AdvanceNs(7_000_000_700);
            Step(engine, b);
            Step(engine, a);

            Assert.Equal(7_000_000_700UL, a.Fields["measuredEt"].Value);
            Assert.Equal(7_000_000_700UL, b.Fields["measuredEt"].Value);
        }
    }
}
