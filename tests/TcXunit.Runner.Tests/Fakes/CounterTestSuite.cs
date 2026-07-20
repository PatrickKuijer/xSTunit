using TcXunit.Runner.TcUnitStub;

namespace TcXunit.Runner.Tests.Fakes
{
    /// <summary>
    /// Fake equivalent of a TcUnit suite POU (FB extending FB_TestSuite). A real
    /// suite would be interpreted from .TcPOU XML; this one is hand-written to
    /// spike the discovery/run/report wiring described in TcXunit-w5x.7.
    /// </summary>
    internal sealed class CounterTestSuite : FB_TestSuite
    {
        public CounterTestSuite()
        {
            TEST("CounterStartsAtZero", () =>
            {
                var counter = new Counter();
                AssertEquals_INT(0, counter.Value, "fresh counter");
            });

            TEST("IncrementAddsOne", () =>
            {
                var counter = new Counter();
                counter.Increment();
                AssertEquals_INT(1, counter.Value, "after Increment");
            });

            TEST("DecrementClampsAtZero", () =>
            {
                var counter = new Counter();
                counter.Decrement();
                AssertEquals_INT(0, counter.Value, "Decrement below zero");
            });
        }
    }
}
