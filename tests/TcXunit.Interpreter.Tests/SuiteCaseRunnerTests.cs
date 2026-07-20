using System.Linq;
using TcXunit.Interpreter;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    public class SuiteCaseRunnerTests
    {
        private const string FixturePouDir =
            @"C:\Git\p_twincat_test_project\TestSolution\TestSolution\PLC1\POUs";

        [Fact]
        public void DiscoverCases_FixtureProject_ListsAllFourCasesUnderFbCounterTests()
        {
            var cases = SuiteCaseRunner.DiscoverCases(FixturePouDir);

            Assert.Equal(
                new[]
                {
                    ("FB_CounterTests", "CounterStartsAtZero"),
                    ("FB_CounterTests", "IncrementAddsDelta"),
                    ("FB_CounterTests", "DecrementClampsAtZero"),
                    ("FB_CounterTests", "ClampedCounterIncrementRespectsCeiling"),
                },
                cases.Select(c => (c.SuiteName, c.CaseName)));
        }

        [Fact]
        public void RunCase_SpecificCase_ReturnsOnlyThatCasesResult()
        {
            var result = SuiteCaseRunner.RunCase(FixturePouDir, "FB_CounterTests", "IncrementAddsDelta");

            Assert.Equal("IncrementAddsDelta", result.Name);
            Assert.True(result.Passed, result.ToString());
        }
    }
}
