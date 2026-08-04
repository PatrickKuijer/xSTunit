using System;
using System.IO;
using System.Text.Json;
using xStunit.Cli;
using Xunit;

namespace xStunit.Cli.Tests
{
    // The key names asserted here are a consumed wire contract - renaming one
    // is invisible to the compiler and silently breaks every machine consumer
    // reading structured results instead of the plain-text lines.
    public class CliRunnerJsonFormatTests : IDisposable
    {
        private readonly string _tempDir;

        public CliRunnerJsonFormatTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "xStunitCliJsonFixture_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
            File.WriteAllText(Path.Combine(_tempDir, "FB_AlwaysFailsTests.TcPOU"), AlwaysFailsSuiteXml);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        [Fact]
        public void Run_FixtureProject_JsonFormat_ReturnsZeroAndValidJsonWithFourPasses()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { TestFixtures.FbCounterFixtureDir(), "--format", "json" }, output);

            Assert.Equal(0, exitCode);
            using var doc = JsonDocument.Parse(output.ToString());
            var root = doc.RootElement;
            Assert.Equal(4, root.GetProperty("passed").GetInt32());
            Assert.Equal(0, root.GetProperty("failed").GetInt32());
            Assert.Equal(0, root.GetProperty("exitCode").GetInt32());
            Assert.True(root.GetProperty("suites").GetArrayLength() > 0);
            var suite = root.GetProperty("suites")[0];
            Assert.EndsWith(".TcPOU", suite.GetProperty("filePath").GetString());
        }

        [Fact]
        public void Run_FailingSuite_JsonFormatEquals_ReturnsOneAndReportsFailureMessage()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir, "--format=json" }, output);

            Assert.Equal(1, exitCode);
            using var doc = JsonDocument.Parse(output.ToString());
            var root = doc.RootElement;
            Assert.Equal(1, root.GetProperty("failed").GetInt32());
            var suite = root.GetProperty("suites")[0];
            Assert.EndsWith("FB_AlwaysFailsTests.TcPOU", suite.GetProperty("filePath").GetString());
            var test = suite.GetProperty("tests")[0];
            Assert.False(test.GetProperty("passed").GetBoolean());
            Assert.True(test.GetProperty("failures").GetArrayLength() > 0);
        }

        [Fact]
        public void Run_JsonFormat_TestsCarryNonNegativeDurationMs_ForPassingAndFailingTests()
        {
            var passingOutput = new StringWriter();
            var passingExitCode = CliRunner.Run(new[] { TestFixtures.FbCounterFixtureDir(), "--format", "json" }, passingOutput);
            Assert.Equal(0, passingExitCode);
            using var passingDoc = JsonDocument.Parse(passingOutput.ToString());
            var passingTests = passingDoc.RootElement.GetProperty("suites")[0].GetProperty("tests");
            Assert.True(passingTests.GetArrayLength() > 0);
            foreach (var test in passingTests.EnumerateArray())
            {
                var durationMs = test.GetProperty("durationMs");
                Assert.Equal(JsonValueKind.Number, durationMs.ValueKind);
                Assert.True(durationMs.GetInt64() >= 0);
            }

            var failingOutput = new StringWriter();
            var failingExitCode = CliRunner.Run(new[] { _tempDir, "--format=json" }, failingOutput);
            Assert.Equal(1, failingExitCode);
            using var failingDoc = JsonDocument.Parse(failingOutput.ToString());
            var failingTest = failingDoc.RootElement.GetProperty("suites")[0].GetProperty("tests")[0];
            Assert.False(failingTest.GetProperty("passed").GetBoolean());
            var failingDurationMs = failingTest.GetProperty("durationMs");
            Assert.Equal(JsonValueKind.Number, failingDurationMs.ValueKind);
            Assert.True(failingDurationMs.GetInt64() >= 0);
        }

        [Fact]
        public void Run_JsonFormat_PassingSuite_ReportsNonNegativeDurationMs()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { TestFixtures.FbCounterFixtureDir(), "--format", "json" }, output);

            Assert.Equal(0, exitCode);
            using var doc = JsonDocument.Parse(output.ToString());
            var suite = doc.RootElement.GetProperty("suites")[0];
            var durationMs = suite.GetProperty("durationMs");
            Assert.Equal(JsonValueKind.Number, durationMs.ValueKind);
            Assert.True(durationMs.GetInt64() >= 0);
        }

        [Fact]
        public void Run_JsonFormat_SuiteWithAFailingTest_StillReportsDurationMs()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir, "--format=json" }, output);

            Assert.Equal(1, exitCode);
            using var doc = JsonDocument.Parse(output.ToString());
            var suite = doc.RootElement.GetProperty("suites")[0];
            var test = suite.GetProperty("tests")[0];
            Assert.False(test.GetProperty("passed").GetBoolean());

            // The suite ran to completion - only the assertion inside it failed
            // - so a duration is still meaningful, unlike the load-failure path
            // below where nothing ran.
            var durationMs = suite.GetProperty("durationMs");
            Assert.Equal(JsonValueKind.Number, durationMs.ValueKind);
            Assert.True(durationMs.GetInt64() >= 0);
        }

        [Fact]
        public void Run_JsonFormat_SuiteLoadFailure_OmitsOrNullsDurationMsRatherThanReportingZero()
        {
            var brokenDir = Path.Combine(Path.GetTempPath(), "xStunitCliJsonBrokenFixture_" + Guid.NewGuid());
            Directory.CreateDirectory(brokenDir);
            try
            {
                File.WriteAllText(Path.Combine(brokenDir, "FB_BrokenSuiteTests.TcPOU"), BrokenSuiteXml);

                var output = new StringWriter();
                var exitCode = CliRunner.Run(new[] { brokenDir, "--format=json" }, output);

                Assert.Equal(1, exitCode);
                using var doc = JsonDocument.Parse(output.ToString());
                var suite = doc.RootElement.GetProperty("suites")[0];
                Assert.False(string.IsNullOrEmpty(suite.GetProperty("error").GetString()));

                var durationMsKind = suite.TryGetProperty("durationMs", out var durationMs)
                    ? durationMs.ValueKind
                    : JsonValueKind.Undefined;
                Assert.True(
                    durationMsKind == JsonValueKind.Null || durationMsKind == JsonValueKind.Undefined,
                    $"expected durationMs to be omitted or null, got {durationMsKind}");
            }
            finally
            {
                Directory.Delete(brokenDir, recursive: true);
            }
        }

        [Fact]
        public void Run_MissingPath_JsonFormat_ReturnsTwoAndJsonError()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { @"C:\this\path\does\not\exist", "--format", "json" }, output);

            Assert.Equal(2, exitCode);
            using var doc = JsonDocument.Parse(output.ToString());
            Assert.Contains("does not exist", doc.RootElement.GetProperty("error").GetString());
        }

        [Fact]
        public void Run_UnknownFormat_ReturnsTwoAndPrintsError()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { TestFixtures.FbCounterFixtureDir(), "--format", "xml" }, output);

            Assert.Equal(2, exitCode);
            Assert.Contains("unknown --format value", output.ToString());
        }

        // Discoverable as a suite, but its body calls a method that exists
        // nowhere in the EXTENDS chain, so the run throws before any suite
        // completes - the load-failure path, distinct from a suite that ran and
        // had a failing TEST().
        private const string BrokenSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_BrokenSuiteTests"" Id=""{00000000-0000-0000-0000-0000000000dd}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_BrokenSuiteTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[ThisMethodDoesNotExistAnywhere();]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        private const string AlwaysFailsSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_AlwaysFailsTests"" Id=""{00000000-0000-0000-0000-0000000000bb}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_AlwaysFailsTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[ThisAlwaysFails();]]></ST>
    </Implementation>
    <Method Name=""ThisAlwaysFails"" Id=""{00000000-0000-0000-0000-0000000000cc}"">
      <Declaration><![CDATA[METHOD PRIVATE ThisAlwaysFails
]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('ThisAlwaysFails');

AssertTrue(Condition := (1 = 2),
           Message := 'one is never two');

TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";
    }
}
