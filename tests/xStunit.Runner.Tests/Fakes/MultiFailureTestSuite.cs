using xStunit.Runner.TcUnitStub;

namespace xStunit.Runner.Tests.Fakes
{
    /// <summary>Two failing asserts inside one TEST() bracket, where upstream keeps only the first failure and ignores the rest.</summary>
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
