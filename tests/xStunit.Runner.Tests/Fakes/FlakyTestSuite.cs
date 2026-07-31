using xStunit.Runner.TcUnitStub;

namespace xStunit.Runner.Tests.Fakes
{
    /// <summary>Fails on purpose, so a failing assert can be shown to be captured on the result rather than thrown out of the run.</summary>
    internal sealed class FlakyTestSuite : FB_TestSuite
    {
        protected override void Body()
        {
            TEST("WrongExpectation");
            AssertEquals_INT(1, 2, "intentional mismatch");
            TEST_FINISHED();
        }
    }
}
