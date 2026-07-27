using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-6fb.2: suite-level counterpart to TcXunit-6fb.1's per-test
    // ElapsedMilliseconds - Engine.RunSuite(string, out long) wraps its
    // instantiate/execute call with a Stopwatch so CliRunner's JSON output can
    // report suites[].durationMs.
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
            // The suite itself still ran to completion (Body() returned normally) -
            // only the individual TEST() assertion failed - so a duration should
            // still be reported, same as CliRunner's suite-load-failure path only
            // omits it when the suite never ran at all.
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
            // The pre-existing RunSuite(string) overload (relied on by
            // SuiteCaseRunner.cs and dozens of other Engine tests) must keep
            // behaving identically now that it just forwards to the new
            // out-param overload and discards the elapsed time.
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
