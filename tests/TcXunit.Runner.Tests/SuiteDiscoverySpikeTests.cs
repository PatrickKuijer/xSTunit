using System;
using System.Collections.Generic;
using TcXunit.Runner;
using TcXunit.Runner.Tests.Fakes;
using Xunit;

namespace TcXunit.Runner.Tests
{
    /// <summary>
    /// Spike for TcXunit-w5x.7: proves a TcUnit-style suite's TEST()/TEST_FINISHED()
    /// brackets can be run once and surfaced as individual xUnit Theory rows (i.e.
    /// individual VS Test Explorer / CI entries), not one lumped test. Case
    /// identity comes from the TEST() call's string literal, discovered by running
    /// the suite body — see the real FB_AssertTrueFalse.TcPOU shape referenced in
    /// the ticket notes.
    /// </summary>
    public class SuiteDiscoverySpikeTests
    {
        public static IEnumerable<object[]> CounterCases()
        {
            foreach (var result in SuiteRunner.RunAll(new CounterTestSuite()))
                yield return new object[] { new ExecutableCase(result) };
        }

        [Theory]
        [MemberData(nameof(CounterCases))]
        public void TcUnit_case_passes(ExecutableCase executable)
        {
            Assert.True(executable.Result.Passed, executable.Result.ToString());
        }

        [Fact]
        public void Failing_assertion_is_captured_not_thrown()
        {
            var results = SuiteRunner.RunAll(new FlakyTestSuite());

            var result = Assert.Single(results);
            Assert.False(result.Passed);
            Assert.Single(result.Failures);
            Assert.Contains("intentional mismatch", result.Failures[0].Message);
        }

        [Fact]
        public void Multiple_failing_asserts_in_one_test_keeps_only_first_failure()
        {
            var results = SuiteRunner.RunAll(new MultiFailureTestSuite());

            var result = Assert.Single(results);
            Assert.False(result.Passed);
            Assert.Single(result.Failures);
            Assert.Contains("first mismatch", result.Failures[0].Message);
        }

        [Fact]
        public void Repeated_test_name_in_one_pass_is_rejected()
        {
            Assert.Throws<NotSupportedException>(() => SuiteRunner.RunAll(new RepeatedTestNameSuite()));
        }
    }
}
