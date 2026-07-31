using System.Threading;
using xStunit.Runner.TcUnitStub;

namespace xStunit.Runner.Tests.Fakes
{
    /// <summary>Burns a known amount of wall-clock time inside the bracket, giving the reported duration a lower bound to be measured against.</summary>
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

    /// <summary>The same lower bound on the other closing path, since TEST_FINISHED_NAMED() has to stop the same clock TEST_FINISHED() does.</summary>
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
