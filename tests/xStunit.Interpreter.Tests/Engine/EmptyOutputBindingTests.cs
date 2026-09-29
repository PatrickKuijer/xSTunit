using System.Collections.Generic;
using System.Linq;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // IEC 61131-3 lets a formal call list a VAR_OUTPUT with nothing after the
    // '=>' ('fb( x := 1, oOut => );'), naming the output without binding it.
    // A parser that demands an expression there makes the whole calling POU
    // unrunnable, and the fault surfaces at every test that merely reaches the
    // calling method. Each call shape below must run normally with the empty
    // binding in place, and its effects must stay observable the usual way.
    public class EmptyOutputBindingTests
    {
        private static IReadOnlyList<PouAst> Pous(string testBody)
        {
            var doubler = new PouAst(
                "FB_Doubler",
                null,
                "FUNCTION_BLOCK FB_Doubler\nVAR_INPUT\n\tinValue : INT;\n\tinOffset : INT;\nEND_VAR\nVAR_OUTPUT\n\tonResult : INT;\nEND_VAR",
                "onResult := inValue * 2 + inOffset;",
                new List<MethodAst>
                {
                    new MethodAst(
                        "Triple",
                        "METHOD Triple : INT\nVAR_INPUT\n\tinValue : INT;\nEND_VAR\nVAR_OUTPUT\n\tonResult : INT;\nEND_VAR",
                        "onResult := inValue * 3;\nTriple := onResult;"),
                });

            var halve = new PouAst(
                "F_Halve",
                null,
                "FUNCTION F_Halve : INT\nVAR_INPUT\n\tinValue : INT;\nEND_VAR\nVAR_OUTPUT\n\tonRemainder : INT;\nEND_VAR",
                "F_Halve := inValue / 2;\nonRemainder := 1;",
                new List<MethodAst>());

            var testCase = new MethodAst(
                "EmptyBinding",
                "METHOD PRIVATE EmptyBinding",
                $"TEST('EmptyBinding');\n{testBody}\nTEST_FINISHED();");

            var suite = new PouAst(
                "FB_DoublerTests",
                "TcUnit.FB_TestSuite",
                "FUNCTION_BLOCK FB_DoublerTests EXTENDS TcUnit.FB_TestSuite\nVAR\n\tsfbDoubler : FB_Doubler;\n\tsfbCounter : CTU;\n\tsnResult : INT;\nEND_VAR",
                "EmptyBinding();",
                new List<MethodAst> { testCase });

            return new[] { doubler, halve, suite };
        }

        private static void AssertSuitePasses(string testBody)
        {
            var results = new Engine(new TypeRegistry(Pous(testBody))).RunSuite("FB_DoublerTests");

            var result = Assert.Single(results);
            Assert.True(result.Passed, string.Join("; ", result.Failures.Select(f => f.Message)));
        }

        [Fact]
        public void RunSuite_FbInstanceCallWithEmptyOutputLast_RunsAndLeavesOutputReadable()
        {
            AssertSuitePasses(
                "sfbDoubler( inValue := 4, onResult => );\n" +
                "AssertEquals_INT( Expected := 8, Actual := sfbDoubler.onResult, Message := 'output readable after empty => binding' );");
        }

        // The argument after the empty binding must still bind: a parser that
        // swallowed the comma, or stopped the list there, would leave inOffset
        // at 0 and the result at 8.
        [Fact]
        public void RunSuite_FbInstanceCallWithEmptyOutputInMiddle_BindsTheArgumentsAfterIt()
        {
            AssertSuitePasses(
                "sfbDoubler( inValue := 4, onResult => , inOffset := 1 );\n" +
                "AssertEquals_INT( Expected := 9, Actual := sfbDoubler.onResult, Message := 'arguments after the empty => still bind' );");
        }

        [Fact]
        public void RunSuite_MethodCallWithEmptyOutput_RunsAndReturnsItsValue()
        {
            AssertSuitePasses(
                "snResult := sfbDoubler.Triple( inValue := 2, onResult => );\n" +
                "AssertEquals_INT( Expected := 6, Actual := snResult, Message := 'method with an empty => still runs' );");
        }

        [Fact]
        public void RunSuite_FunctionCallWithEmptyOutput_RunsAndReturnsItsValue()
        {
            AssertSuitePasses(
                "snResult := F_Halve( inValue := 8, onRemainder => );\n" +
                "AssertEquals_INT( Expected := 4, Actual := snResult, Message := 'function with an empty => still runs' );");
        }

        // A native standard-library FB binds its arguments on a separate path
        // from an interpreted one, so the empty binding has to be tolerated
        // there too rather than evaluated as if it were an input.
        [Fact]
        public void RunSuite_NativeFbCallWithEmptyOutput_RunsAndLeavesOutputReadable()
        {
            AssertSuitePasses(
                "sfbCounter( CU := TRUE, PV := 5, CV => );\n" +
                "AssertEquals_WORD( Expected := 1, Actual := sfbCounter.CV, Message := 'native FB with an empty => still counts' );");
        }
    }
}
