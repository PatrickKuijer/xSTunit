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

        [Fact]
        public void Run_PouOutsideParseSubset_ReturnsTwoAndPrintsClearError()
        {
            var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-cli-rejected-" + Guid.NewGuid()));
            try
            {
                File.WriteAllText(Path.Combine(dir.FullName, "FB_UsesTc2System.TcPOU"), RejectedPouXml);

                var output = new StringWriter();

                var exitCode = CliRunner.Run(new[] { dir.FullName }, output);

                Assert.Equal(2, exitCode);
                Assert.Contains("Tc2_System", output.ToString());
            }
            finally
            {
                Directory.Delete(dir.FullName, recursive: true);
            }
        }

        private const string RejectedPouXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_UsesTc2System"" Id=""{00000000-0000-0000-0000-0000000000ba}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_UsesTc2System]]></Declaration>
    <Implementation>
      <ST><![CDATA[Tc2_System.SOME_FUNCTION();]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

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
