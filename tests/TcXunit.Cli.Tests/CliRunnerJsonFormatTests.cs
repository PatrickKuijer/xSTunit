using System;
using System.IO;
using System.Text.Json;
using TcXunit.Cli;
using Xunit;

namespace TcXunit.Cli.Tests
{
    // --format json (prototype spike for a future VSIX/TcAgent tool window
    // consuming structured results instead of parsing plain-text lines).
    public class CliRunnerJsonFormatTests : IDisposable
    {
        private readonly string _tempDir;

        public CliRunnerJsonFormatTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TcXunitCliJsonFixture_" + Guid.NewGuid());
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
