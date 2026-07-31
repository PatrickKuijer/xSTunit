using xStunit.Runner.TcUnitStub;

namespace xStunit.Runner.Tests.Fakes
{
    /// <summary>
    /// Declares the same test name twice within one cycle, CurrentCycle never
    /// advancing: a genuine duplicate rather than the cyclic re-declaration of
    /// <see cref="CyclicRedeclarationTestSuite"/>, and must be rejected.
    /// </summary>
    internal sealed class RepeatedTestNameSuite : FB_TestSuite
    {
        protected override void Body()
        {
            TEST("SameName");
            TEST_FINISHED();
            TEST("SameName");
            TEST_FINISHED();
        }
    }
}
