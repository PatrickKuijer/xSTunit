using xStunit.Runner.TcUnitStub;

namespace xStunit.Runner.Tests.Fakes
{
    /// <summary>Provokes an AssertTrue and an AssertFalse failure, so their messages can be held to upstream's EXP/ACT shape.</summary>
    internal sealed class AssertTrueFalseFormatTestSuite : FB_TestSuite
    {
        protected override void Body()
        {
            TEST("TrueCheck");
            AssertTrue(false, "must be true");
            TEST_FINISHED();

            TEST("FalseCheck");
            AssertFalse(true, "");
            TEST_FINISHED();
        }
    }
}
