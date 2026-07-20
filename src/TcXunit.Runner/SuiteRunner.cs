using System.Collections.Generic;
using System.Linq;
using TcXunit.Runner.TcUnitStub;

namespace TcXunit.Runner
{
    /// <summary>
    /// Discovers TEST() cases on a suite and runs them. Stands in for what a real
    /// interpreter-backed runner would do after instantiating an FB from parsed
    /// .TcPOU XML instead of a hand-written C# stub — see TcXunit-w5x.6/.7.
    /// </summary>
    public static class SuiteRunner
    {
        public static IReadOnlyList<TestCase> Discover(FB_TestSuite suite) => suite.Cases;

        public static TestCaseResult Run(FB_TestSuite suite, TestCase testCase) => suite.Run(testCase);

        public static IReadOnlyList<TestCaseResult> RunAll(FB_TestSuite suite) =>
            suite.Cases.Select(suite.Run).ToList();
    }
}
