using xStunit.Runner.TcUnitStub;

namespace xStunit.Runner.Tests.Fakes
{
    /// <summary>
    /// Fake equivalent of a TcUnit suite POU (FB extending FB_TestSuite). Mirrors
    /// the real call shape seen in TcUnit-Verifier's FB_AssertTrueFalse.TcPOU:
    /// body calls case-methods unconditionally, each brackets TEST()/assert/
    /// TEST_FINISHED(). A real suite would be interpreted from .TcPOU XML; this
    /// one is hand-written to spike the run/report wiring (TcXunit-w5x.7).
    /// </summary>
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
