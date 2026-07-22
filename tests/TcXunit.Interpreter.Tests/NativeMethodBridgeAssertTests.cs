using System.Collections.Generic;
using System.Linq;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-k28.6: AssertFalse/AssertEquals_BOOL/AssertEquals_STRING/
    // AssertEquals_REAL were already implemented on FB_TestSuite but had no
    // NativeMethodBridge case or TcUnitSuiteHost wrapper, so an interpreted
    // suite calling them threw "not supported" - wires them up.
    public class NativeMethodBridgeAssertTests
    {
        private static Engine NewSuiteEngine(string implementationText)
        {
            var suite = new PouAst("FB_MySuite", "TcUnit.FB_TestSuite", "", implementationText, new List<MethodAst>());
            return new Engine(new TypeRegistry(new[] { suite }));
        }

        [Fact]
        public void RunSuite_AssertFalse_ReachableThroughInterpreter()
        {
            // No TRUE/FALSE literal keywords in the interpreter's expression
            // grammar yet (separate, pre-existing gap) - a comparison
            // expression yields the same BOOL value.
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertFalse(Condition := (1 = 2), Message := 'ok');\n" +
                "TEST_FINISHED();");

            var results = engine.RunSuite("FB_MySuite");

            Assert.True(Assert.Single(results).Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsBool_ReachableAndReportsFailure()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_BOOL(Expected := (1 = 1), Actual := (1 = 2), Message := 'mismatch');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            Assert.Contains("EXP: TRUE, ACT: FALSE", result.Failures[0].Message);
        }

        [Fact]
        public void RunSuite_AssertEqualsString_ReachableThroughInterpreter()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_STRING(Expected := 'abc', Actual := 'abc', Message := 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsReal_ReachableAndRespectsDelta()
        {
            var engine = NewSuiteEngine(
                "TEST('t');\n" +
                "AssertEquals_REAL(Expected := 1.0, Actual := 1.05, Delta := 0.1, Message := 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }
    }
}
