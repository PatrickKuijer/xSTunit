using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using xStunit.Cli;
using Xunit;

namespace xStunit.Cli.Tests
{
    // The blast radius of a fault is the test that was open when it happened,
    // never the suite around it. If a faulting test could abandon its siblings,
    // they would vanish from the report rather than fail in it - and a consumer
    // reading passed:0 concludes its change broke everything, when in fact one
    // test broke and the rest never ran.
    //
    // The boundary is the TEST()/TEST_FINISHED() bracket: with no test open
    // there is nothing to charge the fault to, so it stays a suite-level error.
    public class CliRunnerTestBlastRadiusTests : IDisposable
    {
        private readonly string _tempDir;

        public CliRunnerTestBlastRadiusTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "xStunitCliBlastRadiusFixture_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        // The shape real suites take: one METHOD per test, all called from the
        // suite body.
        [Fact]
        public void Run_OneTestMethodFaults_OtherTestsInTheSameSuiteStillRunAndReport()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_MethodPerTestTests.TcPOU"), MethodPerTestSuiteXml);
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            using var document = JsonDocument.Parse(output.ToString());
            var root = document.RootElement;
            Assert.Equal(1, exitCode);
            Assert.Equal(3, root.GetProperty("passed").GetInt32());
            Assert.Equal(1, root.GetProperty("failed").GetInt32());
        }

        // A test that vanishes from the report is worse than a test that fails
        // in it, so the faulted one has to appear too.
        [Fact]
        public void Run_OneTestMethodFaults_AllFourTestsAppearInTheReport()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_MethodPerTestTests.TcPOU"), MethodPerTestSuiteXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            var tests = FirstSuite(output.ToString()).GetProperty("tests");
            Assert.Equal(4, tests.GetArrayLength());
            var faulted = tests.EnumerateArray().Single(t => !t.GetProperty("passed").GetBoolean());
            Assert.Equal("Faults", faulted.GetProperty("name").GetString());
        }

        // Charging the fault twice - once to the test, once to the suite - would
        // make one failure look like two; the suite did run to completion.
        [Fact]
        public void Run_OneTestMethodFaults_SuiteItselfReportsNoError()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_MethodPerTestTests.TcPOU"), MethodPerTestSuiteXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            var suite = FirstSuite(output.ToString());
            Assert.Equal(JsonValueKind.Null, suite.GetProperty("error").ValueKind);
            Assert.NotEqual(JsonValueKind.Null, suite.GetProperty("durationMs").ValueKind);
        }

        [Fact]
        public void Run_FaultOutsideAnyTestBracket_StaysASuiteLevelError()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_NoBracketTests.TcPOU"), FaultOutsideBracketSuiteXml);
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            var suite = FirstSuite(output.ToString());
            Assert.Equal(1, exitCode);
            Assert.NotEqual(JsonValueKind.Null, suite.GetProperty("error").ValueKind);
            Assert.Equal(0, suite.GetProperty("tests").GetArrayLength());
        }

        // The other suite shape: brackets written inline in the suite body
        // rather than one method per test. Here the statements after the fault
        // are still textually in the same body, so they have to be abandoned
        // with the test they belong to rather than run against a closed
        // bracket.
        [Fact]
        public void Run_InlineBracketsInSuiteBody_FaultedTestFailsAndLaterBracketsStillRun()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_InlineBracketTests.TcPOU"), InlineBracketSuiteXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            using var document = JsonDocument.Parse(output.ToString());
            var root = document.RootElement;
            Assert.Equal(1, root.GetProperty("passed").GetInt32());
            Assert.Equal(1, root.GetProperty("failed").GetInt32());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("suites")[0].GetProperty("error").ValueKind);
        }

        // Containment must not cost the failure its classification: "the
        // interpreter is behind" stays distinguishable from "your PLC code is
        // wrong" whether it surfaces on a test or on a suite.
        [Fact]
        public void Run_UnsupportedConstructInsideOneTest_ChargesAnUnsupportedConstructFailureToThatTest()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_SelPerTestTests.TcPOU"), UnsupportedConstructSuiteXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            var tests = FirstSuite(output.ToString()).GetProperty("tests");
            var faulted = tests.EnumerateArray().Single(t => !t.GetProperty("passed").GetBoolean());
            var failure = faulted.GetProperty("failures")[0];
            Assert.Equal("unsupported-construct", failure.GetProperty("kind").GetString());
            Assert.Equal("SEL", failure.GetProperty("construct").GetString());
        }

        private static JsonElement FirstSuite(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetProperty("suites")[0].Clone();
        }

        private const string MethodPerTestSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_MethodPerTestTests"" Id=""{00000000-0000-0000-0000-0000000000c0}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_MethodPerTestTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[PassesFirst();
Faults();
PassesSecond();
PassesThird();]]></ST>
    </Implementation>
    <Method Name=""PassesFirst"" Id=""{00000000-0000-0000-0000-0000000000c1}"">
      <Declaration><![CDATA[METHOD PRIVATE PassesFirst]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('PassesFirst');
AssertEquals_INT(Expected := 1, Actual := 1, Message := '');
TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
    <Method Name=""Faults"" Id=""{00000000-0000-0000-0000-0000000000c2}"">
      <Declaration><![CDATA[METHOD PRIVATE Faults]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('Faults');
ThisMethodDoesNotExist();
TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
    <Method Name=""PassesSecond"" Id=""{00000000-0000-0000-0000-0000000000c3}"">
      <Declaration><![CDATA[METHOD PRIVATE PassesSecond]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('PassesSecond');
AssertEquals_INT(Expected := 2, Actual := 2, Message := '');
TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
    <Method Name=""PassesThird"" Id=""{00000000-0000-0000-0000-0000000000c4}"">
      <Declaration><![CDATA[METHOD PRIVATE PassesThird]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('PassesThird');
AssertEquals_INT(Expected := 3, Actual := 3, Message := '');
TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";

        private const string FaultOutsideBracketSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_NoBracketTests"" Id=""{00000000-0000-0000-0000-0000000000c5}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_NoBracketTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[ThisMethodDoesNotExist();]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        private const string InlineBracketSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_InlineBracketTests"" Id=""{00000000-0000-0000-0000-0000000000c6}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_InlineBracketTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[TEST('Faults');
ThisMethodDoesNotExist();
AssertEquals_INT(Expected := 1, Actual := 1, Message := 'never reached');
TEST_FINISHED();
TEST('Passes');
AssertEquals_INT(Expected := 2, Actual := 2, Message := '');
TEST_FINISHED();]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        private const string UnsupportedConstructSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_SelPerTestTests"" Id=""{00000000-0000-0000-0000-0000000000c7}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_SelPerTestTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[UsesSel();
Passes();]]></ST>
    </Implementation>
    <Method Name=""UsesSel"" Id=""{00000000-0000-0000-0000-0000000000c8}"">
      <Declaration><![CDATA[METHOD PRIVATE UsesSel
VAR
	value : INT;
END_VAR
]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('UsesSel');
value := SEL(TRUE, 1, 3);
TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
    <Method Name=""Passes"" Id=""{00000000-0000-0000-0000-0000000000c9}"">
      <Declaration><![CDATA[METHOD PRIVATE Passes]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('Passes');
AssertEquals_INT(Expected := 1, Actual := 1, Message := '');
TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";
    }
}
