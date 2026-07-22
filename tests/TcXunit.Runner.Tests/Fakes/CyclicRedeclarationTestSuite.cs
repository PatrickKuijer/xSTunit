using TcXunit.Runner.TcUnitStub;

namespace TcXunit.Runner.Tests.Fakes
{
    /// <summary>Calls TEST() with the same name twice but advances CurrentCycle between the two calls, simulating upstream's normal cyclic re-declaration (TEST('X') again in a later PLC cycle re-attaches instead of erroring) (TcXunit-k28.5).</summary>
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
