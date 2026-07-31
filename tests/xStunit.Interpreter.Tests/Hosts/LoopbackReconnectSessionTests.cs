using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // Worked example of the session/generation-counter reconnect pattern
    // (wiki/05-session-counter-reconnect.md), and the guard that the existing
    // primitives suffice to express it: Generation is an ordinary VAR field on
    // the FB under test, bumped by ST assignment between Loopback's
    // Drop()/Restore(). Nothing here reaches into Loopback's internals, and the
    // assertions read only the FB's own reconnect-path output - so if these go
    // red, the pattern itself has stopped being expressible, not just a
    // helper.
    public class LoopbackReconnectSessionTests
    {
        private static Engine NewWrapperEngine()
        {
            var controller = new PouAst(
                "FB_ControllerUnderTest",
                null,
                "VAR\n" +
                "\tGeneration : UDINT;\n" +
                "\tlastSeenGeneration : UDINT;\n" +
                "\tlinkWasUp : BOOL;\n" +
                "\tReregistrationCount : UDINT;\n" +
                "END_VAR",
                "",
                new List<MethodAst>
                {
                    new MethodAst(
                        "Poll",
                        "METHOD PUBLIC Poll\nVAR_INPUT\n\tlinkUp : BOOL;\nEND_VAR",
                        "IF linkUp AND NOT linkWasUp THEN\n" +
                        "\tIF Generation <> lastSeenGeneration THEN\n" +
                        "\t\tReregistrationCount := ReregistrationCount + 1;\n" +
                        "\t\tlastSeenGeneration := Generation;\n" +
                        "\tEND_IF\n" +
                        "END_IF\n" +
                        "linkWasUp := linkUp;"),
                });

            var wrapper = new PouAst(
                "FB_Wrapper",
                null,
                "VAR\n\tfbLink : Loopback;\n\tfbA : FB_ControllerUnderTest;\nEND_VAR",
                "",
                new List<MethodAst>());

            return new Engine(new TypeRegistry(new[] { controller, wrapper }));
        }

        private static FbInstance Link(FbInstance wrapper) => (FbInstance)wrapper.Fields["fbLink"].Value;

        private static FbInstance Controller(FbInstance wrapper) => (FbInstance)wrapper.Fields["fbA"].Value;

        private static void Fault(Engine engine, FbInstance wrapper, string methodName) =>
            engine.CallMethod(Link(wrapper), methodName, new Expr[0], new NamedArg[0], new Frame(wrapper, "FB_Wrapper"), null);

        private static void Poll(Engine engine, FbInstance wrapper) =>
            engine.CallMethod(
                Controller(wrapper),
                "Poll",
                new Expr[0],
                new[] { new NamedArg("linkUp", new FieldAccessExpr(new IdentifierExpr("fbLink"), "LinkUp")) },
                new Frame(wrapper, "FB_Wrapper"),
                null);

        private static long ReregistrationCount(FbInstance wrapper) =>
            (long)Controller(wrapper).Fields["ReregistrationCount"].Value;

        [Fact]
        public void BriefBlip_GenerationUnchanged_DoesNotCountAsReconnect()
        {
            var engine = NewWrapperEngine();
            var wrapper = engine.NewInstance("FB_Wrapper");

            Fault(engine, wrapper, "Drop");
            Poll(engine, wrapper);
            Fault(engine, wrapper, "Restore");
            Poll(engine, wrapper);

            Assert.Equal(0L, ReregistrationCount(wrapper));
        }

        [Fact]
        public void SimulatedRestart_GenerationBumped_CountsAsReconnect()
        {
            var engine = NewWrapperEngine();
            var wrapper = engine.NewInstance("FB_Wrapper");
            var controller = Controller(wrapper);

            Fault(engine, wrapper, "Drop");
            Poll(engine, wrapper);
            controller.Fields["Generation"].Value = (long)controller.Fields["Generation"].Value + 1;
            Fault(engine, wrapper, "Restore");
            Poll(engine, wrapper);

            Assert.Equal(1L, ReregistrationCount(wrapper));
        }
    }
}
