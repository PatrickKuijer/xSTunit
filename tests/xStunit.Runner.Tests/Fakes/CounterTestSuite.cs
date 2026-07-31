using xStunit.Runner.TcUnitStub;

namespace xStunit.Runner.Tests.Fakes
{
    /// <summary>The all-passing baseline suite, against which the failure fixtures here are the deviations.</summary>
    internal sealed class CounterTestSuite : FB_TestSuite
    {
        protected override void Body()
        {
            CounterStartsAtZero();
            IncrementAddsOne();
            DecrementClampsAtZero();
        }

        private void CounterStartsAtZero()
        {
            TEST("CounterStartsAtZero");
            var counter = new Counter();
            AssertEquals_INT(0, counter.Value, "fresh counter");
            TEST_FINISHED();
        }

        private void IncrementAddsOne()
        {
            TEST("IncrementAddsOne");
            var counter = new Counter();
            counter.Increment();
            AssertEquals_INT(1, counter.Value, "after Increment");
            TEST_FINISHED();
        }

        private void DecrementClampsAtZero()
        {
            TEST("DecrementClampsAtZero");
            var counter = new Counter();
            counter.Decrement();
            AssertEquals_INT(0, counter.Value, "Decrement below zero");
            TEST_FINISHED();
        }
    }
}
