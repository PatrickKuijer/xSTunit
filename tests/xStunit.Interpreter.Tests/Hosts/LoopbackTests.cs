using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // Loopback moves a value only on an explicit Transmit(source, sink) call:
    // a discrete copy through REFERENCE TO-style field-access binding, never a
    // standing wire that StepCycles re-evaluates. That keeps a test's data flow
    // where the test can see it, at the cost of having to say so every cycle.
    public class LoopbackTests
    {
        private static Engine NewWrapperEngine()
        {
            var payload = new PouAst(
                "FB_Payload",
                null,
                "VAR\n\tBuffer : INT;\nEND_VAR",
                "",
                new List<MethodAst>());

            var wrapper = new PouAst(
                "FB_Wrapper",
                null,
                "VAR\n\tfbLink : Loopback;\n\ttxFb : FB_Payload;\n\trxFb : FB_Payload;\nEND_VAR",
                "",
                new List<MethodAst>());

            return new Engine(new TypeRegistry(new[] { payload, wrapper }));
        }

        private static void Transmit(Engine engine, FbInstance wrapper) =>
            engine.CallMethod(
                (FbInstance)wrapper.Fields["fbLink"].Value,
                "Transmit",
                new Expr[0],
                new[]
                {
                    new NamedArg("source", new FieldAccessExpr(new IdentifierExpr("txFb"), "Buffer")),
                    new NamedArg("sink", new FieldAccessExpr(new IdentifierExpr("rxFb"), "Buffer")),
                },
                new Frame(wrapper, "FB_Wrapper"),
                null);

        [Fact]
        public void Transmit_CopiesScalarValue_OnExplicitCall()
        {
            var engine = NewWrapperEngine();
            var wrapper = engine.NewInstance("FB_Wrapper");
            var txFb = (FbInstance)wrapper.Fields["txFb"].Value;
            var rxFb = (FbInstance)wrapper.Fields["rxFb"].Value;

            txFb.Fields["Buffer"].Value = 42;
            Transmit(engine, wrapper);

            Assert.Equal(42, rxFb.Fields["Buffer"].Value);
        }

        [Fact]
        public void Transmit_DoesNotAutoWire_SinkUnchangedWithoutExplicitCall()
        {
            var engine = NewWrapperEngine();
            var wrapper = engine.NewInstance("FB_Wrapper");
            var txFb = (FbInstance)wrapper.Fields["txFb"].Value;
            var rxFb = (FbInstance)wrapper.Fields["rxFb"].Value;

            txFb.Fields["Buffer"].Value = 99;
            engine.CallMethod(wrapper, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            Assert.Equal(0, rxFb.Fields["Buffer"].Value);
        }

        [Fact]
        public void Transmit_LaterSourceChange_NotReflectedUntilNextExplicitCall()
        {
            var engine = NewWrapperEngine();
            var wrapper = engine.NewInstance("FB_Wrapper");
            var txFb = (FbInstance)wrapper.Fields["txFb"].Value;
            var rxFb = (FbInstance)wrapper.Fields["rxFb"].Value;

            txFb.Fields["Buffer"].Value = 1;
            Transmit(engine, wrapper);
            Assert.Equal(1, rxFb.Fields["Buffer"].Value);

            txFb.Fields["Buffer"].Value = 2;
            Assert.Equal(1, rxFb.Fields["Buffer"].Value);
        }

        [Fact]
        public void Transmit_StructPayload_ClonesFieldsInsteadOfAliasing()
        {
            var stPoint = StructDeclParser.Parse(@"TYPE ST_Point :
STRUCT
	x : INT;
END_STRUCT
END_TYPE");
            var payload = new PouAst(
                "FB_Payload",
                null,
                "VAR\n\tBuffer : ST_Point;\nEND_VAR",
                "",
                new List<MethodAst>());
            var wrapper = new PouAst(
                "FB_Wrapper",
                null,
                "VAR\n\tfbLink : Loopback;\n\ttxFb : FB_Payload;\n\trxFb : FB_Payload;\nEND_VAR",
                "",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { payload, wrapper }, new[] { stPoint }));

            var wrapperInstance = engine.NewInstance("FB_Wrapper");
            var txFb = (FbInstance)wrapperInstance.Fields["txFb"].Value;
            var rxFb = (FbInstance)wrapperInstance.Fields["rxFb"].Value;

            var sourceStruct = (StructInstance)txFb.Fields["Buffer"].Value;
            sourceStruct.Fields["x"].Value = 5;

            Transmit(engine, wrapperInstance);

            var sinkStruct = (StructInstance)rxFb.Fields["Buffer"].Value;
            Assert.Equal(5, sinkStruct.Fields["x"].Value);

            sourceStruct.Fields["x"].Value = 999;

            Assert.Equal(5, sinkStruct.Fields["x"].Value);
            Assert.NotSame(sourceStruct, sinkStruct);
        }
    }
}
