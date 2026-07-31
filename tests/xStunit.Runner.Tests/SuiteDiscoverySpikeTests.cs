using System;
using System.Collections.Generic;
using xStunit.Runner;
using xStunit.Runner.Tests.Fakes;
using Xunit;

namespace xStunit.Runner.Tests
{
    /// <summary>
    /// A suite runs once, and each TEST()/TEST_FINISHED() bracket it executed
    /// surfaces as its own xUnit row rather than one lumped test — case identity
    /// comes from the TEST() call's string literal, discovered by running the body.
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
            Assert.Equal("FAILED TEST 'WrongExpectation', EXP: 1, ACT: 2, MSG: intentional mismatch", result.Failures[0].Message);
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
        public void AssertEqualsInt_ValuesThatCollideAsSigned16Bit_ComparesEqual()
        {
            var results = SuiteRunner.RunAll(new Int16WraparoundTestSuite());

            var result = Assert.Single(results);
            Assert.True(result.Passed, result.ToString());
        }

        [Fact]
        public void AssertTrueFalse_Failure_ReportsExpectedActualLikeUpstream()
        {
            var results = SuiteRunner.RunAll(new AssertTrueFalseFormatTestSuite());

            Assert.Equal(2, results.Count);
            Assert.Equal(
                "FAILED TEST 'TrueCheck', EXP: TRUE, ACT: FALSE, MSG: must be true",
                results[0].Failures[0].Message);
            Assert.Equal(
                "FAILED TEST 'FalseCheck', EXP: FALSE, ACT: TRUE",
                results[1].Failures[0].Message);
        }

        [Fact]
        public void Repeated_test_name_in_one_pass_is_rejected()
        {
            Assert.Throws<NotSupportedException>(() => SuiteRunner.RunAll(new RepeatedTestNameSuite()));
        }

        [Fact]
        public void Repeated_test_name_in_a_later_cycle_re_attaches_instead_of_throwing()
        {
            var results = SuiteRunner.RunAll(new CyclicRedeclarationTestSuite());

            var result = Assert.Single(results);
            Assert.Equal("SameName", result.Name);
            Assert.True(result.Passed, result.ToString());
        }
    }
}
