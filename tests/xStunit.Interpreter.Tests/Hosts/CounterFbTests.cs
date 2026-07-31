using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcXunit-l64b: CTU/CTD/CTUD native stubs, exercised the same way as
    // BistableLatchFbTests - a wrapper FB does the bare invocation
    // (fbCounter(CU:=.., RESET:=.., PV:=..)) and reads Q/QU/QD/CV back via
    // plain field access. Each counter gets its own wrapper because their
    // input and output names differ (CTU: CU/RESET->Q, CTD: CD/LOAD->Q,
    // CTUD: CU/CD/RESET/LOAD->QU/QD).
    //
    // Semantics under test are the documented Tc2_Standard ones: counting on
    // the RISING EDGE of CU/CD only, RESET/LOAD taking precedence over
    // counting, and CV SATURATING at the WORD limits rather than wrapping.
    public class CounterFbTests
    {
        private static Engine NewCtuEngine(string counterTypeName = "CTU")
        {
            var pou = new PouAst(
                "FB_Wrapper",
                null,
                "VAR\n\tfbCounter : " + counterTypeName + ";\n\tcuVar : BOOL;\n\tresetVar : BOOL;\n\tpvVar : WORD;\n\tmeasuredQ : BOOL;\n\tmeasuredCV : WORD;\nEND_VAR",
                "fbCounter(CU:=cuVar, RESET:=resetVar, PV:=pvVar);\nmeasuredQ := fbCounter.Q;\nmeasuredCV := fbCounter.CV;",
                new List<MethodAst>());

            return new Engine(new TypeRegistry(new[] { pou }));
        }

        private static Engine NewCtdEngine(string counterTypeName = "CTD")
        {
            var pou = new PouAst(
                "FB_Wrapper",
                null,
                "VAR\n\tfbCounter : " + counterTypeName + ";\n\tcdVar : BOOL;\n\tloadVar : BOOL;\n\tpvVar : WORD;\n\tmeasuredQ : BOOL;\n\tmeasuredCV : WORD;\nEND_VAR",
                "fbCounter(CD:=cdVar, LOAD:=loadVar, PV:=pvVar);\nmeasuredQ := fbCounter.Q;\nmeasuredCV := fbCounter.CV;",
                new List<MethodAst>());

            return new Engine(new TypeRegistry(new[] { pou }));
        }

        private static Engine NewCtudEngine(string counterTypeName = "CTUD")
        {
            var pou = new PouAst(
                "FB_Wrapper",
                null,
                "VAR\n\tfbCounter : " + counterTypeName + ";\n\tcuVar : BOOL;\n\tcdVar : BOOL;\n\tresetVar : BOOL;\n\tloadVar : BOOL;\n\tpvVar : WORD;\n\tmeasuredQU : BOOL;\n\tmeasuredQD : BOOL;\n\tmeasuredCV : WORD;\nEND_VAR",
                "fbCounter(CU:=cuVar, CD:=cdVar, RESET:=resetVar, LOAD:=loadVar, PV:=pvVar);\nmeasuredQU := fbCounter.QU;\nmeasuredQD := fbCounter.QD;\nmeasuredCV := fbCounter.CV;",
                new List<MethodAst>());

            return new Engine(new TypeRegistry(new[] { pou }));
        }

        private static void RunCycle(Engine engine, FbInstance instance) =>
            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

        private static void StepUp(Engine engine, FbInstance instance, bool cu, bool reset, int pv)
        {
            instance.Fields["cuVar"].Value = cu;
            instance.Fields["resetVar"].Value = reset;
            instance.Fields["pvVar"].Value = pv;
            RunCycle(engine, instance);
        }

        private static void StepDown(Engine engine, FbInstance instance, bool cd, bool load, int pv)
        {
            instance.Fields["cdVar"].Value = cd;
            instance.Fields["loadVar"].Value = load;
            instance.Fields["pvVar"].Value = pv;
            RunCycle(engine, instance);
        }

        private static void StepUpDown(Engine engine, FbInstance instance, bool cu, bool cd, bool reset, bool load, int pv)
        {
            instance.Fields["cuVar"].Value = cu;
            instance.Fields["cdVar"].Value = cd;
            instance.Fields["resetVar"].Value = reset;
            instance.Fields["loadVar"].Value = load;
            instance.Fields["pvVar"].Value = pv;
            RunCycle(engine, instance);
        }

        private static object Cv(FbInstance instance) => instance.Fields["measuredCV"].Value;

        private static object Q(FbInstance instance) => instance.Fields["measuredQ"].Value;

        private static object Qu(FbInstance instance) => instance.Fields["measuredQU"].Value;

        private static object Qd(FbInstance instance) => instance.Fields["measuredQD"].Value;

        // Seeds the counter's own CV Cell, so the WORD-limit tests don't need
        // 65535 interpreted cycles to walk there.
        private static void SeedCounterValue(FbInstance instance, int value) =>
            ((FbInstance)instance.Fields["fbCounter"].Value).Fields["CV"].Value = value;

        // --- CTU: RESET -> CV := 0, rising CU -> CV := CV + 1, Q := CV >= PV ---

        [Fact]
        public void Ctu_StartsAtZero()
        {
            var engine = NewCtuEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            StepUp(engine, instance, cu: false, reset: false, pv: 3);
            Assert.Equal(0, Cv(instance));
            Assert.Equal(false, Q(instance));
        }

        [Fact]
        public void Ctu_CountsOnRisingEdgeOnly()
        {
            var engine = NewCtuEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            // First cycle with CU already TRUE is a FALSE->TRUE edge: the
            // counter's previous-CU memory starts FALSE, as the IEC body's
            // ordinary BOOL does.
            StepUp(engine, instance, cu: true, reset: false, pv: 3);
            Assert.Equal(1, Cv(instance));

            // Held high: no further edge, no further counting.
            StepUp(engine, instance, cu: true, reset: false, pv: 3);
            Assert.Equal(1, Cv(instance));
            StepUp(engine, instance, cu: true, reset: false, pv: 3);
            Assert.Equal(1, Cv(instance));

            // Falling edge does not count either...
            StepUp(engine, instance, cu: false, reset: false, pv: 3);
            Assert.Equal(1, Cv(instance));

            // ...but the next rising edge does.
            StepUp(engine, instance, cu: true, reset: false, pv: 3);
            Assert.Equal(2, Cv(instance));
        }

        [Fact]
        public void Ctu_QGoesTrueWhenCvReachesPvAndStaysTrueAbove()
        {
            var engine = NewCtuEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            for (var i = 0; i < 2; i++)
            {
                StepUp(engine, instance, cu: true, reset: false, pv: 2);
                Assert.Equal(i + 1, Cv(instance));
                StepUp(engine, instance, cu: false, reset: false, pv: 2);
            }

            Assert.Equal(2, Cv(instance));
            Assert.Equal(true, Q(instance));

            StepUp(engine, instance, cu: true, reset: false, pv: 2);
            Assert.Equal(3, Cv(instance));
            Assert.Equal(true, Q(instance));
        }

        [Fact]
        public void Ctu_ResetTakesPrecedenceOverCounting()
        {
            var engine = NewCtuEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            StepUp(engine, instance, cu: true, reset: false, pv: 1);
            Assert.Equal(1, Cv(instance));
            Assert.Equal(true, Q(instance));

            // Rising CU edge AND RESET on the same cycle: RESET wins.
            StepUp(engine, instance, cu: false, reset: false, pv: 1);
            StepUp(engine, instance, cu: true, reset: true, pv: 1);
            Assert.Equal(0, Cv(instance));
            Assert.Equal(false, Q(instance));

            // RESET held: CV stays at 0 even with more CU edges.
            StepUp(engine, instance, cu: false, reset: true, pv: 1);
            StepUp(engine, instance, cu: true, reset: true, pv: 1);
            Assert.Equal(0, Cv(instance));
        }

        [Fact]
        public void Ctu_CountingResumesAfterResetDrops()
        {
            var engine = NewCtuEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            StepUp(engine, instance, cu: true, reset: true, pv: 5);
            Assert.Equal(0, Cv(instance));

            // CU stayed high across the RESET cycle, so dropping RESET must
            // NOT produce a phantom edge - the level was already sampled.
            StepUp(engine, instance, cu: true, reset: false, pv: 5);
            Assert.Equal(0, Cv(instance));

            StepUp(engine, instance, cu: false, reset: false, pv: 5);
            StepUp(engine, instance, cu: true, reset: false, pv: 5);
            Assert.Equal(1, Cv(instance));
        }

        // The gotcha: CV saturates at the WORD ceiling, it does not wrap to 0.
        [Fact]
        public void Ctu_SaturatesAtWordCeilingInsteadOfWrapping()
        {
            var engine = NewCtuEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            SeedCounterValue(instance, 65534);

            StepUp(engine, instance, cu: true, reset: false, pv: 65535);
            Assert.Equal(65535, Cv(instance));
            Assert.Equal(true, Q(instance));

            StepUp(engine, instance, cu: false, reset: false, pv: 65535);
            StepUp(engine, instance, cu: true, reset: false, pv: 65535);
            Assert.Equal(65535, Cv(instance));

            StepUp(engine, instance, cu: false, reset: false, pv: 65535);
            StepUp(engine, instance, cu: true, reset: false, pv: 65535);
            Assert.Equal(65535, Cv(instance));
            Assert.Equal(true, Q(instance));
        }

        // --- CTD: LOAD -> CV := PV, rising CD -> CV := CV - 1, Q := CV = 0 ---

        [Fact]
        public void Ctd_StartsAtZeroWithQAlreadySet()
        {
            var engine = NewCtdEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            StepDown(engine, instance, cd: false, load: false, pv: 3);
            Assert.Equal(0, Cv(instance));
            Assert.Equal(true, Q(instance));
        }

        [Fact]
        public void Ctd_LoadInitializesCvWithPv()
        {
            var engine = NewCtdEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            StepDown(engine, instance, cd: false, load: true, pv: 3);
            Assert.Equal(3, Cv(instance));
            Assert.Equal(false, Q(instance));
        }

        [Fact]
        public void Ctd_CountsDownOnRisingEdgeOnly()
        {
            var engine = NewCtdEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            StepDown(engine, instance, cd: false, load: true, pv: 3);
            Assert.Equal(3, Cv(instance));

            StepDown(engine, instance, cd: true, load: false, pv: 3);
            Assert.Equal(2, Cv(instance));

            // Held high: no second decrement.
            StepDown(engine, instance, cd: true, load: false, pv: 3);
            Assert.Equal(2, Cv(instance));

            StepDown(engine, instance, cd: false, load: false, pv: 3);
            Assert.Equal(2, Cv(instance));

            StepDown(engine, instance, cd: true, load: false, pv: 3);
            Assert.Equal(1, Cv(instance));
            Assert.Equal(false, Q(instance));
        }

        [Fact]
        public void Ctd_LoadTakesPrecedenceOverCounting()
        {
            var engine = NewCtdEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            StepDown(engine, instance, cd: false, load: true, pv: 4);
            StepDown(engine, instance, cd: true, load: false, pv: 4);
            Assert.Equal(3, Cv(instance));

            // Rising CD edge AND LOAD on the same cycle: LOAD wins, so CV is
            // the preset rather than the preset minus one.
            StepDown(engine, instance, cd: false, load: false, pv: 4);
            StepDown(engine, instance, cd: true, load: true, pv: 4);
            Assert.Equal(4, Cv(instance));
        }

        // The gotcha, downward: CV stops at 0, it does not wrap to 65535.
        [Fact]
        public void Ctd_SaturatesAtZeroInsteadOfWrapping()
        {
            var engine = NewCtdEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            StepDown(engine, instance, cd: false, load: true, pv: 1);
            Assert.Equal(1, Cv(instance));

            StepDown(engine, instance, cd: true, load: false, pv: 1);
            Assert.Equal(0, Cv(instance));
            Assert.Equal(true, Q(instance));

            StepDown(engine, instance, cd: false, load: false, pv: 1);
            StepDown(engine, instance, cd: true, load: false, pv: 1);
            Assert.Equal(0, Cv(instance));
            Assert.Equal(true, Q(instance));

            StepDown(engine, instance, cd: false, load: false, pv: 1);
            StepDown(engine, instance, cd: true, load: false, pv: 1);
            Assert.Equal(0, Cv(instance));
        }

        // --- CTUD: RESET beats LOAD beats counting ---

        [Fact]
        public void Ctud_CountsUpAndDownOnRisingEdgesOnly()
        {
            var engine = NewCtudEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            StepUpDown(engine, instance, cu: true, cd: false, reset: false, load: false, pv: 2);
            Assert.Equal(1, Cv(instance));

            // CU held high - no second edge.
            StepUpDown(engine, instance, cu: true, cd: false, reset: false, load: false, pv: 2);
            Assert.Equal(1, Cv(instance));

            StepUpDown(engine, instance, cu: false, cd: false, reset: false, load: false, pv: 2);
            StepUpDown(engine, instance, cu: true, cd: false, reset: false, load: false, pv: 2);
            Assert.Equal(2, Cv(instance));
            Assert.Equal(true, Qu(instance));
            Assert.Equal(false, Qd(instance));

            StepUpDown(engine, instance, cu: false, cd: true, reset: false, load: false, pv: 2);
            Assert.Equal(1, Cv(instance));
            Assert.Equal(false, Qu(instance));

            // CD held high - no second decrement.
            StepUpDown(engine, instance, cu: false, cd: true, reset: false, load: false, pv: 2);
            Assert.Equal(1, Cv(instance));

            StepUpDown(engine, instance, cu: false, cd: false, reset: false, load: false, pv: 2);
            StepUpDown(engine, instance, cu: false, cd: true, reset: false, load: false, pv: 2);
            Assert.Equal(0, Cv(instance));
            Assert.Equal(true, Qd(instance));
        }

        [Fact]
        public void Ctud_LoadInitializesCvWithPvAndBeatsCounting()
        {
            var engine = NewCtudEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            StepUpDown(engine, instance, cu: false, cd: false, reset: false, load: true, pv: 7);
            Assert.Equal(7, Cv(instance));
            Assert.Equal(true, Qu(instance));
            Assert.Equal(false, Qd(instance));

            // Rising CU edge AND LOAD: LOAD wins, so CV is the preset, not 8.
            StepUpDown(engine, instance, cu: true, cd: false, reset: false, load: true, pv: 7);
            Assert.Equal(7, Cv(instance));
        }

        [Fact]
        public void Ctud_ResetTakesPrecedenceOverLoadAndCounting()
        {
            var engine = NewCtudEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            StepUpDown(engine, instance, cu: false, cd: false, reset: false, load: true, pv: 7);
            Assert.Equal(7, Cv(instance));

            // RESET and LOAD together: RESET wins.
            StepUpDown(engine, instance, cu: true, cd: false, reset: true, load: true, pv: 7);
            Assert.Equal(0, Cv(instance));
            Assert.Equal(false, Qu(instance));
            Assert.Equal(true, Qd(instance));
        }

        // IEC 61131-3's NOT (CU AND CD) guard on the CTUD body: coincident up
        // and down edges cancel instead of one branch silently winning.
        [Fact]
        public void Ctud_SimultaneousUpAndDownEdgesCancel()
        {
            var engine = NewCtudEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            StepUpDown(engine, instance, cu: false, cd: false, reset: false, load: true, pv: 4);
            Assert.Equal(4, Cv(instance));

            StepUpDown(engine, instance, cu: true, cd: true, reset: false, load: false, pv: 4);
            Assert.Equal(4, Cv(instance));
        }

        [Fact]
        public void Ctud_SaturatesAtBothWordLimits()
        {
            var engine = NewCtudEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            SeedCounterValue(instance, 65535);

            StepUpDown(engine, instance, cu: true, cd: false, reset: false, load: false, pv: 65535);
            Assert.Equal(65535, Cv(instance));

            StepUpDown(engine, instance, cu: false, cd: false, reset: false, load: false, pv: 65535);
            StepUpDown(engine, instance, cu: true, cd: false, reset: false, load: false, pv: 65535);
            Assert.Equal(65535, Cv(instance));

            // Floor: RESET to 0, then a CD edge must not wrap back to 65535.
            StepUpDown(engine, instance, cu: false, cd: false, reset: true, load: false, pv: 65535);
            Assert.Equal(0, Cv(instance));

            StepUpDown(engine, instance, cu: false, cd: true, reset: false, load: false, pv: 65535);
            Assert.Equal(0, Cv(instance));
            Assert.Equal(true, Qd(instance));
        }

        // --- wiring ---

        [Fact]
        public void PositionalArguments_BindInIecDeclarationOrder()
        {
            var pou = new PouAst(
                "FB_Wrapper",
                null,
                "VAR\n\tfbCounter : CTU;\n\tcuVar : BOOL;\n\tresetVar : BOOL;\n\tpvVar : WORD;\n\tmeasuredQ : BOOL;\n\tmeasuredCV : WORD;\nEND_VAR",
                "fbCounter(cuVar, resetVar, pvVar);\nmeasuredQ := fbCounter.Q;\nmeasuredCV := fbCounter.CV;",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_Wrapper");

            StepUp(engine, instance, cu: true, reset: false, pv: 1);
            Assert.Equal(1, Cv(instance));
            Assert.Equal(true, Q(instance));

            StepUp(engine, instance, cu: true, reset: true, pv: 1);
            Assert.Equal(0, Cv(instance));
            Assert.Equal(false, Q(instance));
        }

        // TcXunit-nch: the type-name lookup that routes to this host is
        // case-insensitive, so a lowercase/mixed-case spelling must reach the
        // same host instead of falling through to NativeHostKind.Suite.
        [Theory]
        [InlineData("ctu")]
        [InlineData("Ctu")]
        public void LowercaseOrMixedCaseCtu_InstantiatesAsNativeCounterHost(string typeName)
        {
            var engine = NewCtuEngine(typeName);
            var instance = engine.NewInstance("FB_Wrapper");
            var counter = (FbInstance)instance.Fields["fbCounter"].Value;

            Assert.Equal(NativeHostKind.Counter, counter.NativeKind);
            Assert.NotNull(counter.NativeCounterHost);

            StepUp(engine, instance, cu: true, reset: false, pv: 1);
            Assert.Equal(1, Cv(instance));
        }

        [Theory]
        [InlineData("ctd")]
        [InlineData("Ctd")]
        public void LowercaseOrMixedCaseCtd_InstantiatesAsNativeCounterHost(string typeName)
        {
            var engine = NewCtdEngine(typeName);
            var instance = engine.NewInstance("FB_Wrapper");
            var counter = (FbInstance)instance.Fields["fbCounter"].Value;

            Assert.Equal(NativeHostKind.Counter, counter.NativeKind);
            Assert.NotNull(counter.NativeCounterHost);

            StepDown(engine, instance, cd: false, load: true, pv: 2);
            Assert.Equal(2, Cv(instance));
        }

        [Theory]
        [InlineData("ctud")]
        [InlineData("CtUd")]
        public void LowercaseOrMixedCaseCtud_InstantiatesAsNativeCounterHost(string typeName)
        {
            var engine = NewCtudEngine(typeName);
            var instance = engine.NewInstance("FB_Wrapper");
            var counter = (FbInstance)instance.Fields["fbCounter"].Value;

            Assert.Equal(NativeHostKind.Counter, counter.NativeKind);
            Assert.NotNull(counter.NativeCounterHost);

            StepUpDown(engine, instance, cu: false, cd: false, reset: false, load: true, pv: 2);
            Assert.Equal(2, Cv(instance));
        }

        [Fact]
        public void EachCounterInstance_KeepsItsOwnEdgeMemoryAndCounterValue()
        {
            var pou = new PouAst(
                "FB_Wrapper",
                null,
                "VAR\n\tfbCounter : CTU;\n\tfbOther : CTU;\n\tcuVar : BOOL;\n\tresetVar : BOOL;\n\tpvVar : WORD;\n\tmeasuredQ : BOOL;\n\tmeasuredCV : WORD;\n\totherCV : WORD;\nEND_VAR",
                "fbCounter(CU:=cuVar, RESET:=resetVar, PV:=pvVar);\nmeasuredQ := fbCounter.Q;\nmeasuredCV := fbCounter.CV;\notherCV := fbOther.CV;",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_Wrapper");

            StepUp(engine, instance, cu: true, reset: false, pv: 3);
            StepUp(engine, instance, cu: false, reset: false, pv: 3);
            StepUp(engine, instance, cu: true, reset: false, pv: 3);

            Assert.Equal(2, Cv(instance));
            Assert.Equal(0, instance.Fields["otherCV"].Value);
        }
    }
}
