using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    public class RunSuiteDurationTests
    {
        private static Engine NewSuiteEngine(string implementationText)
        {
            var suite = new PouAst("FB_MySuite", "TcUnit.FB_TestSuite", "", implementationText, new List<MethodAst>());
            return new Engine(new TypeRegistry(new[] { suite }));
        }

        [Fact]
        public void RunSuite_OutElapsedMilliseconds_IsNonNegative()
        {
            var engine = NewSuiteEngine(
                "TEST('QuickTest');\n" +
                "AssertTrue(Condition := TRUE, Message := 'ok');\n" +
                "TEST_FINISHED();");

            var results = engine.RunSuite("FB_MySuite", out var elapsedMilliseconds);

            Assert.Single(results);
            Assert.True(results[0].Passed, results[0].ToString());
            Assert.True(elapsedMilliseconds >= 0, $"expected elapsedMilliseconds >= 0, got {elapsedMilliseconds}");
        }

        [Fact]
        public void RunSuite_OutElapsedMilliseconds_CapturedEvenWhenATestFails()
        {
            // A failed assertion does not stop the suite body from running to
            // completion, so a duration is still owed. It is absent only where
            // the suite never ran at all.
            var engine = NewSuiteEngine(
                "TEST('FailingTest');\n" +
                "AssertTrue(Condition := FALSE, Message := 'deliberately false');\n" +
                "TEST_FINISHED();");

            var results = engine.RunSuite("FB_MySuite", out var elapsedMilliseconds);

            Assert.Single(results);
            Assert.False(results[0].Passed);
            Assert.True(elapsedMilliseconds >= 0, $"expected elapsedMilliseconds >= 0, got {elapsedMilliseconds}");
        }

        [Fact]
        public void RunSuite_ParameterlessOverload_StillReturnsSameResultsAsOutParamOverload()
        {
            var implementation =
                "TEST('QuickTest');\n" +
                "AssertTrue(Condition := TRUE, Message := 'ok');\n" +
                "TEST_FINISHED();";

            var withoutDuration = NewSuiteEngine(implementation).RunSuite("FB_MySuite");
            var withDuration = NewSuiteEngine(implementation).RunSuite("FB_MySuite", out _);

            Assert.Single(withoutDuration);
            Assert.Single(withDuration);
            Assert.Equal(withoutDuration[0].Name, withDuration[0].Name);
            Assert.Equal(withoutDuration[0].Passed, withDuration[0].Passed);
        }
    }
}
