using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using xStunit.Cli;
using Xunit;

namespace xStunit.Cli.Tests
{
    // A per-test failure carries its parts as fields, not just as prose a
    // consumer would have to regex back apart - and the location is one of
    // them, since prose alone cannot say which of three asserts in a method
    // failed.
    public class CliRunnerStructuredFailureTests : IDisposable
    {
        private readonly string _tempDir;

        public CliRunnerStructuredFailureTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TcXunitCliStructuredFailureFixture_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
            File.WriteAllText(Path.Combine(_tempDir, "FB_ThreeAssertTests.TcPOU"), ThreeAssertSuiteXml);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        [Fact]
        public void Run_FailedAssert_ReportsExpectedActualAndAssertMessageAsFields()
        {
            var failure = FirstFailure();

            Assert.Equal("assertion", failure.GetProperty("kind").GetString());
            Assert.Equal("AssertEquals_INT", failure.GetProperty("assert").GetString());
            Assert.Equal("99", failure.GetProperty("expected").GetString());
            Assert.Equal("3", failure.GetProperty("actual").GetString());
            Assert.Equal("second assert", failure.GetProperty("assertMessage").GetString());
        }

        [Fact]
        public void Run_SecondOfThreeAssertsFails_LocatesThatAssertNotTheMethod()
        {
            var failure = FirstFailure();

            Assert.Equal("FB_ThreeAssertTests", failure.GetProperty("pou").GetString());
            Assert.Equal("ThreeAsserts", failure.GetProperty("method").GetString());
            Assert.Equal(4, failure.GetProperty("bodyLine").GetInt32());
        }

        // The raw .TcPOU XML line, for a consumer opening the file directly
        // rather than through the XAE editor.
        [Fact]
        public void Run_FailedAssert_AlsoReportsTheRawFileLine()
        {
            var failure = FirstFailure();

            Assert.True(failure.GetProperty("line").GetInt32() > failure.GetProperty("bodyLine").GetInt32());
        }

        // The formatted line is rendered verbatim by text output and by the
        // VSIX results tree, so its wording is a contract rather than a detail.
        [Fact]
        public void Run_FailedAssert_StillCarriesTheFormattedMessageLine()
        {
            var failure = FirstFailure();

            Assert.Equal(
                "FAILED TEST 'ThreeAsserts', EXP: 99, ACT: 3, MSG: second assert",
                failure.GetProperty("message").GetString());
        }

        [Fact]
        public void Run_FailedAssert_TextOutputIsUnchanged()
        {
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir }, output);

            Assert.Contains("FAILED TEST 'ThreeAsserts', EXP: 99, ACT: 3, MSG: second assert", output.ToString());
        }

        private JsonElement FirstFailure()
        {
            var output = new StringWriter();
            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            using var document = JsonDocument.Parse(output.ToString());
            return document.RootElement
                .GetProperty("suites")[0]
                .GetProperty("tests")
                .EnumerateArray()
                .Single(t => !t.GetProperty("passed").GetBoolean())
                .GetProperty("failures")[0]
                .Clone();
        }

        // Body lines are what the bodyLine assertion above counts: TEST is 1,
        // the first assert 2, and the deliberately-failing middle one 4 - so
        // the blank lines in this fixture are load-bearing.
        private const string ThreeAssertSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_ThreeAssertTests"" Id=""{00000000-0000-0000-0000-0000000000d0}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_ThreeAssertTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[ThreeAsserts();]]></ST>
    </Implementation>
    <Method Name=""ThreeAsserts"" Id=""{00000000-0000-0000-0000-0000000000d1}"">
      <Declaration><![CDATA[METHOD PRIVATE ThreeAsserts]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('ThreeAsserts');
AssertEquals_INT(Expected := 3, Actual := 3, Message := 'first assert');

AssertEquals_INT(Expected := 99, Actual := 3, Message := 'second assert');

AssertEquals_INT(Expected := 7, Actual := 7, Message := 'third assert');
TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";
    }
}
