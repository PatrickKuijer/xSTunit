using TcXunit.Runner.TcUnitStub;

namespace TcXunit.Runner.Tests.Fakes
{
    /// <summary>Two failing asserts in one TEST() bracket, used to prove only the first failure's message/type is kept (TcXunit-k28.1 - matches upstream FB_Test's 'set if not already set' semantics).</summary>
    internal sealed class MultiFailureTestSuite : FB_TestSuite
    {
        protected override void Body()
        {
            TEST("MultipleFailures");
            AssertEquals_INT(1, 2, "first mismatch");
            AssertEquals_INT(3, 4, "second mismatch");
            TEST_FINISHED();
        }
    }
}
