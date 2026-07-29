using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-9go: TcXunit-3g7 mapped VAR_TEMP to VarSection.Local so
    // METHOD/ACTION-scoped VAR_TEMP locals reset every call (BindParams
    // rebuilds them fresh in a new Frame per CallMethod). But
    // Engine.NewInstance's IsPersistedField also treated VarSection.Local as
    // persisted whenever it came from a FUNCTION_BLOCK/PROGRAM's own
    // top-level DeclarationText, so a VAR_TEMP declared directly at FB/
    // PROGRAM top level (legal IEC 61131-3, not just inside METHOD/ACTION
    // bodies) went through NewInstance's persisted-field path instead of a
    // fresh-Frame-per-call path and incorrectly persisted across
    // calls/cycles like a real VAR field. Fixed by giving top-level
    // VAR_TEMP its own VarSection.Temp and resetting it to default before
    // every top-level body invocation (Engine.ResetTopLevelTempFields).
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

            // If tTemp incorrectly persisted like a real VAR field (the bug),
            // this second top-level invocation would see it still at 1 from
            // the previous call, increment it to 2, and fResult would read 2
            // instead of a fresh 1.
            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);
            Assert.Equal(1, instance.Fields["fResult"].Value);
        }
    }
}
