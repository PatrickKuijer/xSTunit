using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // The end-to-end guard for the whole chain: real .TcPOU fixture files
    // parsed, registered and interpreted, rather than a hand-built AST or a C#
    // stand-in for the suite.
    public class FbCounterTestsSuiteTests
    {
        private static readonly string FixtureDir = TestFixtures.FbCounterFixtureDir();

        [Fact]
        public void RunSuite_FbCounterTests_AllFourCasesPass()
        {
            var wanted = new[] { "FB_Counter.TcPOU", "FB_ClampedCounter.TcPOU", "FB_CounterTests.TcPOU" };
            var types = wanted.Select(f => TcPouParser.Parse(File.ReadAllText(Path.Combine(FixtureDir, f))));

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
