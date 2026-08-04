using xStunit.Runner.Tests.Fakes;
using Xunit;

namespace xStunit.Runner.Tests
{
    public class TestCaseDurationTests
    {
        [Fact]
        public void TestFinished_CapturesElapsedTimeSinceTestOpened()
        {
            var results = new SleepingTestSuite().Run();

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
            var results = new SleepingOrderedTestSuite().Run();

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
            var results = new CounterTestSuite().Run();

            Assert.All(results, r => Assert.True(r.ElapsedMilliseconds >= 0));
        }
    }
}
