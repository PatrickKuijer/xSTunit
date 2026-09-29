using System.Collections.Generic;
using System.Linq;
using xStunit.Parser;
using xStunit.Runner.TcUnitStub;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    public class StructMemberFbCallTests
    {
        private static readonly PouAst Counter = new PouAst(
            "FB_Counter",
            null,
            "FUNCTION_BLOCK FB_Counter\nVAR_INPUT\n\tnStep : INT;\nEND_VAR\nVAR_OUTPUT\n\tnTotal : INT;\nEND_VAR",
            "nTotal := nTotal + nStep;",
            new List<MethodAst>
            {
                new MethodAst("Reset", "METHOD Reset", "nTotal := 0;"),
            });

        private static readonly PouAst Bumper = new PouAst(
            "FB_Bumper",
            null,
            "FUNCTION_BLOCK FB_Bumper\nVAR_IN_OUT\n\tnRef : INT;\nEND_VAR",
            "nRef := nRef + 10;",
            new List<MethodAst>());

        private static readonly StructAst Holder = new StructAst(
            "ST_Holder",
            new[]
            {
                new VarDecl("fbX", "FB_Counter", null, VarSection.Local),
                new VarDecl("nPlain", "INT", null, VarSection.Local),
                new VarDecl("fbIo", "FB_Bumper", null, VarSection.Local),
                new VarDecl("tonX", "TON", null, VarSection.Local),
                new VarDecl("ctuX", "CTU", null, VarSection.Local),
                new VarDecl("fbGone", "Loopback", null, VarSection.Local),
            });

        private static readonly GvlAst Globals = new GvlAst(
            "GVL_Holders", "VAR_GLOBAL\n\tstGlobal : ST_Holder;\nEND_VAR");

        private static TestCaseResult Run(string testBody, string extraLocals = "")
        {
            var testCase = new MethodAst(
                "M_Case",
                "METHOD PRIVATE Case\nVAR\n\tnOut : INT;\n" + extraLocals + "\nEND_VAR",
                "TEST('M_Case');\n" + testBody + "\nTEST_FINISHED();");
            var suite = new PouAst(
                "FB_HolderTests",
                "TcUnit.FB_TestSuite",
                "FUNCTION_BLOCK FB_HolderTests EXTENDS TcUnit.FB_TestSuite\nVAR\n\tstA : ST_Holder;\n\taSt : ARRAY[0..1] OF ST_Holder;\n\tnCalls : INT;\nEND_VAR",
                "M_Case();",
                new List<MethodAst>
                {
                    testCase,
                    new MethodAst("M_Next", "METHOD PRIVATE M_Next : INT", "nCalls := nCalls + 1;\nM_Next := nCalls - 1;"),
                });
            var registry = new TypeRegistry(new[] { Counter, Bumper, suite }, new[] { Holder }, gvls: new[] { Globals });
            return Assert.Single(new Engine(registry).RunSuite("FB_HolderTests"));
        }

        private static void AssertPasses(string testBody, string extraLocals = "")
        {
            var result = Run(testBody, extraLocals);
            Assert.True(result.Passed, string.Join("; ", result.Failures.Select(f => f.Message)));
        }

        // Calling an FB held in a struct member must run that member's own
        // body, so its persisted state and outputs are visible through the
        // struct afterwards.
        [Fact]
        public void Call_OnStructMemberFb_RunsBodyAgainstMemberInstance()
        {
            AssertPasses(
                "stA.fbX.nStep := 2;\nstA.fbX();\nstA.fbX();\n" +
                "AssertEquals_INT( Expected := 4, Actual := stA.fbX.nTotal, Message := 'state persists across calls' );");
        }

        [Fact]
        public void Call_OnStructMemberFb_BindsInputsAndOutputs()
        {
            AssertPasses(
                "stA.fbX( nStep := 5, nTotal => nOut );\n" +
                "AssertEquals_INT( Expected := 5, Actual := nOut, Message := 'output bound' );\n" +
                "AssertEquals_INT( Expected := 5, Actual := stA.fbX.nTotal, Message := 'member output' );");
        }

        [Fact]
        public void Call_OnStructMemberFb_BindsPositionalInput()
        {
            AssertPasses(
                "stA.fbX( 3 );\n" +
                "AssertEquals_INT( Expected := 3, Actual := stA.fbX.nTotal, Message := 'positional' );");
        }

        [Fact]
        public void MethodCall_OnStructMemberFb_MutatesMemberState()
        {
            AssertPasses(
                "stA.fbX( nStep := 4 );\nstA.fbX.Reset();\n" +
                "AssertEquals_INT( Expected := 0, Actual := stA.fbX.nTotal, Message := 'reset' );");
        }

        [Fact]
        public void Call_OnArrayElementStructMemberFb_TouchesOnlyThatElement()
        {
            AssertPasses(
                "aSt[1].fbX( nStep := 7 );\n" +
                "AssertEquals_INT( Expected := 7, Actual := aSt[1].fbX.nTotal, Message := 'element 1' );\n" +
                "AssertEquals_INT( Expected := 0, Actual := aSt[0].fbX.nTotal, Message := 'element 0' );");
        }

        [Fact]
        public void Call_OnThisQualifiedStructMemberFb_RunsBody()
        {
            AssertPasses(
                "THIS^.stA.fbX( nStep := 6 );\n" +
                "AssertEquals_INT( Expected := 6, Actual := stA.fbX.nTotal, Message := 'this' );");
        }

        [Fact]
        public void Call_OnGvlStructMemberFb_RunsBody()
        {
            AssertPasses(
                "GVL_Holders.stGlobal.fbX( nStep := 8 );\n" +
                "AssertEquals_INT( Expected := 8, Actual := GVL_Holders.stGlobal.fbX.nTotal, Message := 'gvl' );");
        }

        // The receiver expression of a member call is evaluated exactly once,
        // or an index with a side effect would advance twice.
        [Fact]
        public void Call_OnStructMemberFb_EvaluatesReceiverOnce()
        {
            AssertPasses(
                "nOut := 0;\naSt[M_Next()].fbX( nStep := 1 );\n" +
                "AssertEquals_INT( Expected := 1, Actual := nCalls, Message := 'index evaluated once' );\n" +
                "AssertEquals_INT( Expected := 1, Actual := aSt[0].fbX.nTotal, Message := 'called element' );");
        }

        // A VAR_IN_OUT of a struct-member FB must alias the caller's variable,
        // as it does for a top-level instance, or writes never reach the caller.
        [Fact]
        public void Call_OnStructMemberFbWithInOut_WritesThroughToCallerVariable()
        {
            AssertPasses(
                "nOut := 1;\nstA.fbIo( nRef := nOut );\n" +
                "AssertEquals_INT( Expected := 11, Actual := nOut, Message := 'in-out aliased' );");
        }

        // Native stub FBs held in a struct must dispatch through their host and
        // write back '=>' outputs like top-level instances.
        [Fact]
        public void Call_OnStructMemberCounter_WritesBackOutput()
        {
            AssertPasses(
                "stA.ctuX( CU := TRUE, PV := 1, Q => bDone );\n" +
                "AssertTrue( Condition := bDone, Message := 'counter output' );",
                "\tbDone : BOOL;");
        }

        [Fact]
        public void Call_OnStructMemberTimer_WritesBackOutput()
        {
            AssertPasses(
                "stA.tonX( IN := TRUE, PT := T#50MS );\nAdvanceClock( T#100MS );\n" +
                "stA.tonX( IN := TRUE, PT := T#50MS, Q => bDone );\n" +
                "AssertTrue( Condition := bDone, Message := 'timer output' );",
                "\tbDone : BOOL;");
        }

        // An FB member whose type has no directly invocable body must
        // say which member and type, not claim the member is not an FB.
        [Fact]
        public void Call_OnStructMemberFbWithoutCallableBody_NamesMemberAndType()
        {
            var result = Run("stA.fbGone();");

            Assert.False(result.Passed);
            var message = string.Join("; ", result.Failures.Select(f => f.Message));
            Assert.Contains("fbGone", message);
            Assert.Contains("Loopback", message);
            Assert.Contains("plugin", message);
        }

        [Fact]
        public void Call_OnNonFbStructMember_FailsNamingTheMember()
        {
            var result = Run("stA.nPlain();");

            Assert.False(result.Passed);
            var message = string.Join("; ", result.Failures.Select(f => f.Message));
            Assert.Contains("nPlain", message);
            Assert.DoesNotContain("InvalidCast", message);
        }
    }
}
