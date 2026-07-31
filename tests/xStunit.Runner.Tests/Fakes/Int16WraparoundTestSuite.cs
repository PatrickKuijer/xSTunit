using xStunit.Runner.TcUnitStub;

namespace xStunit.Runner.Tests.Fakes
{
    /// <summary>32768 and -32768 collide as signed 16-bit INT, matching what a real TwinCAT INT variable would already have wrapped to (TcXunit-k28.4).</summary>
    internal sealed class Int16WraparoundTestSuite : FB_TestSuite
    {
        protected override void Body()
        {
            TEST("WrapsLikeRealInt");
            AssertEquals_INT(32768, -32768, "wraparound collision");
            TEST_FINISHED();
        }
    }
}
