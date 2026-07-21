using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
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
    }
}
