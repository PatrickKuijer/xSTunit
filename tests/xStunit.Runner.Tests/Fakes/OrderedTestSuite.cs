using xStunit.Runner.TcUnitStub;

namespace xStunit.Runner.Tests.Fakes
{
    /// <summary>Two TEST_ORDERED() tests run in declared order, guarded by their return value — mirrors upstream's `IF TEST_ORDERED('X') THEN ... TEST_FINISHED(); END_IF` pattern (TcXunit-k28.7).</summary>
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

    /// <summary>TEST_ORDERED('Test_2') is declared before 'Test_1' finishes — its guard must stay FALSE, so no assert runs for it and it never reaches TEST_FINISHED() (TcXunit-k28.7).</summary>
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

    /// <summary>TEST_FINISHED_NAMED() closes a test other than the currently-open one (TcXunit-k28.7).</summary>
    internal sealed class NamedFinishTestSuite : FB_TestSuite
    {
        protected override void Body()
        {
            TEST("A");
            TEST_FINISHED_NAMED("A");
        }
    }

    /// <summary>TEST_FINISHED_NAMED() for a name never declared via TEST()/TEST_ORDERED() must fail fast (TcXunit-k28.7).</summary>
    internal sealed class UnknownNamedFinishTestSuite : FB_TestSuite
    {
        protected override void Body()
        {
            TEST_FINISHED_NAMED("NeverDeclared");
        }
    }

    /// <summary>IS_TEST_FINISHED() for a name never declared via TEST()/TEST_ORDERED() must fail fast (TcXunit-k28.7).</summary>
    internal sealed class UnknownIsFinishedTestSuite : FB_TestSuite
    {
        protected override void Body()
        {
            IS_TEST_FINISHED("NeverDeclared");
        }
    }
}
