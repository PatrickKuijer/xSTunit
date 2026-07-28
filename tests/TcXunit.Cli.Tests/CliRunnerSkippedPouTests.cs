using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using TcXunit.Cli;
using Xunit;

namespace TcXunit.Cli.Tests
{
    // TcXunit-iyd.7: a POU outside the v1 parse subset (or a structurally
    // unexpected file) must be skipped and reported individually instead of
    // aborting the whole run. Isolated temp-directory fixture so the
    // deliberately-unsupported POUs don't pollute the shared FB_Counter
    // fixture's all-green state.
    public class CliRunnerSkippedPouTests : IDisposable
    {
        private readonly string _tempDir;

        public CliRunnerSkippedPouTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TcXunitCliSkipFixture_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
            File.WriteAllText(Path.Combine(_tempDir, "FB_UsesTc2System.TcPOU"), UnsupportedPouXml);
            File.WriteAllText(Path.Combine(_tempDir, "FB_PassingTests.TcPOU"), PassingSuiteXml);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        [Fact]
        public void Run_UnsupportedPouAlongsideSuite_StillRunsSuiteAndReportsSkip()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir }, output);

            var text = output.ToString();
            // Exit 0 (a completed run), not 2 - the unsupported POU is a skip,
            // not a usage/discovery error.
            Assert.Equal(0, exitCode);
            Assert.Contains("PASS", text);
            Assert.Contains("1 passed, 0 failed, 1 skipped", text);
            Assert.Contains("FB_UsesTc2System.TcPOU", text);
            Assert.Contains("outside the v1 parse subset", text);
        }

        [Fact]
        public void Run_MalformedPouAlongsideSuite_StillRunsSuiteAndReportsSkip()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_Malformed.TcPOU"), "<TcPlcObject><POU Name=");
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir }, output);

            var text = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("1 passed, 0 failed, 2 skipped", text);
            Assert.Contains("FB_Malformed.TcPOU", text);
        }

        [Fact]
        public void Run_UnsupportedPou_JsonFormat_ListsSkippedFileAndReason()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            Assert.Equal(0, exitCode);
            using var doc = JsonDocument.Parse(output.ToString());
            var root = doc.RootElement;
            Assert.Equal(1, root.GetProperty("passed").GetInt32());
            var skipped = root.GetProperty("skipped");
            Assert.Equal(1, skipped.GetArrayLength());
            Assert.EndsWith("FB_UsesTc2System.TcPOU", skipped[0].GetProperty("filePath").GetString());
            Assert.Contains("outside the v1 parse subset", skipped[0].GetProperty("reason").GetString());
        }

        [Fact]
        public void Run_NoSkips_JsonFormat_EmitsEmptySkippedArray()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { TestFixtures.FbCounterFixtureDir(), "--format", "json" }, output);

            Assert.Equal(0, exitCode);
            using var doc = JsonDocument.Parse(output.ToString());
            Assert.Equal(0, doc.RootElement.GetProperty("skipped").GetArrayLength());
        }

        // Every candidate POU unsupported: still a discovery error (exit 2,
        // nothing ran), but the reason each file was dropped has to be visible
        // rather than just "no suites found".
        [Fact]
        public void Run_OnlyUnsupportedPous_ReturnsTwoAndReportsEachSkip()
        {
            var onlyUnsupportedDir = Path.Combine(_tempDir, "onlyUnsupported");
            Directory.CreateDirectory(onlyUnsupportedDir);
            File.WriteAllText(Path.Combine(onlyUnsupportedDir, "FB_UsesTc2System.TcPOU"), UnsupportedPouXml);
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { onlyUnsupportedDir }, output);

            var text = output.ToString();
            Assert.Equal(2, exitCode);
            Assert.Contains("no TcUnit suites found", text);
            Assert.Contains("FB_UsesTc2System.TcPOU", text);
        }

        [Fact]
        public void Run_OnlyUnsupportedPous_JsonFormat_ReportsErrorWithSkips()
        {
            var onlyUnsupportedDir = Path.Combine(_tempDir, "onlyUnsupportedJson");
            Directory.CreateDirectory(onlyUnsupportedDir);
            File.WriteAllText(Path.Combine(onlyUnsupportedDir, "FB_UsesTc2System.TcPOU"), UnsupportedPouXml);
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { onlyUnsupportedDir, "--format=json" }, output);

            Assert.Equal(2, exitCode);
            using var doc = JsonDocument.Parse(output.ToString());
            var root = doc.RootElement;
            Assert.Contains("no TcUnit suites found", root.GetProperty("error").GetString());
            Assert.Equal(1, root.GetProperty("skipped").GetArrayLength());
        }

        // Production-shaped POU that TcPouParser rejects outright
        // (Tc2_System call in the body), the exact shape that used to abort the
        // whole run.
        private const string UnsupportedPouXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_UsesTc2System"" Id=""{00000000-0000-0000-0000-0000000000b0}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_UsesTc2System
VAR
    fbTimer : Tc2_System.GETCURTASKINDEX;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[Tc2_System.GETCURTASKINDEX();]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        private const string PassingSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_PassingTests"" Id=""{00000000-0000-0000-0000-0000000000b1}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_PassingTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[ThisAlwaysPasses();]]></ST>
    </Implementation>
    <Method Name=""ThisAlwaysPasses"" Id=""{00000000-0000-0000-0000-0000000000b2}"">
      <Declaration><![CDATA[METHOD PRIVATE ThisAlwaysPasses
]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('ThisAlwaysPasses');

AssertTrue(Condition := (1 = 1),
           Message := 'one is always one');

TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";
    }
}
