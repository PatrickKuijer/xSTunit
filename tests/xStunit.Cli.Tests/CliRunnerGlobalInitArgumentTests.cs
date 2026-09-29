using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using xStunit.Cli;
using xStunit.Vsix.TestRunner;
using Xunit;

namespace xStunit.Cli.Tests
{
    // A global whose FB_init arguments cannot be bound costs that global and
    // the suites reading it, nothing more. The run must still finish and exit
    // by test outcome, or an unrelated broken declaration hides every result.
    public class CliRunnerGlobalInitArgumentTests : IDisposable
    {
        private readonly string _tempDir;

        public CliRunnerGlobalInitArgumentTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "xStunitCliGlobalInitFixture_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
            File.WriteAllText(Path.Combine(_tempDir, "FB_Plain.TcPOU"), PlainFbXml);
            File.WriteAllText(Path.Combine(_tempDir, "GVL_Bad.TcGVL"), BadGvlXml);
            File.WriteAllText(Path.Combine(_tempDir, "FB_UnrelatedTests.TcPOU"), SuiteXml("FB_UnrelatedTests", "1 = 1"));
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        [Fact]
        public void Run_BadGlobalNoSuiteReads_UnrelatedSuiteRunsAndWarningIsReported()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir }, output);

            var text = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("1 passed, 0 failed", text);
            Assert.Contains(Path.Combine(_tempDir, "GVL_Bad.TcGVL"), text);
            Assert.Contains("FB_init arguments rejected", text);
            Assert.Contains("fbBad : FB_Plain(nope := 1);", text);
            Assert.Contains("Instance 'fbBad'", text);
            Assert.DoesNotContain("not understood", text);
        }

        // The wire shape is additive: filePath and lines keep their meaning
        // (an on-disk path and the declaration line), and the reason travels
        // only in the separate rejections array.
        [Fact]
        public void Run_JsonFormat_CarriesTheGlobalFaultUnderTheGvlPathWithLineAndReason()
        {
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            using var doc = JsonDocument.Parse(output.ToString());
            var warning = Assert.Single(doc.RootElement.GetProperty("warnings").EnumerateArray().ToList());
            Assert.Equal(Path.Combine(_tempDir, "GVL_Bad.TcGVL"), warning.GetProperty("filePath").GetString());
            Assert.Equal(
                "fbBad : FB_Plain(nope := 1);",
                Assert.Single(warning.GetProperty("lines").EnumerateArray().ToList()).GetString());
            var rejection = Assert.Single(warning.GetProperty("rejections").EnumerateArray().ToList());
            Assert.Equal("fbBad : FB_Plain(nope := 1);", rejection.GetProperty("line").GetString());
            Assert.Contains("declare an FB_init", rejection.GetProperty("reason").GetString());
        }

        [Fact]
        public void Run_JsonFormat_RejectionsDeserializeIntoTheExtensionModel()
        {
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            var result = JsonSerializer.Deserialize<XstunitRunResult>(
                output.ToString(), XstunitEventStream.SerializerOptions);
            var rejection = Assert.Single(Assert.Single(result.Warnings).Rejections);
            Assert.Equal("fbBad : FB_Plain(nope := 1);", rejection.Line);
            Assert.Contains("Instance 'fbBad'", rejection.Reason);
        }

        // One file is one warned file, however many kinds of problem it has.
        [Fact]
        public void Run_GvlWithUnreadableLineAndBadGlobal_IsOneWarnedFile()
        {
            File.WriteAllText(Path.Combine(_tempDir, "GVL_Bad.TcGVL"), BadGvlWithUnreadableLineXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            using var doc = JsonDocument.Parse(output.ToString());
            var warning = Assert.Single(doc.RootElement.GetProperty("warnings").EnumerateArray().ToList());
            Assert.Equal(2, warning.GetProperty("lines").GetArrayLength());
            Assert.Equal(1, warning.GetProperty("rejections").GetArrayLength());
        }

        [Fact]
        public void Run_SuiteReadingBadGlobal_FailsWithPreciseReasonAndExitsOne()
        {
            File.WriteAllText(
                Path.Combine(_tempDir, "FB_ReadsBadTests.TcPOU"),
                SuiteXml("FB_ReadsBadTests", "GVL_Bad.fbBad.nCount = 0"));
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir }, output);

            var text = output.ToString();
            Assert.Equal(1, exitCode);
            Assert.Contains("Instance 'fbBad'", text);
            Assert.DoesNotContain("Cannot access fields", text);
            Assert.Contains("1 passed, 1 failed", text);
        }

        private const string BadGvlWithUnreadableLineXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <GVL Name=""GVL_Bad"" Id=""{00000000-0000-0000-0000-0000000000f1}"">
    <Declaration><![CDATA[VAR_GLOBAL
    fbBad : FB_Plain(nope := 1);
    a, b : INT;
END_VAR]]></Declaration>
  </GVL>
</TcPlcObject>";

        private const string BadGvlXml =@"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <GVL Name=""GVL_Bad"" Id=""{00000000-0000-0000-0000-0000000000f1}"">
    <Declaration><![CDATA[VAR_GLOBAL
    fbBad : FB_Plain(nope := 1);
END_VAR]]></Declaration>
  </GVL>
</TcPlcObject>";

        private const string PlainFbXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_Plain"" Id=""{00000000-0000-0000-0000-0000000000f2}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_Plain
VAR
    nCount : INT;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[nCount := nCount + 1;]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        private static string SuiteXml(string name, string condition) => $@"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""{name}"" Id=""{{00000000-0000-0000-0000-0000000000f3}}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK {name} EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[M_Check();]]></ST>
    </Implementation>
    <Method Name=""M_Check"" Id=""{{00000000-0000-0000-0000-0000000000f4}}"">
      <Declaration><![CDATA[METHOD PRIVATE M_Check
]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('M_Check');

AssertTrue(Condition := ({condition}),
           Message := 'condition holds');

TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";
    }
}
