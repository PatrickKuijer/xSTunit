using System.IO;
using TcXunit.Cli;
using Xunit;

namespace TcXunit.Cli.Tests
{
    public class CliRunnerTests
    {
        private static readonly string FixturePouDir = TestFixtures.FbCounterFixtureDir();

        [Fact]
        public void Run_FixtureProject_PrintsFourPassesAndReturnsZero()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixturePouDir }, output);

            Assert.Equal(0, exitCode);
            var text = output.ToString();
            Assert.Equal(4, CountOccurrences(text, "PASS"));
            Assert.DoesNotContain("FAIL", text);
        }

        [Fact]
        public void Run_MissingPath_ReturnsTwoAndPrintsError()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { @"C:\this\path\does\not\exist" }, output);

            Assert.Equal(2, exitCode);
            Assert.Contains("does not exist", output.ToString());
        }

        [Fact]
        public void Run_NoArgs_ReturnsTwoAndPrintsUsage()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new string[0], output);

            Assert.Equal(2, exitCode);
            Assert.Contains("usage", output.ToString().ToLowerInvariant());
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = haystack.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }
            return count;
        }
    }
}
