using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using xStunit.Cli;
using Xunit;

namespace xStunit.Cli.Tests
{
    // A declaration line the parser cannot spell costs a variable, not a file.
    // The POU still loads, its suites still run, and the run still exits by
    // test outcome - so unless the loss is reported here, nothing in the run
    // mentions it at all. Isolated temp-directory fixture, because these POUs
    // are deliberately not fully readable.
    public class CliRunnerDeclarationWarningTests : IDisposable
    {
        private readonly string _tempDir;

        public CliRunnerDeclarationWarningTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "xStunitCliWarningFixture_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
            File.WriteAllText(Path.Combine(_tempDir, "FB_PassingTests.TcPOU"), PassingSuiteXml);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        [Fact]
        public void Run_PouWithUnreadableDeclarationLine_RunsSuitesAndReportsTheLineAsAWarning()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_Drive.TcPOU"), DriveWithUnreadableLinePouXml);
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir }, output);

            var text = output.ToString();
            // Exit 0: a warning never changes the exit code, exactly as a
            // skip never does. The suite alongside it still ran.
            Assert.Equal(0, exitCode);
            Assert.Contains("1 passed, 0 failed, 1 file with warnings", text);
            Assert.Contains("FB_Drive.TcPOU", text);
            Assert.Contains("i, j : INT;", text);

            // The count of files LOST stays zero: this file was not lost.
            Assert.DoesNotContain("skipped", text);
        }

        [Fact]
        public void Run_TreeWhoseDeclarationsAllParse_MentionsNoWarnings()
        {
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir }, output);

            var text = output.ToString();
            Assert.Contains("1 passed, 0 failed", text);
            Assert.DoesNotContain("warning", text);
        }

        // The wire shape carries warnings separately from skips, so a consumer
        // counting skips to report reduced coverage never counts these too.
        [Fact]
        public void Run_JsonFormat_CarriesWarningsAsTheirOwnArray()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_Drive.TcPOU"), DriveWithUnreadableLinePouXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            using var doc = JsonDocument.Parse(output.ToString());
            var root = doc.RootElement;

            Assert.Empty(root.GetProperty("skipped").EnumerateArray());

            var warning = Assert.Single(root.GetProperty("warnings").EnumerateArray().ToList());
            Assert.EndsWith("FB_Drive.TcPOU", warning.GetProperty("filePath").GetString());
            Assert.Equal(
                new[] { "i, j : INT;" },
                warning.GetProperty("lines").EnumerateArray().Select(l => l.GetString()).ToArray());
        }

        [Fact]
        public void Run_GvlWithUnreadableDeclarationLine_ReportsTheLineAsAWarning()
        {
            File.WriteAllText(Path.Combine(_tempDir, "gLimits.TcGVL"), GvlWithUnreadableLineXml);
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir }, output);

            var text = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("1 passed, 0 failed, 1 file with warnings", text);
            Assert.Contains("gLimits.TcGVL", text);
            Assert.Contains("nLow, nHigh : INT;", text);
            Assert.DoesNotContain("skipped", text);
        }

        [Fact]
        public void Run_StructDutWithUnreadableFieldLine_ReportsTheLineAsAWarning()
        {
            File.WriteAllText(Path.Combine(_tempDir, "ST_Pair.TcDUT"), StructWithUnreadableLineXml);
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir }, output);

            var text = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("1 passed, 0 failed, 1 file with warnings", text);
            Assert.Contains("ST_Pair.TcDUT", text);
            Assert.Contains("a, b : INT;", text);
            Assert.DoesNotContain("skipped", text);
        }

        [Fact]
        public void Run_JsonFormat_AttributesGvlAndDutWarningsToTheirOwnFiles()
        {
            File.WriteAllText(Path.Combine(_tempDir, "gLimits.TcGVL"), GvlWithUnreadableLineXml);
            File.WriteAllText(Path.Combine(_tempDir, "ST_Pair.TcDUT"), StructWithUnreadableLineXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            using var doc = JsonDocument.Parse(output.ToString());
            var warnings = doc.RootElement.GetProperty("warnings").EnumerateArray().ToList();

            Assert.Equal(2, warnings.Count);
            var gvl = warnings.Single(w => w.GetProperty("filePath").GetString().EndsWith("gLimits.TcGVL"));
            var dut = warnings.Single(w => w.GetProperty("filePath").GetString().EndsWith("ST_Pair.TcDUT"));
            Assert.Equal("nLow, nHigh : INT;", Assert.Single(gvl.GetProperty("lines").EnumerateArray().ToList()).GetString());
            Assert.Equal("a, b : INT;", Assert.Single(dut.GetProperty("lines").EnumerateArray().ToList()).GetString());
        }

        private const string GvlWithUnreadableLineXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <GVL Name=""gLimits"" Id=""{00000000-0000-0000-0000-0000000000e3}"">
    <Declaration><![CDATA[VAR_GLOBAL CONSTANT
    nMax : INT := 16;
    nLow, nHigh : INT;
END_VAR]]></Declaration>
  </GVL>
</TcPlcObject>";

        private const string StructWithUnreadableLineXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <DUT Name=""ST_Pair"" Id=""{00000000-0000-0000-0000-0000000000e4}"">
    <Declaration><![CDATA[TYPE ST_Pair :
STRUCT
    x : INT;
    a, b : INT;
END_STRUCT
END_TYPE]]></Declaration>
  </DUT>
</TcPlcObject>";

        private const string DriveWithUnreadableLinePouXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_Drive"" Id=""{00000000-0000-0000-0000-0000000000e0}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_Drive
VAR
    nCount : INT;
    i, j : INT;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[nCount := nCount + 1;]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        private const string PassingSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_PassingTests"" Id=""{00000000-0000-0000-0000-0000000000e1}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_PassingTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[ThisAlwaysPasses();]]></ST>
    </Implementation>
    <Method Name=""ThisAlwaysPasses"" Id=""{00000000-0000-0000-0000-0000000000e2}"">
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
