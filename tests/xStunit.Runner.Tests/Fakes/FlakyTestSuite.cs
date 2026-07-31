using xStunit.Runner.TcUnitStub;

namespace xStunit.Runner.Tests.Fakes
{
    /// <summary>Deliberately failing suite, used to prove AssertTrue/AssertEquals_* failures are captured, not thrown.</summary>
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
