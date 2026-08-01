using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using xStunit.Cli;
using Xunit;

namespace xStunit.Cli.Tests
{
    // A suite-level fault ends the suite, but it does not un-run the tests that
    // finished before it. Reporting those as `tests: []`, `passed: 0` is
    // actively misleading rather than merely incomplete: it tells a developer
    // whose setup code faulted late that nothing passed, and hides how far the
    // run got - the first thing worth knowing about a setup fault.
    //
    // Both output shapes carry the same answer. --format json and --stream
    // build their suite reports separately, so a fix to one that misses the
    // other leaves the two contradicting each other for exactly this case.
    public class CliRunnerSuiteErrorPartialResultsTests : IDisposable
    {
        private readonly string _tempDir;

        public CliRunnerSuiteErrorPartialResultsTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "XstunitCliSuiteErrorPartialFixture_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
            File.WriteAllText(Path.Combine(_tempDir, "FB_LateFaultTests.TcPOU"), LateFaultSuiteXml);
            File.WriteAllText(Path.Combine(_tempDir, "FB_ConvergenceMaster.TcPOU"), ConvergenceMasterXml);
            File.WriteAllText(Path.Combine(_tempDir, "FB_ConvergenceRamp.TcPOU"), ConvergenceRampXml);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        [Fact]
        public void Run_SuiteFaultsAfterACompletedTest_JsonReportKeepsThatTest()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            using var document = JsonDocument.Parse(output.ToString());
            var root = document.RootElement;
            var suite = root.GetProperty("suites")[0];

            Assert.Equal(1, exitCode);
            Assert.NotEqual(JsonValueKind.Null, suite.GetProperty("error").ValueKind);

            var test = Assert.Single(suite.GetProperty("tests").EnumerateArray().ToList());
            Assert.Equal("RanBeforeTheFault", test.GetProperty("name").GetString());
            Assert.True(test.GetProperty("passed").GetBoolean());
        }

        // The counts have to describe the same run the tests array does: a
        // report listing a passing test under passed: 0 is the misleading shape
        // again, one level up.
        [Fact]
        public void Run_SuiteFaultsAfterACompletedTest_CountsTheCompletedTestAndStillFailsTheRun()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            using var document = JsonDocument.Parse(output.ToString());
            var root = document.RootElement;

            Assert.Equal(1, root.GetProperty("passed").GetInt32());
            // The suite-level fault itself, which is what keeps the run failing
            // even though every test in it passed.
            Assert.Equal(1, root.GetProperty("failed").GetInt32());
            Assert.Equal(1, root.GetProperty("exitCode").GetInt32());
            Assert.Equal(1, exitCode);
        }

        // Classification is not this fix's business: the suite error keeps
        // whatever kind it had, so a consumer switching on `kind` sees no
        // change from gaining the tests array.
        [Fact]
        public void Run_SuiteFaultsAfterACompletedTest_SuiteErrorKindIsUnchanged()
        {
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            using var document = JsonDocument.Parse(output.ToString());
            var suite = document.RootElement.GetProperty("suites")[0];
            Assert.Equal("assertion", suite.GetProperty("kind").GetString());
            Assert.Contains("AssertConverges: fields did not converge", suite.GetProperty("error").GetString());
        }

        [Fact]
        public void Run_Stream_SuiteFaultsAfterACompletedTest_SuiteResultLineKeepsThatTest()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir, "--stream" }, output);

            var lines = ParseLines(output.ToString());
            var result = lines.Single(l => l.GetProperty("event").GetString() == "suite-result");

            Assert.Equal(1, exitCode);
            Assert.Equal("fail", result.GetProperty("outcome").GetString());
            var test = Assert.Single(result.GetProperty("tests").EnumerateArray().ToList());
            Assert.Equal("RanBeforeTheFault", test.GetProperty("name").GetString());
            Assert.True(test.GetProperty("passed").GetBoolean());

            var summary = lines.Single(l => l.GetProperty("event").GetString() == "summary");
            Assert.Equal(1, summary.GetProperty("passed").GetInt32());
            Assert.Equal(1, summary.GetProperty("failed").GetInt32());
            Assert.Equal(1, summary.GetProperty("exitCode").GetInt32());
        }

        // Text output is read by the human diagnosing the fault, so it owes the
        // same answer as the machine formats: the FAIL line alone leaves the
        // "1 passed" in the summary unaccounted for.
        [Fact]
        public void Run_SuiteFaultsAfterACompletedTest_TextOutputNamesThatTestAboveTheFailLine()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir }, output);
            var text = output.ToString();

            Assert.Equal(1, exitCode);
            Assert.Contains("RanBeforeTheFault: PASS", text);
            Assert.Contains("FB_LateFaultTests: FAIL", text);
            Assert.Contains("1 passed, 1 failed", text);
            Assert.True(
                text.IndexOf("RanBeforeTheFault: PASS", StringComparison.Ordinal) <
                text.IndexOf("FB_LateFaultTests: FAIL", StringComparison.Ordinal));
        }

        private static List<JsonElement> ParseLines(string ndjson) =>
            ndjson.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.TrimEnd('\r'))
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => JsonDocument.Parse(l).RootElement.Clone())
                .ToList();

        // The bead's reproduction: one bracketed test that passes, then a
        // convergence assertion in the suite body with no bracket open, so the
        // fault has no test to charge and escapes as a suite-level error.
        private const string LateFaultSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_LateFaultTests"" Id=""{00000000-0000-0000-0000-0000000000d0}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_LateFaultTests EXTENDS TcUnit.FB_TestSuite
VAR
	master : FB_ConvergenceMaster;
	proxy : FB_ConvergenceRamp;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[RanBeforeTheFault();
master.Value := 99;
AssertConverges(master, proxy, ['Value'], 2);]]></ST>
    </Implementation>
    <Method Name=""RanBeforeTheFault"" Id=""{00000000-0000-0000-0000-0000000000d1}"">
      <Declaration><![CDATA[METHOD PRIVATE RanBeforeTheFault]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('RanBeforeTheFault');
AssertEquals_INT(Expected := 1, Actual := 1, Message := '');
TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";

        private const string ConvergenceMasterXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_ConvergenceMaster"" Id=""{00000000-0000-0000-0000-0000000000d2}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_ConvergenceMaster
VAR
	Value : INT;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        // Ramps away from the master's value instead of toward it, so the
        // convergence budget is always exhausted.
        private const string ConvergenceRampXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_ConvergenceRamp"" Id=""{00000000-0000-0000-0000-0000000000d3}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_ConvergenceRamp
VAR
	Value : INT;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[Value := Value + 1;]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";
    }
}
