using xStunit.Runner.TcUnitStub;

namespace xStunit.Runner.Tests.Fakes
{
    /// <summary>Two ordered tests in the shape upstream ST writes them, each body guarded by the TEST_ORDERED() return value.</summary>
    internal sealed class OrderedTestSuite : FB_TestSuite
    {
        protected override void Body()
        {
            if (TEST_ORDERED("Test_1"))
            {
                AssertEquals_INT(1, 1, "ok");
                TEST_FINISHED();
            }

            if (TEST_ORDERED("Test_2"))
            {
                AssertEquals_INT(2, 2, "ok");
                TEST_FINISHED();
            }
        }
    }

    /// <summary>Reaches Test_2 while Test_1 is still open: the out-of-turn guard must stay FALSE, so Test_2 runs nothing and never becomes a result.</summary>
    internal sealed class OutOfTurnOrderedTestSuite : FB_TestSuite
    {
        protected override void Body()
        {
            TEST_ORDERED("Test_1");

            if (TEST_ORDERED("Test_2"))
                TEST_FINISHED();

            AssertEquals_INT(1, 1, "ok");
            TEST_FINISHED();
        }
    }

    internal sealed class NamedFinishTestSuite : FB_TestSuite
    {
        protected override void Body()
        {
            TEST("A");
            TEST_FINISHED_NAMED("A");
        }
    }

    internal sealed class UnknownNamedFinishTestSuite : FB_TestSuite
    {
        protected override void Body()
        {
            TEST_FINISHED_NAMED("NeverDeclared");
        }
    }

    internal sealed class UnknownIsFinishedTestSuite : FB_TestSuite
    {
        protected override void Body()
        {
            IS_TEST_FINISHED("NeverDeclared");
        }
    }
}
