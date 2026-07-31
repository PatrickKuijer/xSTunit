using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcXunit-ejjl: RS/SR native stubs, exercised the same way as
    // EdgeTriggerFbTests - a wrapper FB does the bare invocation
    // (fbLatch(SET:=.., RESET1:=..)) and reads Q1 back via plain field access.
    // The two latches are wrapped separately because their input names differ
    // (RS: SET/RESET1, SR: SET1/RESET).
    public class BistableLatchFbTests
    {
        private static Engine NewWrapperEngine(string latchTypeName, string setInput, string resetInput)
        {
            var pou = new PouAst(
                "FB_Wrapper",
                null,
                "VAR\n\tfbLatch : " + latchTypeName + ";\n\tsetVar : BOOL;\n\tresetVar : BOOL;\n\tmeasuredQ1 : BOOL;\nEND_VAR",
                "fbLatch(" + setInput + ":=setVar, " + resetInput + ":=resetVar);\nmeasuredQ1 := fbLatch.Q1;",
                new List<MethodAst>());

            return new Engine(new TypeRegistry(new[] { pou }));
        }

        private static Engine NewRsEngine() => NewWrapperEngine("RS", "SET", "RESET1");

        private static Engine NewSrEngine() => NewWrapperEngine("SR", "SET1", "RESET");

        private static void Step(Engine engine, FbInstance instance, bool set, bool reset)
        {
            instance.Fields["setVar"].Value = set;
            instance.Fields["resetVar"].Value = reset;
            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);
        }

        private static object Q1(FbInstance instance) => instance.Fields["measuredQ1"].Value;

        // --- RS (reset-dominant): Q1 := NOT RESET1 AND (Q1 OR SET) ---

        [Fact]
        public void Rs_StartsLow()
        {
            var engine = NewRsEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            Step(engine, instance, set: false, reset: false);
            Assert.Equal(false, Q1(instance));
        }

        [Fact]
        public void Rs_SetLatchesAndHoldsAfterSetDrops()
        {
            var engine = NewRsEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            Step(engine, instance, set: true, reset: false);
            Assert.Equal(true, Q1(instance));

            Step(engine, instance, set: false, reset: false);
            Assert.Equal(true, Q1(instance));

            Step(engine, instance, set: false, reset: false);
            Assert.Equal(true, Q1(instance));
        }

        [Fact]
        public void Rs_ResetClearsAndStaysCleared()
        {
            var engine = NewRsEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            Step(engine, instance, set: true, reset: false);
            Assert.Equal(true, Q1(instance));

            Step(engine, instance, set: false, reset: true);
            Assert.Equal(false, Q1(instance));

            Step(engine, instance, set: false, reset: false);
            Assert.Equal(false, Q1(instance));
        }

        [Fact]
        public void Rs_ResetDominatesWhenBothInputsTrue()
        {
            var engine = NewRsEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            // Latched first, so this proves reset wins over the held state...
            Step(engine, instance, set: true, reset: false);
            Assert.Equal(true, Q1(instance));

            Step(engine, instance, set: true, reset: true);
            Assert.Equal(false, Q1(instance));

            // ...and from a cleared state, that SET cannot get through RESET1.
            Step(engine, instance, set: true, reset: true);
            Assert.Equal(false, Q1(instance));

            Step(engine, instance, set: true, reset: false);
            Assert.Equal(true, Q1(instance));
        }

        // --- SR (set-dominant): Q1 := (NOT RESET AND Q1) OR SET1 ---

        [Fact]
        public void Sr_StartsLow()
        {
            var engine = NewSrEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            Step(engine, instance, set: false, reset: false);
            Assert.Equal(false, Q1(instance));
        }

        [Fact]
        public void Sr_SetLatchesAndHoldsAfterSetDrops()
        {
            var engine = NewSrEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            Step(engine, instance, set: true, reset: false);
            Assert.Equal(true, Q1(instance));

            Step(engine, instance, set: false, reset: false);
            Assert.Equal(true, Q1(instance));

            Step(engine, instance, set: false, reset: false);
            Assert.Equal(true, Q1(instance));
        }

        [Fact]
        public void Sr_ResetClearsAndStaysCleared()
        {
            var engine = NewSrEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            Step(engine, instance, set: true, reset: false);
            Assert.Equal(true, Q1(instance));

            Step(engine, instance, set: false, reset: true);
            Assert.Equal(false, Q1(instance));

            Step(engine, instance, set: false, reset: false);
            Assert.Equal(false, Q1(instance));
        }

        [Fact]
        public void Sr_SetDominatesWhenBothInputsTrue()
        {
            var engine = NewSrEngine();
            var instance = engine.NewInstance("FB_Wrapper");

            // From a cleared state, SET1 gets through RESET...
            Step(engine, instance, set: true, reset: true);
            Assert.Equal(true, Q1(instance));

            // ...and keeps holding it high while both stay true.
            Step(engine, instance, set: true, reset: true);
            Assert.Equal(true, Q1(instance));

            Step(engine, instance, set: false, reset: true);
            Assert.Equal(false, Q1(instance));
        }

        // The whole point of the asymmetric parameter names: driving RS and SR
        // with the same set/reset sequence differs only on the both-true cycle.
        [Fact]
        public void RsAndSr_DifferOnlyWhenBothInputsAreTrue()
        {
            var rsEngine = NewRsEngine();
            var rs = rsEngine.NewInstance("FB_Wrapper");
            var srEngine = NewSrEngine();
            var sr = srEngine.NewInstance("FB_Wrapper");

            var sequence = new[]
            {
                (set: false, reset: false),
                (set: true, reset: false),
                (set: false, reset: false),
                (set: false, reset: true),
                (set: true, reset: false),
            };

            foreach (var (set, reset) in sequence)
            {
                Step(rsEngine, rs, set, reset);
                Step(srEngine, sr, set, reset);
                Assert.Equal(Q1(rs), Q1(sr));
            }

            Step(rsEngine, rs, set: true, reset: true);
            Step(srEngine, sr, set: true, reset: true);
            Assert.Equal(false, Q1(rs));
            Assert.Equal(true, Q1(sr));
        }

        // TcXunit-nch: the type-name lookup that routes to this host is
        // case-insensitive, so a lowercase/mixed-case spelling must reach the
        // same host instead of falling through to NativeHostKind.Suite.
        [Theory]
        [InlineData("rs")]
        [InlineData("Rs")]
        public void LowercaseOrMixedCaseRs_InstantiatesAsNativeBistableLatchHost(string typeName)
        {
            var engine = NewWrapperEngine(typeName, "SET", "RESET1");
            var instance = engine.NewInstance("FB_Wrapper");
            var latch = (FbInstance)instance.Fields["fbLatch"].Value;

            Assert.Equal(NativeHostKind.BistableLatch, latch.NativeKind);
            Assert.NotNull(latch.NativeBistableLatchHost);

            Step(engine, instance, set: true, reset: true);
            Assert.Equal(false, Q1(instance));
        }

        [Theory]
        [InlineData("sr")]
        [InlineData("Sr")]
        public void LowercaseOrMixedCaseSr_InstantiatesAsNativeBistableLatchHost(string typeName)
        {
            var engine = NewWrapperEngine(typeName, "SET1", "RESET");
            var instance = engine.NewInstance("FB_Wrapper");
            var latch = (FbInstance)instance.Fields["fbLatch"].Value;

            Assert.Equal(NativeHostKind.BistableLatch, latch.NativeKind);
            Assert.NotNull(latch.NativeBistableLatchHost);

            Step(engine, instance, set: true, reset: true);
            Assert.Equal(true, Q1(instance));
        }
    }
}
