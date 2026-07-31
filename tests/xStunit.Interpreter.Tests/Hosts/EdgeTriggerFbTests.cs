using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcXunit-f6b (TcXunit-jwb/TcXunit-t1q): R_TRIG/F_TRIG native stubs,
    // exercised the same way as TimerFbTests - a wrapper FB does the bare
    // invocation (fbTrig(CLK:=..)) and reads Q back via plain field access.
    public class EdgeTriggerFbTests
    {
        private static Engine NewWrapperEngine(string triggerTypeName)
        {
            var pou = new PouAst(
                "FB_Wrapper",
                null,
                "VAR\n\tfbTrig : " + triggerTypeName + ";\n\tclkVar : BOOL;\n\tmeasuredQ : BOOL;\nEND_VAR",
                "fbTrig(CLK:=clkVar);\nmeasuredQ := fbTrig.Q;",
                new List<MethodAst>());

            return new Engine(new TypeRegistry(new[] { pou }));
        }

        private static void Step(Engine engine, FbInstance instance) =>
            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

        [Fact]
        public void RTrig_NoEdge_QStaysFalse()
        {
            var engine = NewWrapperEngine("R_TRIG");
            var instance = engine.NewInstance("FB_Wrapper");
            instance.Fields["clkVar"].Value = false;

            Step(engine, instance);
            Assert.Equal(false, instance.Fields["measuredQ"].Value);
        }

        [Fact]
        public void RTrig_RisingEdge_FiresOnce()
        {
            var engine = NewWrapperEngine("R_TRIG");
            var instance = engine.NewInstance("FB_Wrapper");
            instance.Fields["clkVar"].Value = false;
            Step(engine, instance); // baseline - no edge

            instance.Fields["clkVar"].Value = true;
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);

            Step(engine, instance); // held high - Q drops after first cycle
            Assert.Equal(false, instance.Fields["measuredQ"].Value);
        }

        [Fact]
        public void RTrig_RefiresAfterFallingThenRisingAgain()
        {
            var engine = NewWrapperEngine("R_TRIG");
            var instance = engine.NewInstance("FB_Wrapper");
            instance.Fields["clkVar"].Value = false;
            Step(engine, instance);

            instance.Fields["clkVar"].Value = true;
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);

            instance.Fields["clkVar"].Value = false;
            Step(engine, instance);
            Assert.Equal(false, instance.Fields["measuredQ"].Value);

            instance.Fields["clkVar"].Value = true;
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);
        }

        [Fact]
        public void FTrig_NoEdge_QStaysFalse()
        {
            var engine = NewWrapperEngine("F_TRIG");
            var instance = engine.NewInstance("FB_Wrapper");
            instance.Fields["clkVar"].Value = true;

            Step(engine, instance);
            Assert.Equal(false, instance.Fields["measuredQ"].Value);
        }

        [Fact]
        public void FTrig_FallingEdge_FiresOnce()
        {
            var engine = NewWrapperEngine("F_TRIG");
            var instance = engine.NewInstance("FB_Wrapper");
            instance.Fields["clkVar"].Value = true;
            Step(engine, instance); // baseline - no edge

            instance.Fields["clkVar"].Value = false;
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);

            Step(engine, instance); // held low - Q drops after first cycle
            Assert.Equal(false, instance.Fields["measuredQ"].Value);
        }

        [Fact]
        public void FTrig_RefiresAfterRisingThenFallingAgain()
        {
            var engine = NewWrapperEngine("F_TRIG");
            var instance = engine.NewInstance("FB_Wrapper");
            instance.Fields["clkVar"].Value = true;
            Step(engine, instance);

            instance.Fields["clkVar"].Value = false;
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);

            instance.Fields["clkVar"].Value = true;
            Step(engine, instance);
            Assert.Equal(false, instance.Fields["measuredQ"].Value);

            instance.Fields["clkVar"].Value = false;
            Step(engine, instance);
            Assert.Equal(true, instance.Fields["measuredQ"].Value);
        }
    }
}
