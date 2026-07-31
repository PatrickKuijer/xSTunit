using System.Collections.Generic;
using xStunit.Runner.TcUnitStub;

namespace xStunit.Runner
{
    /// <summary>
    /// Stands in for what an interpreter-backed runner does once it has an FB
    /// instance: run the body once and collect the TEST()/TEST_FINISHED()
    /// brackets it hits along the way.
    /// </summary>
    public static class SuiteRunner
    {
        public static IReadOnlyList<TestCaseResult> RunAll(FB_TestSuite suite) => suite.Run();
    }
}
