using System.Threading;
using TcXunit.Runner.TcUnitStub;

namespace TcXunit.Runner.Tests.Fakes
{
    /// <summary>
    /// Sleeps a known amount inside the TEST()/TEST_FINISHED() bracket so a test
    /// can assert the resulting TestCaseResult.ElapsedMilliseconds reflects real
    /// wall-clock time spent between TEST() opening the record and FinishRecord
    /// stopping it (TcXunit-6fb.1).
    /// </summary>
    internal sealed class SleepingTestSuite : FB_TestSuite
    {
        public const int SleepMilliseconds = 30;

        protected override void Body()
        {
            TEST("SlowTest");
            Thread.Sleep(SleepMilliseconds);
            AssertTrue(true, "always true");
            TEST_FINISHED();
        }
    }

    /// <summary>
    /// TEST_ORDERED()/TEST_FINISHED_NAMED() counterpart of SleepingTestSuite, so
    /// the stopwatch-capture coverage isn't limited to the plain TEST()/
    /// TEST_FINISHED() path (TcXunit-6fb.1 acceptance criteria explicitly calls
    /// out both TEST_FINISHED() and TEST_FINISHED_NAMED()).
    /// </summary>
    internal sealed class SleepingOrderedTestSuite : FB_TestSuite
    {
        public const int SleepMilliseconds = 30;

        protected override void Body()
        {
            if (TEST_ORDERED("SlowOrderedTest"))
            {
                Thread.Sleep(SleepMilliseconds);
                AssertTrue(true, "always true");
                TEST_FINISHED_NAMED("SlowOrderedTest");
            }
        }
    }
}
