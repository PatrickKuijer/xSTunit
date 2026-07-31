using xStunit.Runner.TcUnitStub;

namespace xStunit.Runner.Tests.Fakes
{
    /// <summary>Calls TEST() with the same name twice within the same cycle (CurrentCycle never advances) — a genuine duplicate test name, not cyclic re-declaration, so it's rejected (TcXunit-k28.5).</summary>
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
