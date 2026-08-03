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
                    new MethodAst(
                        "M_Bridge",
                        "METHOD M_Bridge : BOOL\nVAR_IN_OUT\n\tioHost2 : FB_WidgetHost;\nEND_VAR",
                        "M_Bridge := M_PumpCheck(ioHost2);"),
                });

            return (handler, builder, host);
        }

        // A caller external to FB_WidgetHost holding a reference to it, forwarding that reference
        // into FB_WidgetHost.M_PumpCheck's own VAR_IN_OUT parameter - mirrors a helper method
        // receiving an FB reference from outside rather than an FB pumping itself.
        private static Frame ExternalCallerFrame(FbInstance hostInstance)
        {
            var frame = new Frame(null, "Driver");
            frame.Locals["hostRef"] = new Cell { Value = hostInstance };
            return frame;
        }

        [Fact]
        public void HostBodyNeverInvoked_NestedBuilderFieldStaysUnwired_ForwardedCheckIsFalse()
        {
            var (handler, builder, host) = BuildFixture();
            var engine = new Engine(new TypeRegistry(new[] { handler, builder, host }));
            var hostInstance = engine.NewInstance("FB_WidgetHost");
            hostInstance.Fields["iipHandler"].Value = engine.NewInstance("FB_FakeHandler");

            var callerFrame = ExternalCallerFrame(hostInstance);
            var result = engine.CallMethod(
                hostInstance, "M_PumpCheck", new Expr[] { new IdentifierExpr("hostRef") }, new NamedArg[0], callerFrame, null);

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

            var callerFrame = ExternalCallerFrame(hostInstance);
            var result = engine.CallMethod(
                hostInstance, "M_PumpCheck", new Expr[] { new IdentifierExpr("hostRef") }, new NamedArg[0], callerFrame, null);

            Assert.True((bool)result);
        }

        // Generalizes the above through a second VAR_IN_OUT hop (external caller -> M_Bridge ->
        // M_PumpCheck -> the nested interface check) - the forwarded reference and its wiring state
        // survive an extra level of pumping, not just one.
        [Fact]
        public void ForwardedThroughTwoNestedVarInOutHops_StillConvergesTrue()
        {
            var (handler, builder, host) = BuildFixture();
            var engine = new Engine(new TypeRegistry(new[] { handler, builder, host }));
            var hostInstance = engine.NewInstance("FB_WidgetHost");
            hostInstance.Fields["iipHandler"].Value = engine.NewInstance("FB_FakeHandler");

            engine.CallMethod(hostInstance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            var callerFrame = ExternalCallerFrame(hostInstance);
            var result = engine.CallMethod(
                hostInstance, "M_Bridge", new Expr[] { new IdentifierExpr("hostRef") }, new NamedArg[0], callerFrame, null);

            Assert.True((bool)result);
        }
    }
}
