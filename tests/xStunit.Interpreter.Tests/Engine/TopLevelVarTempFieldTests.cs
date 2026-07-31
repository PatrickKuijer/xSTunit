using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // VAR_TEMP is legal directly at FUNCTION_BLOCK/PROGRAM top level, not only
    // inside METHOD/ACTION bodies. It lives in the instance's field table
    // alongside VAR, which makes the two easy to conflate, but its whole
    // meaning is the opposite: it resets to its default before every top-level
    // body invocation rather than carrying state across cycles.
    public class TopLevelVarTempFieldTests
    {
        private static Engine NewFixtureEngine()
        {
            var method = new MethodAst(
                "M_WriteTemp",
                "METHOD PRIVATE M_WriteTemp",
                "tTemp := tTemp + 1;\nfResult := tTemp;");

            var pou = new PouAst(
                "FB_TopLevelVarTempFixture",
                null,
                "VAR_TEMP\n\ttTemp : UINT;\nEND_VAR\nVAR\n\tfResult : UINT;\nEND_VAR",
                "M_WriteTemp();",
                new List<MethodAst> { method });

            return new Engine(new TypeRegistry(new[] { pou }));
        }

        [Fact]
        public void TopLevelVarTemp_IsDeclaredAndWritableFromAMethod()
        {
            var engine = NewFixtureEngine();
            var instance = engine.NewInstance("FB_TopLevelVarTempFixture");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            Assert.Equal(1, instance.Fields["fResult"].Value);
        }

        [Fact]
        public void TopLevelVarTemp_ResetsToDefaultOnEveryTopLevelInvocation()
        {
            var engine = NewFixtureEngine();
            var instance = engine.NewInstance("FB_TopLevelVarTempFixture");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);
            Assert.Equal(1, instance.Fields["fResult"].Value);

            // Were tTemp persisting like a VAR field, this second invocation
            // would start from 1 and leave fResult at 2.
            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);
            Assert.Equal(1, instance.Fields["fResult"].Value);
        }
    }
}
