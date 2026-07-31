using System;
using System.IO;
using xStunit.Cli;
using Xunit;

namespace xStunit.Cli.Tests
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
            var text = output.ToString();
            Assert.Contains("usage", text.ToLowerInvariant());
            Assert.Contains("--suite", text);
            Assert.Contains("--help", text);
        }

        [Theory]
        [InlineData("--help")]
        [InlineData("-h")]
        public void Run_Help_PrintsHelpAndReturnsZero(string helpFlag)
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { helpFlag }, output);

            Assert.Equal(0, exitCode);
            var text = output.ToString();
            Assert.Contains("Usage:", text);
            foreach (var flag in new[] { "--format", "--suite", "--plugins", "--coverage", "--stream", "--help" })
                Assert.Contains(flag, text);
            Assert.Contains("Examples:", text);
        }

        [Fact]
        public void Run_HelpWithOtherArgs_TakesPrecedenceAndReturnsZero()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixturePouDir, "--help" }, output);

            Assert.Equal(0, exitCode);
            Assert.Contains("Usage:", output.ToString());
        }

        [Fact]
        public void Run_HelpImmediatelyAfterValueConsumingFlag_StillShowsHelp()
        {
            // --plugins (like --format/--suite) consumes the very next token
            // as its value, so --help has to be recognised before that.
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { "--plugins", "--help" }, output);

            Assert.Equal(0, exitCode);
            Assert.Contains("Usage:", output.ToString());
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
        public void Run_LaterArgMissingPath_ReturnsTwoAndNamesThatPath()
        {
            // Every input path is checked, not just the first: with one
            // argument a "check args[0] only" implementation is
            // indistinguishable from a correct one.
            var missingPath = Path.Combine(Path.GetTempPath(), "tcxunit-cli-missing-" + Guid.NewGuid());

            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixturePouDir, missingPath }, output);

            Assert.Equal(2, exitCode);
            var text = output.ToString();
            Assert.Contains("does not exist", text);
            Assert.Contains(missingPath, text);
        }

        [Fact]
        public void Run_SameDirectoryPassedTwice_DeduplicatesAndPassesAllFour()
        {
            // Repeating an input directory is a union with itself: the loader
            // de-duplicates resolved paths before parsing, so the same file
            // reached twice must not trip the duplicate-type error that a
            // genuine name clash across directories does.
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { FixturePouDir, FixturePouDir }, output);

            Assert.Equal(0, exitCode);
            var text = output.ToString();
            Assert.Equal(4, CountOccurrences(text, "PASS"));
            Assert.DoesNotContain("FAIL", text);
        }

        [Fact]
        public void Run_SuiteDependsOnStructTypeFromTcDutFile_ResolvesAndPasses()
        {
            // The CLI builds its own TypeRegistry; if that registry omits
            // STRUCTs declared in .TcDUT files, a suite depending on one fails
            // to resolve here while Test Explorer discovery still works.
            var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-cli-struct-" + Guid.NewGuid()));
            try
            {
                File.WriteAllText(Path.Combine(dir.FullName, "ST_Msg.TcDUT"), StructDutXml);
                File.WriteAllText(Path.Combine(dir.FullName, "FB_StructTests.TcPOU"), StructSuitePouXml);

                var output = new StringWriter();

                var exitCode = CliRunner.Run(new[] { dir.FullName }, output);

                var text = output.ToString();
                Assert.Equal(0, exitCode);
                Assert.Equal(1, CountOccurrences(text, "PASS"));
                Assert.DoesNotContain("FAIL", text);
            }
            finally
            {
                Directory.Delete(dir.FullName, recursive: true);
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

        private const string StructDutXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <DUT Name=""ST_Msg"" Id=""{00000000-0000-0000-0000-0000000000db}"">
    <Declaration><![CDATA[TYPE ST_Msg :
STRUCT
	count : INT;
END_STRUCT
END_TYPE]]></Declaration>
  </DUT>
</TcPlcObject>";

        private const string StructSuitePouXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <POU Name=""FB_StructTests"" Id=""{00000000-0000-0000-0000-0000000000dc}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_StructTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[StructFieldDefaultsToZero();]]></ST>
    </Implementation>
    <Method Name=""StructFieldDefaultsToZero"" Id=""{00000000-0000-0000-0000-0000000000dd}"">
      <Declaration><![CDATA[METHOD PRIVATE StructFieldDefaultsToZero
VAR
	msg : ST_Msg;
END_VAR
]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('StructFieldDefaultsToZero');

msg.count := 5;

AssertEquals_INT(Expected := 5,
                  Actual := msg.count,
                  Message := 'ST_Msg field roundtrip');

TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
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
