using System;
using System.IO;
using System.Text.Json;
using TcXunit.Cli;
using Xunit;

namespace TcXunit.Cli.Tests
{
    // TcXunit-p3t.1: a suite that throws used to log only its own name plus the
    // raw exception message - nothing said which PLC POU/method was executing.
    // The failure line (and the JSON suites[].error string, whose shape is
    // unchanged) now names the innermost POU + method.
    public class CliRunnerFailureLocationTests : IDisposable
    {
        private readonly string _tempDir;

        public CliRunnerFailureLocationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TcXunitCliFailureLocationFixture_" + Guid.NewGuid());
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
            // The "(" is where TcXunit-p3t.4's file line lands; this test still
            // only pins the POU + method half of the location (the line itself
            // is CliRunnerFailureLineTests' subject). TcXunit-3tx.3: the fault
            // is inside an open TEST() bracket, so the FAIL line is that test's
            // rather than the suite's - the location it carries is unchanged.
            Assert.Contains("ThisThrows: FAIL (FB_DeepHelper.Level3(", text);
            // The original message is preserved verbatim after the location.
            Assert.Contains("Method 'ThisMethodDoesNotExist' not found", text);
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

            // TcXunit-3tx.3: contained into the open test, so the located
            // message is failures[].message rather than suites[].error - still a
            // plain string, still the same text.
            var message = suite.GetProperty("tests")[0].GetProperty("failures")[0].GetProperty("message");
            Assert.Equal(JsonValueKind.String, message.ValueKind);
            Assert.StartsWith("FB_DeepHelper.Level3(", message.GetString());
            Assert.Contains("Method 'ThisMethodDoesNotExist' not found", message.GetString());
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
