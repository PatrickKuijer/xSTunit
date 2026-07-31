using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcXunit-w5x.15.4: FbInstance.StepCycles(n) re-invokes the FB's top-level
    // body n times, reusing Cell state across calls. No dt param, no scheduler.
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

        // TcXunit-z1l: a top-level RETURN inside the stepped FB's cyclic body
        // must only end that cycle, not unwind into whatever ST statement
        // (e.g. a caller's METHOD) invoked StepCycles.
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

        // TcXunit-agc: the try/catch(MethodReturnSignal) in StepCycles's
        // `for` loop is per-iteration, so a RETURN that ends cycle N must
        // not prevent cycles N+1..cycles from running. The body flips
        // ibEnable on its own RETURN-taking cycle so a single StepCycles(3)
        // call exercises both the RETURN path (cycle 1) and normal
        // completion (cycles 2 and 3) without external re-entry.
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

            // Cycle 1: ibEnable starts FALSE, so it flips to TRUE and RETURNs
            // before incrementing Count. Cycles 2 and 3 then see ibEnable ==
            // TRUE and run to completion, each incrementing Count. If the
            // MethodReturnSignal from cycle 1 aborted the whole cycles loop
            // instead of just that iteration, Count would still be 0.
            Assert.Equal(2, instance.Fields["Count"].Value);
        }
    }
}
