using xStunit.Runner.TcUnitStub;

namespace xStunit.Runner.Tests.Fakes
{
    /// <summary>Asserts 32768 against -32768: the two collide as signed 16-bit INT, which is the value a real PLC INT variable would already hold.</summary>
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
