using TcXunit.Runner.TcUnitStub;

namespace TcXunit.Runner.Tests.Fakes
{
    /// <summary>Calls TEST() with the same name twice in one pass — real TcUnit relies on PLC-cyclic re-entry to make this valid; v1 doesn't support that, so it should be rejected rather than silently misreported.</summary>
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
