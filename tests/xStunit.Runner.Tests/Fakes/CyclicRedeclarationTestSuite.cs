using xStunit.Runner.TcUnitStub;

namespace xStunit.Runner.Tests.Fakes
{
    /// <summary>
    /// Declares the same test name twice, but advances CurrentCycle in between:
    /// that is the ordinary cyclic re-declaration a PLC scan produces, and must
    /// re-attach to the existing record rather than be treated as a duplicate.
    /// </summary>
    internal sealed class CyclicRedeclarationTestSuite : FB_TestSuite
    {
        protected override void Body()
        {
            TEST("SameName");
            TEST_FINISHED();

            CurrentCycle++;

            TEST("SameName");
            TEST_FINISHED();
        }
    }
}
