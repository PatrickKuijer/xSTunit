using System;
using System.IO;
using System.Text.Json;
using xStunit.Cli;
using Xunit;

namespace xStunit.Cli.Tests
{
    // --suite is repeatable, not single-valued: a second occurrence widens the
    // filter rather than replacing the first, which is what lets a caller
    // re-run an arbitrary subset of what it discovered.
    public class CliRunnerSuiteFilterTests : IDisposable
    {
        private readonly string _tempDir;

        public CliRunnerSuiteFilterTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TcXunitCliSuiteFilterFixture_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
            File.WriteAllText(Path.Combine(_tempDir, "FB_AlwaysPassesTests.TcPOU"), AlwaysPassesSuiteXml);
            File.WriteAllText(Path.Combine(_tempDir, "FB_AlwaysFailsTests.TcPOU"), AlwaysFailsSuiteXml);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        [Fact]
        public void Run_SuiteFilter_OneSuite_RestrictsResultsToNamedSuite()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir, "--suite", "FB_AlwaysPassesTests", "--format=json" }, output);

            Assert.Equal(0, exitCode);
            using var doc = JsonDocument.Parse(output.ToString());
            var root = doc.RootElement;
            var suites = root.GetProperty("suites");
            Assert.Equal(1, suites.GetArrayLength());
            Assert.Equal("FB_AlwaysPassesTests", suites[0].GetProperty("name").GetString());
        }

        [Fact]
        public void Run_SuiteFilter_MultipleSuites_RestrictsResultsToNamedSuites()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(
                new[] { _tempDir, "--suite", "FB_AlwaysPassesTests", "--suite", "FB_AlwaysFailsTests", "--format=json" },
                output);

            Assert.Equal(1, exitCode);
            using var doc = JsonDocument.Parse(output.ToString());
            var suites = doc.RootElement.GetProperty("suites");
            Assert.Equal(2, suites.GetArrayLength());
            var names = new[] { suites[0].GetProperty("name").GetString(), suites[1].GetProperty("name").GetString() };
            Assert.Contains("FB_AlwaysPassesTests", names);
            Assert.Contains("FB_AlwaysFailsTests", names);
        }

        [Fact]
        public void Run_SuiteFilter_UnknownSuiteName_JsonFormat_ReturnsTwoAndJsonError()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir, "--suite", "FB_DoesNotExist", "--format=json" }, output);

            Assert.Equal(2, exitCode);
            using var doc = JsonDocument.Parse(output.ToString());
            Assert.Contains("FB_DoesNotExist", doc.RootElement.GetProperty("error").GetString());
        }

        [Fact]
        public void Run_SuiteFilter_UnknownSuiteName_TextFormat_ReturnsTwoAndPrintsError()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir, "--suite", "FB_DoesNotExist" }, output);

            Assert.Equal(2, exitCode);
            Assert.Contains("FB_DoesNotExist", output.ToString());
        }

        [Fact]
        public void Run_NoSuiteFilter_RunsAllDiscoveredSuites()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir, "--format=json" }, output);

            Assert.Equal(1, exitCode);
            using var doc = JsonDocument.Parse(output.ToString());
            Assert.Equal(2, doc.RootElement.GetProperty("suites").GetArrayLength());
        }

        private const string AlwaysPassesSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_AlwaysPassesTests"" Id=""{00000000-0000-0000-0000-0000000000ee}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_AlwaysPassesTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[ThisAlwaysPasses();]]></ST>
    </Implementation>
    <Method Name=""ThisAlwaysPasses"" Id=""{00000000-0000-0000-0000-0000000000ff}"">
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
