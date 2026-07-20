using System.Collections.Generic;
using TcXunit.Runner;
using TcXunit.Runner.Tests.Fakes;
using Xunit;

namespace TcXunit.Runner.Tests
{
    /// <summary>
    /// Spike for TcXunit-w5x.7: proves a TcUnit-style suite's TEST() cases can be
    /// discovered at collection time and surface as individual xUnit Theory rows
    /// (i.e. individual VS Test Explorer / CI entries), not one lumped test.
    /// </summary>
    public class SuiteDiscoverySpikeTests
    {
        public static IEnumerable<object[]> CounterCases()
        {
            var suite = new CounterTestSuite();
            foreach (var testCase in SuiteRunner.Discover(suite))
                yield return new object[] { new ExecutableCase(suite, testCase) };
        }

        [Theory]
        [MemberData(nameof(CounterCases))]
        public void TcUnit_case_passes(ExecutableCase executable)
        {
            var result = SuiteRunner.Run(executable.Suite, executable.Case);

            Assert.True(result.Passed, result.ToString());
        }

        [Fact]
        public void Failing_assertion_is_captured_not_thrown()
        {
            var suite = new FlakyTestSuite();
            var testCase = SuiteRunner.Discover(suite)[0];

            var result = SuiteRunner.Run(suite, testCase);

            Assert.False(result.Passed);
            Assert.Single(result.Failures);
            Assert.Contains("intentional mismatch", result.Failures[0].Message);
        }
    }
}
