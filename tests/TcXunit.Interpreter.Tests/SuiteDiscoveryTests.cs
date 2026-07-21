using System.IO;
using System.Linq;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    public class SuiteDiscoveryTests
    {
        private const string FixturePouDir =
            @"C:\Git\p_twincat_test_project\TestSolution";

        [Fact]
        public void FindSuiteTypeNames_FixtureProject_FindsOnlyFbCounterTests()
        {
            var files = Directory.GetFiles(FixturePouDir, "*.TcPOU", SearchOption.AllDirectories);
            var types = files.Select(f => TcPouParser.Parse(File.ReadAllText(f))).ToList();
            var registry = new TypeRegistry(types);

            var suites = SuiteDiscovery.FindSuiteTypeNames(registry, types.Select(t => t.Name));

            Assert.Equal(new[] { "FB_CounterTests" }, suites);
        }
    }
}
