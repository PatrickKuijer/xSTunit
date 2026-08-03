using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A FUNCTION_BLOCK's own top-level ST body only runs when the FB itself is invoked - nothing
    // implicit triggers it. A nested FB field that is wired only inside that body (a VAR_INPUT
    // interface reference proxied down onto a child instance, say) stays at its unassigned default
    // until the owner is actually invoked, even once the owner's own VAR_INPUT has already been set
    // directly. This holds even when the check is reached through a VAR_IN_OUT-forwarded FB
    // reference and a nested method call - the forwarded reference itself compares correctly either
    // way (see InterfaceReferenceEqualityTests); what changes is only whether the wiring ran.
    public class NestedInterfaceWiringTests
    {
        private static (PouAst Handler, PouAst Builder, PouAst Host) BuildFixture()
        {
            var handler = new PouAst(
                "FB_FakeHandler",
                null,
                "VAR\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    new MethodAst("bDoWork", "METHOD bDoWork : BOOL", "bDoWork := TRUE;"),
                });

            var builder = new PouAst(
                "FB_WidgetLabelBuilder",
                null,
                "VAR_INPUT\n\tiipHandler : ITF_Handler;\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    new MethodAst(
                        "M_CheckHandlerAssigned",
                        "METHOD M_CheckHandlerAssigned : BOOL",
                        "M_CheckHandlerAssigned := (iipHandler <> 0) AND_THEN iipHandler.bDoWork();"),
                });

            var host = new PouAst(
                "FB_WidgetHost",
                null,
                "VAR_INPUT\n\tiipHandler : ITF_Handler;\nEND_VAR\nVAR\n\tsfbBuilder : FB_WidgetLabelBuilder;\nEND_VAR",
                "sfbBuilder.iipHandler := iipHandler;",
                new List<MethodAst>
                {
                    new MethodAst(
                        "M_PumpCheck",
                        "METHOD M_PumpCheck : BOOL\nVAR_IN_OUT\n\tioHost : FB_WidgetHost;\nEND_VAR",
                        "M_PumpCheck := ioHost.sfbBuilder.M_CheckHandlerAssigned();"),
                });

            return (handler, builder, host);
        }

        [Fact]
        public void HostBodyNeverInvoked_NestedBuilderFieldStaysUnwired_ForwardedCheckIsFalse()
        {
            var (handler, builder, host) = BuildFixture();
            var engine = new Engine(new TypeRegistry(new[] { handler, builder, host }));
            var hostInstance = engine.NewInstance("FB_WidgetHost");
            hostInstance.Fields["iipHandler"].Value = engine.NewInstance("FB_FakeHandler");

            var frame = new Frame(hostInstance, "FB_WidgetHost");
            var result = engine.CallMethod(
                hostInstance, "M_PumpCheck", new Expr[] { new ThisRefExpr() }, new NamedArg[0], frame, null);

            Assert.False((bool)result);
        }

        [Fact]
        public void HostBodyInvoked_WiringPropagates_ForwardedCheckConvergesTrue()
        {
            var (handler, builder, host) = BuildFixture();
            var engine = new Engine(new TypeRegistry(new[] { handler, builder, host }));
            var hostInstance = engine.NewInstance("FB_WidgetHost");
            hostInstance.Fields["iipHandler"].Value = engine.NewInstance("FB_FakeHandler");

            engine.CallMethod(hostInstance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            var frame = new Frame(hostInstance, "FB_WidgetHost");
            var result = engine.CallMethod(
                hostInstance, "M_PumpCheck", new Expr[] { new ThisRefExpr() }, new NamedArg[0], frame, null);

            Assert.True((bool)result);
        }
    }
}
