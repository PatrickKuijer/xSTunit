using System;
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

        [Fact]
        public void Run_SourceAndTestsInSeparateDirectories_UnionsAndPassesAllFour()
        {
            var sourceDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-cli-source-" + Guid.NewGuid()));
            var testsDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-cli-tests-" + Guid.NewGuid()));
            try
            {
                CopyFixtureFile(sourceDir.FullName, "FB_Counter.TcPOU");
                CopyFixtureFile(sourceDir.FullName, "FB_ClampedCounter.TcPOU");
                CopyFixtureFile(testsDir.FullName, "FB_CounterTests.TcPOU");

                var output = new StringWriter();

                var exitCode = CliRunner.Run(new[] { sourceDir.FullName, testsDir.FullName }, output);

                Assert.Equal(0, exitCode);
                var text = output.ToString();
                Assert.Equal(4, CountOccurrences(text, "PASS"));
                Assert.DoesNotContain("FAIL", text);
            }
            finally
            {
                Directory.Delete(sourceDir.FullName, recursive: true);
                Directory.Delete(testsDir.FullName, recursive: true);
            }
        }

        [Fact]
        public void Run_DuplicateTypeNameAcrossDirectories_ReturnsTwoAndPrintsClearError()
        {
            var dirA = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-cli-dupA-" + Guid.NewGuid()));
            var dirB = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-cli-dupB-" + Guid.NewGuid()));
            try
            {
                CopyFixtureFile(dirA.FullName, "FB_Counter.TcPOU");
                CopyFixtureFile(dirB.FullName, "FB_Counter.TcPOU");

                var output = new StringWriter();

                var exitCode = CliRunner.Run(new[] { dirA.FullName, dirB.FullName }, output);

                Assert.Equal(2, exitCode);
                Assert.Contains("FB_Counter", output.ToString());
            }
            finally
            {
                Directory.Delete(dirA.FullName, recursive: true);
                Directory.Delete(dirB.FullName, recursive: true);
            }
        }

        private static void CopyFixtureFile(string destDir, string fileName) =>
            File.Copy(Path.Combine(FixturePouDir, fileName), Path.Combine(destDir, fileName));

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
