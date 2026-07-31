using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using xStunit.Cli;
using Xunit;

namespace xStunit.Cli.Tests
{
    // TcXunit-3tx.3: a fault inside ONE test method used to abandon the entire
    // suite - the remaining tests never ran and were not reported at all, so a
    // consuming agent read passed:0 and concluded its change broke everything.
    // Blast radius belongs to the test, not the suite.
    public class CliRunnerTestBlastRadiusTests : IDisposable
    {
        private readonly string _tempDir;

        public CliRunnerTestBlastRadiusTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TcXunitCliBlastRadiusFixture_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        // The acceptance repro, in the shape real TcUnit suites take: one
        // METHOD per test, all called from the suite body.
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

        // Every test is present in the report, including the faulted one -
        // a test that vanishes is worse than a test that fails.
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

        // A fault absorbed into a test must NOT also be reported as a suite
        // error - the suite ran to completion, so `error` stays null and the
        // per-suite duration is real.
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

        // A fault outside any TEST()/TEST_FINISHED() bracket has no test to
        // charge it to, so it stays a suite-level error exactly as before.
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

        // The other suite shape: TEST()/TEST_FINISHED() brackets written inline
        // in the suite body. The statements still belonging to the faulted test
        // must be abandoned - not run against a closed bracket - and the next
        // TEST() must pick up cleanly.
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

        // The failure charged to the faulted test carries the same kind
        // vocabulary as a suite-level error (TcXunit-3tx.1), so "the
        // interpreter is behind" stays distinguishable wherever it surfaces.
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
