using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A suite receiver must not swallow every unresolved call: routing to the
    // native bridge is gated on CanInvoke, so the global-FUNCTION and
    // native-function lookups behind it stay reachable from inside a suite.
    // Calling from a plain FUNCTION_BLOCK, where there is no suite host at all,
    // exercises none of this.
    public class SuiteGlobalFunctionCallTests
    {
        private static PouAst SuiteCalling(string body)
        {
            var testCase = new MethodAst(
                "UsesGlobalFunction",
                "METHOD PRIVATE UsesGlobalFunction",
                $"TEST('UsesGlobalFunction');\n{body}\nTEST_FINISHED();");

            return new PouAst(
                "FB_WidgetTests",
                "TcUnit.FB_TestSuite",
                "FUNCTION_BLOCK FB_WidgetTests EXTENDS TcUnit.FB_TestSuite\nVAR\n\tnResult : INT;\nEND_VAR",
                "UsesGlobalFunction();",
                new List<MethodAst> { testCase });
        }

        [Fact]
        public void RunSuite_SuiteMethodCallsGlobalFunctionPou_Resolves()
        {
            var function = new PouAst(
                "F_Double",
                null,
                "FUNCTION F_Double : INT\nVAR_INPUT\n\tnIn : INT;\nEND_VAR",
                "F_Double := nIn * 2;",
                new List<MethodAst>());

            var suite = SuiteCalling(
                "AssertEquals_INT(Expected := 42, Actual := F_Double(21), Message := 'global function from suite');");

            var results = new Engine(new TypeRegistry(new[] { suite, function })).RunSuite("FB_WidgetTests");

            var result = Assert.Single(results);
            Assert.True(result.Passed, string.Join("; ", result.Failures.Select(f => f.Message)));
        }

        [Fact]
        public void RunSuite_UnimplementedTcUnitApiFromSuite_StillReportsTheGrowOnDemandMessage()
        {
            // The other side of the CanInvoke gate: letting unrecognized names
            // fall through must not cost a suite the more useful "this TcUnit
            // API isn't wired up yet" diagnostic.
            var suite = SuiteCalling("AssertSomethingNobodyImplemented(Condition := TRUE);");

            // The call sits inside an open TEST() bracket, so the fault fails
            // that test rather than the whole suite, carrying the diagnostic
            // verbatim onto the failure.
            var results = new Engine(new TypeRegistry(new[] { suite })).RunSuite("FB_WidgetTests");

            var failure = Assert.Single(Assert.Single(results).Failures);
            Assert.Contains("AssertSomethingNobodyImplemented", failure.Message);
            Assert.Contains("isn't supported yet", failure.Message);
            // Classified as an interpreter gap, not as a defect in the suite
            // under test - the reader is told not to go edit their POU.
            Assert.Equal(xStunit.Runner.FailureKind.UnsupportedConstruct, failure.Kind);
            Assert.Equal("AssertSomethingNobodyImplemented", failure.Construct);
        }

        // AssertArrayEquals_LWORD is real upstream API that is deliberately not
        // wired up yet, where the name above is fictional. Both must reach the
        // same grow-on-demand diagnostic, or the classification would depend on
        // whether the caller's typo happened to name something real.
        [Fact]
        public void RunSuite_UnwiredUpstreamArrayAssert_StillReportsUnsupportedConstructKind()
        {
            var suite = SuiteCalling("AssertArrayEquals_LWORD(Expecteds := 0, Actuals := 0, Message := '');");

            var results = new Engine(new TypeRegistry(new[] { suite })).RunSuite("FB_WidgetTests");

            var failure = Assert.Single(Assert.Single(results).Failures);
            Assert.Contains("AssertArrayEquals_LWORD", failure.Message);
            Assert.Contains("isn't supported yet", failure.Message);
            Assert.Equal(xStunit.Runner.FailureKind.UnsupportedConstruct, failure.Kind);
            Assert.Equal("AssertArrayEquals_LWORD", failure.Construct);
        }

        // An unqualified call from a suite body that resolves to nothing is not
        // automatically an unwired framework API - far more often it is a typo
        // of one of the suite's own methods. Matching none of the
        // Assert*/TEST*/IS_TEST* prefixes, it is a real and fixable defect in
        // the PLC code, so misclassifying it as an interpreter gap would tell
        // the author to leave the very POU that is broken alone.
        [Fact]
        public void RunSuite_MisspelledUnqualifiedCallFromSuite_ReportsPlcFaultNotUnsupportedConstruct()
        {
            var suite = SuiteCalling("CounterStartsAtZeroo();");

            var results = new Engine(new TypeRegistry(new[] { suite })).RunSuite("FB_WidgetTests");

            var failure = Assert.Single(Assert.Single(results).Failures);
            Assert.Contains("CounterStartsAtZeroo", failure.Message);
            Assert.Equal(xStunit.Runner.FailureKind.PlcFault, failure.Kind);
            Assert.Null(failure.Construct);
        }
    }
}
