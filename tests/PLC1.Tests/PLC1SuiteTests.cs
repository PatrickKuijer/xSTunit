using System.Collections.Generic;
using System.Linq;
using TcXunit.Interpreter;
using Xunit;

namespace PLC1.Tests
{
    // This is the "PLC Test Project" side of the picture: open TcXunit.sln in
    // VS, Test Explorer lists every TcUnit TEST() case found under PLC1's
    // POUs as its own row (via [MemberData] discovery), runnable/rerunnable
    // individually - no PLC download needed (TcXunit-w5x.14).
    public class PLC1SuiteTests
    {
        private const string PouDirectory =
            @"C:\Git\p_twincat_test_project\TestSolution\TestSolution\PLC1\POUs";

        public static IEnumerable<object[]> Cases() =>
            SuiteCaseRunner.DiscoverCases(PouDirectory).Select(c => new object[] { c.SuiteName, c.CaseName });

        [Theory]
        [MemberData(nameof(Cases))]
        public void Case_Passes(string suiteName, string caseName)
        {
            var result = SuiteCaseRunner.RunCase(PouDirectory, suiteName, caseName);
            Assert.True(result.Passed, result.ToString());
        }
    }
}
