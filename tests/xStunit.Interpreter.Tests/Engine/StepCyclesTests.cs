using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // StepCycles(n) re-invokes an FB's top-level body n times, carrying Cell
    // state across cycles. It models the PLC scan, not time: there is no dt and
    // no scheduler, so a cycle is only ever "one more pass over the body".
    public class StepCyclesTests
    {
        private static Engine NewCounterEngine()
        {
            var pou = new PouAst(
                "FB_Counter",
                null,
                "VAR\n\tCount : INT;\nEND_VAR",
                "Count := Count + 1;",
                new List<MethodAst>());

            return new Engine(new TypeRegistry(new[] { pou }));
        }

        [Fact]
        public void StepCycles_ReinvokesBodyNTimes_PreservingCellState()
        {
            var engine = NewCounterEngine();
            var instance = engine.NewInstance("FB_Counter");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(3) }, new NamedArg[0], null, null);

            Assert.Equal(3, instance.Fields["Count"].Value);
        }

        [Fact]
        public void StepCycles_MultipleInstances_StepIndependentlyInCallerOrder()
        {
            var engine = NewCounterEngine();
            var a = engine.NewInstance("FB_Counter");
            var b = engine.NewInstance("FB_Counter");

            engine.CallMethod(a, "StepCycles", new Expr[] { new IntLiteralExpr(2) }, new NamedArg[0], null, null);
            engine.CallMethod(b, "StepCycles", new Expr[] { new IntLiteralExpr(5) }, new NamedArg[0], null, null);
            engine.CallMethod(a, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            Assert.Equal(3, a.Fields["Count"].Value);
            Assert.Equal(5, b.Fields["Count"].Value);
        }

        // A top-level RETURN in a cyclic body ends that cycle only. Letting it
        // unwind further would abandon the rest of whatever ST statement
        // invoked StepCycles.
        [Fact]
        public void StepCycles_TopLevelReturnInBody_DoesNotEscapeIntoCaller()
        {
            var body = new PouAst(
                "FB_Body",
                null,
                "VAR_INPUT\n\tibEnable : BOOL;\nEND_VAR\nVAR\n\tCount : INT;\nEND_VAR",
                "IF NOT(ibEnable) THEN\n\tRETURN;\nEND_IF\nCount := Count + 1;",
                new List<MethodAst>());

            var caller = new PouAst(
                "FB_Caller",
                null,
                "VAR\n\tsfb : FB_Body;\n\tMarker : BOOL;\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    new MethodAst("Run", "", "sfb.StepCycles(1);\nMarker := TRUE;")
                });

            var engine = new Engine(new TypeRegistry(new[] { body, caller }));
            var instance = engine.NewInstance("FB_Caller");

            engine.CallMethod(instance, "Run", new Expr[0], new NamedArg[0], null, null);

            Assert.True((bool)instance.Fields["Marker"].Value);
            var nested = (FbInstance)instance.Fields["sfb"].Value;
            Assert.Equal(0, nested.Fields["Count"].Value);
        }

        // A RETURN that ends cycle N must not stop cycles N+1 onward from
        // running - the scan continues. The body flips its own enable on the
        // RETURN-taking cycle so one StepCycles(3) covers both paths without
        // any external re-entry.
        [Fact]
        public void StepCycles_ReturnInEarlyCycle_DoesNotAbortRemainingCycles()
        {
            var pou = new PouAst(
                "FB_ToggleCounter",
                null,
                "VAR_INPUT\n\tibEnable : BOOL;\nEND_VAR\nVAR\n\tCount : INT;\nEND_VAR",
                "IF NOT(ibEnable) THEN\n\tibEnable := TRUE;\n\tRETURN;\nEND_IF\nCount := Count + 1;",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_ToggleCounter");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(3) }, new NamedArg[0], null, null);

            // Cycle 1 flips the enable and returns before incrementing; cycles
            // 2 and 3 each increment. A RETURN that aborted the whole loop
            // rather than one iteration would leave Count at 0.
            Assert.Equal(2, instance.Fields["Count"].Value);
        }
    }
}
