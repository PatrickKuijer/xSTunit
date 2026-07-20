using System.Collections.Generic;
using TcXunit.Runner.TcUnitStub;

namespace TcXunit.Runner
{
    /// <summary>
    /// Runs a suite's body once, collecting the TEST()/TEST_FINISHED() brackets it
    /// hits along the way. Stands in for what a real interpreter-backed runner
    /// would do after instantiating an FB from parsed .TcPOU XML instead of a
    /// hand-written C# stub — see TcXunit-w5x.6/.7.
    /// </summary>
    public static class SuiteRunner
    {
        public static IReadOnlyList<TestCaseResult> RunAll(FB_TestSuite suite) => suite.Run();
    }
}
