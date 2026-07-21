using System.IO;
using System.Linq;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    public class SuiteDiscoveryTests
    {
        private static readonly string FixturePouDir = TestFixtures.FbCounterFixtureDir();

        [Fact]
        public void FindSuiteTypeNames_FixtureProject_FindsOnlyFbCounterTests()
        {
            var files = Directory.GetFiles(FixturePouDir, "*.TcPOU");
            var types = files.Select(f => TcPouParser.Parse(File.ReadAllText(f))).ToList();
            var registry = new TypeRegistry(types);

            var suites = SuiteDiscovery.FindSuiteTypeNames(registry, types.Select(t => t.Name));

            Assert.Equal(new[] { "FB_CounterTests" }, suites);
        }
    }
}
