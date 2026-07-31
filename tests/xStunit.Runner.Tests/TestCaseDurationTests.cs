using xStunit.Runner.Tests.Fakes;
using Xunit;

namespace xStunit.Runner.Tests
{
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
