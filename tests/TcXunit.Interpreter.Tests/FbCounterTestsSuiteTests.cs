using System.IO;
using System.Linq;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // End-to-end target of TcXunit-w5x.12: run the real FB_CounterTests suite
    // (TcXunit-w5x.8 fixture) through the interpreter, not a hand-written C#
    // stand-in, and get back TcUnit-shaped pass/fail results.
    public class FbCounterTestsSuiteTests
    {
        private const string FixtureRootDir =
            @"C:\Git\p_twincat_test_project\TestSolution";

        [Fact]
        public void RunSuite_FbCounterTests_AllFourCasesPass()
        {
            var wanted = new[] { "FB_Counter.TcPOU", "FB_ClampedCounter.TcPOU", "FB_CounterTests.TcPOU" };
            var types = Directory.GetFiles(FixtureRootDir, "*.TcPOU", SearchOption.AllDirectories)
                .Where(f => wanted.Contains(Path.GetFileName(f)))
                .Select(f => TcPouParser.Parse(File.ReadAllText(f)));

            var engine = new Engine(new TypeRegistry(types));

            var results = engine.RunSuite("FB_CounterTests");

            Assert.Equal(4, results.Count);
            Assert.All(results, r => Assert.True(r.Passed, r.ToString()));
            Assert.Equal(
                new[]
                {
                    "CounterStartsAtZero",
                    "IncrementAddsDelta",
                    "DecrementClampsAtZero",
                    "ClampedCounterIncrementRespectsCeiling",
                },
                results.Select(r => r.Name));
        }
    }
}
