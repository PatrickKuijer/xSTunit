using System;
using System.IO;
using System.Text.Json;
using xStunit.Cli;
using Xunit;

namespace xStunit.Cli.Tests
{
    // A fault several frames deep must name the innermost PLC POU + method that
    // was executing; the suite name and the raw exception message alone do not
    // tell a reader which file to open.
    public class CliRunnerFailureLocationTests : IDisposable
    {
        private readonly string _tempDir;

        public CliRunnerFailureLocationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "xStunitCliFailureLocationFixture_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
            File.WriteAllText(Path.Combine(_tempDir, "FB_DeepHelper.TcPOU"), DeepHelperXml);
            File.WriteAllText(Path.Combine(_tempDir, "FB_NestedThrowTests.TcPOU"), NestedThrowSuiteXml);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        [Fact]
        public void Run_SuiteThrowsDeepInCallChain_TextLogNamesInnermostPouAndMethod()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir }, output);
            var text = output.ToString();

            Assert.Equal(1, exitCode);
            // The trailing "(" is where the line number lands; only the POU +
            // method half is pinned here, the number itself being
            // CliRunnerFailureLineTests' subject.
            Assert.Contains("ThisThrows: FAIL (FB_DeepHelper.Level3(", text);
            // The original message survives verbatim after the location.
            Assert.Contains("'ThisMethodDoesNotExist' not found", text);
        }

        [Fact]
        public void Run_SuiteThrowsDeepInCallChain_JsonMessageCarriesLocation()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir, "--format=json" }, output);

            Assert.Equal(1, exitCode);
            using var doc = JsonDocument.Parse(output.ToString());
            var suite = doc.RootElement.GetProperty("suites")[0];
            Assert.Equal("FB_NestedThrowTests", suite.GetProperty("name").GetString());

            var message = suite.GetProperty("tests")[0].GetProperty("failures")[0].GetProperty("message");
            Assert.Equal(JsonValueKind.String, message.ValueKind);
            Assert.StartsWith("FB_DeepHelper.Level3(", message.GetString());
            Assert.Contains("'ThisMethodDoesNotExist' not found", message.GetString());
        }

        private const string DeepHelperXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_DeepHelper"" Id=""{00000000-0000-0000-0000-0000000000b0}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_DeepHelper]]></Declaration>
    <Implementation>
      <ST><![CDATA[]]></ST>
    </Implementation>
    <Method Name=""Level1"" Id=""{00000000-0000-0000-0000-0000000000b1}"">
      <Declaration><![CDATA[METHOD PUBLIC Level1
]]></Declaration>
      <Implementation>
        <ST><![CDATA[Level2();]]></ST>
      </Implementation>
    </Method>
    <Method Name=""Level2"" Id=""{00000000-0000-0000-0000-0000000000b2}"">
      <Declaration><![CDATA[METHOD PUBLIC Level2
]]></Declaration>
      <Implementation>
        <ST><![CDATA[Level3();]]></ST>
      </Implementation>
    </Method>
    <Method Name=""Level3"" Id=""{00000000-0000-0000-0000-0000000000b3}"">
      <Declaration><![CDATA[METHOD PUBLIC Level3
]]></Declaration>
      <Implementation>
        <ST><![CDATA[ThisMethodDoesNotExist();]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";

        private const string NestedThrowSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_NestedThrowTests"" Id=""{00000000-0000-0000-0000-0000000000b4}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_NestedThrowTests EXTENDS TcUnit.FB_TestSuite
VAR
	deep : FB_DeepHelper;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[ThisThrows();]]></ST>
    </Implementation>
    <Method Name=""ThisThrows"" Id=""{00000000-0000-0000-0000-0000000000b5}"">
      <Declaration><![CDATA[METHOD PRIVATE ThisThrows
]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('ThisThrows');

deep.Level1();

TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";
    }
}
