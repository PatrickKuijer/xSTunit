using TcXunit.Runner.TcUnitStub;

namespace TcXunit.Runner.Tests.Fakes
{
    /// <summary>Deliberately failing suite, used to prove AssertEquals_* failures are captured, not thrown.</summary>
    internal sealed class FlakyTestSuite : FB_TestSuite
    {
        public FlakyTestSuite()
        {
            TEST("WrongExpectation", () =>
            {
                AssertEquals_INT(1, 2, "intentional mismatch");
            });
        }
    }
}
