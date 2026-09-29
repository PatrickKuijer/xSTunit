using System.Collections.Generic;
using System.Linq;
using xStunit.Parser;
using xStunit.Runner.TcUnitStub;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A direct FB-instance call's 'out => target' must copy the output into
    // the target once the FB body has run, exactly as a METHOD or FUNCTION
    // call does. Without it the target silently keeps its old value, so code
    // under test that reads FB outputs this way sees stale data and a suite
    // can pass or fail for the wrong reason.
    public class FbInstanceOutputBindingTests
    {
        private static IReadOnlyList<PouAst> Pous(string testLocals, string testBody)
        {
            var doubler = new PouAst(
                "FB_Doubler",
                null,
                "FUNCTION_BLOCK FB_Doubler\nVAR_INPUT\n\tinValue : INT;\nEND_VAR\nVAR_OUTPUT\n\tonResult : INT;\n\torHalf : REAL;\nEND_VAR",
                "onResult := inValue * 2;\norHalf := 0.5;",
                new List<MethodAst>
                {
                    new MethodAst(
                        "Triple",
                        "METHOD Triple\nVAR_INPUT\n\tinValue : INT;\nEND_VAR\nVAR_OUTPUT\n\tonResult : INT;\n\torHalf : REAL;\nEND_VAR",
                        "onResult := inValue * 3;\norHalf := 0.5;"),
                });

            var testCase = new MethodAst(
                "OutputBinding",
                testLocals == null
                    ? "METHOD PRIVATE OutputBinding"
                    : $"METHOD PRIVATE OutputBinding\nVAR\n{testLocals}\nEND_VAR",
                $"TEST('OutputBinding');\n{testBody}\nTEST_FINISHED();");

            var suite = new PouAst(
                "FB_DoublerTests",
                "TcUnit.FB_TestSuite",
                "FUNCTION_BLOCK FB_DoublerTests EXTENDS TcUnit.FB_TestSuite\nVAR\n\tsfbDoubler : FB_Doubler;\n\tsfbCounter : CTU;\n\tsnBound : INT;\n\tsnCount : WORD;\nEND_VAR",
                "OutputBinding();",
                new List<MethodAst> { testCase });

            return new[] { doubler, suite };
        }

        private static TestCaseResult RunSingle(string testLocals, string testBody) =>
            Assert.Single(new Engine(new TypeRegistry(Pous(testLocals, testBody))).RunSuite("FB_DoublerTests"));

        private static void AssertSuitePasses(string testLocals, string testBody)
        {
            var result = RunSingle(testLocals, testBody);
            Assert.True(result.Passed, string.Join("; ", result.Failures.Select(f => f.Message)));
        }

        [Fact]
        public void RunSuite_FbInstanceOutputBoundToSuiteMember_WritesTheOutputBack()
        {
            AssertSuitePasses(
                null,
                "sfbDoubler( inValue := 3, onResult => snBound );\n" +
                "AssertEquals_INT( Expected := 6, Actual := snBound, Message := 'suite member receives the output' );");
        }

        [Fact]
        public void RunSuite_FbInstanceOutputBoundToMethodLocal_WritesTheOutputBack()
        {
            AssertSuitePasses(
                "\tnLocal : INT;",
                "sfbDoubler( inValue := 5, onResult => nLocal );\n" +
                "AssertEquals_INT( Expected := 10, Actual := nLocal, Message := 'method local receives the output' );");
        }

        // A call passing only outputs binds no input at all, so the FB runs on
        // the input assigned beforehand; the write-back must not depend on the
        // call also carrying an input argument.
        [Fact]
        public void RunSuite_FbInstanceCallPassingOnlyOutputs_WritesTheOutputBack()
        {
            AssertSuitePasses(
                "\tnLocal : INT;",
                "sfbDoubler.inValue := 7;\n" +
                "sfbDoubler( onResult => nLocal );\n" +
                "AssertEquals_INT( Expected := 14, Actual := nLocal, Message := 'output-only call writes back' );");
        }

        [Fact]
        public void RunSuite_MethodOutputBoundToMethodLocal_StillWritesTheOutputBack()
        {
            AssertSuitePasses(
                "\tnLocal : INT;",
                "sfbDoubler.Triple( inValue := 2, onResult => nLocal );\n" +
                "AssertEquals_INT( Expected := 6, Actual := nLocal, Message := 'method output write-back unchanged' );");
        }

        // The FB write-back must apply the same assignment rules as the
        // METHOD one: an INT output widens into an LREAL target ...
        [Fact]
        public void RunSuite_FbInstanceIntOutputIntoLrealTarget_Widens()
        {
            AssertSuitePasses(
                "\tfLocal : LREAL;",
                "sfbDoubler( inValue := 3, onResult => fLocal );\n" +
                "AssertEquals_LREAL( Expected := 6.0, Actual := fLocal, Delta := 0.0, Message := 'INT output widens into LREAL' );");
        }

        // ... and a REAL output into an INT target is refused as an implicit
        // narrowing, with the same diagnostic either call shape produces.
        [Theory]
        [InlineData("sfbDoubler( inValue := 1, orHalf => nLocal );")]
        [InlineData("sfbDoubler.Triple( inValue := 1, orHalf => nLocal );")]
        public void RunSuite_RealOutputIntoIntTarget_IsRejectedAsNarrowing(string call)
        {
            var result = RunSingle("\tnLocal : INT;", call);

            var failure = Assert.Single(result.Failures);
            Assert.Contains("Implicit narrowing from REAL to INT", failure.Message);
        }

        // A native standard-library FB binds on its own path. There an output
        // binding must not be read as an input: evaluating 'CV => snCount' into
        // CV would reset the count to the target's value before every update,
        // so the counter could never get past 1.
        [Fact]
        public void RunSuite_NativeFbOutputBinding_WritesBackWithoutClobberingTheOutput()
        {
            AssertSuitePasses(
                null,
                "sfbCounter( CU := TRUE, PV := 5, CV => snCount );\n" +
                "sfbCounter( CU := FALSE, PV := 5, CV => snCount );\n" +
                "sfbCounter( CU := TRUE, PV := 5, CV => snCount );\n" +
                "AssertEquals_WORD( Expected := 2, Actual := sfbCounter.CV, Message := 'counter counts both edges' );\n" +
                "AssertEquals_WORD( Expected := 2, Actual := snCount, Message := 'target receives CV' );");
        }
    }
}
