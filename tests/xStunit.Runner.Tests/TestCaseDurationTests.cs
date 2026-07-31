using xStunit.Runner.Tests.Fakes;
using Xunit;

namespace xStunit.Runner.Tests
{
    /// <summary>
    /// TcXunit-6fb.1: FB_TestSuite starts a stopwatch on a test's record when
    /// TEST()/TEST_ORDERED() opens it, and FinishRecord (shared by
    /// TEST_FINISHED() and TEST_FINISHED_NAMED()) stops it and stashes the
    /// elapsed time into the TestCaseResult it builds.
    /// </summary>
    public class TestCaseDurationTests
    {
        [Fact]
        public void TestFinished_CapturesElapsedTimeSinceTestOpened()
        {
            var results = SuiteRunner.RunAll(new SleepingTestSuite());

            var result = Assert.Single(results);
            Assert.Equal("SlowTest", result.Name);
            Assert.True(result.Passed, result.ToString());
            Assert.True(
                result.ElapsedMilliseconds >= SleepingTestSuite.SleepMilliseconds,
                $"expected ElapsedMilliseconds >= {SleepingTestSuite.SleepMilliseconds}, got {result.ElapsedMilliseconds}");
        }

        [Fact]
        public void TestFinishedNamed_ViaTestOrdered_CapturesElapsedTime()
        {
            var results = SuiteRunner.RunAll(new SleepingOrderedTestSuite());

            var result = Assert.Single(results);
            Assert.Equal("SlowOrderedTest", result.Name);
            Assert.True(result.Passed, result.ToString());
            Assert.True(
                result.ElapsedMilliseconds >= SleepingOrderedTestSuite.SleepMilliseconds,
                $"expected ElapsedMilliseconds >= {SleepingOrderedTestSuite.SleepMilliseconds}, got {result.ElapsedMilliseconds}");
        }

        [Fact]
        public void TestFinished_QuickTest_ElapsedMillisecondsIsNonNegative()
        {
            var results = SuiteRunner.RunAll(new CounterTestSuite());

            Assert.All(results, r => Assert.True(r.ElapsedMilliseconds >= 0));
        }
    }
}
