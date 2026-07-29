using System;
using System.Collections.Generic;
using System.Linq;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-6k2: a TcUnit suite could not call a global FUNCTION POU at all.
    //
    // Engine routed every unresolved call from a suite receiver to
    // NativeMethodBridge, whose default branch throws "isn't supported yet" -
    // so the global-FUNCTION fallback (TcXunit-9su) and the native-function
    // lookup after it were both unreachable from inside a suite. The
    // pre-existing global-function tests all called from a plain
    // FUNCTION_BLOCK, whose NativeSuiteHost is null, which is why nothing
    // caught it. Fixed by gating that routing on
    // NativeMethodBridge.CanInvoke.
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

            var ex = Assert.ThrowsAny<Exception>(() =>
                new Engine(new TypeRegistry(new[] { suite })).RunSuite("FB_WidgetTests"));

            Assert.Contains("AssertSomethingNobodyImplemented", ex.Message);
            Assert.Contains("isn't supported yet", ex.Message);
        }
    }
}
