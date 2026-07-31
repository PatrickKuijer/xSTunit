using xStunit.Runner.TcUnitStub;

namespace xStunit.Runner.Tests.Fakes
{
    /// <summary>Failing AssertTrue/AssertFalse, used to prove they carry EXP/ACT detail via AssertEquals_BOOL like upstream (TcXunit-k28.2/.3).</summary>
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
