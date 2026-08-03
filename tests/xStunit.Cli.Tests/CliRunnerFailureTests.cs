using System;
using System.IO;
using xStunit.Cli;
using Xunit;

namespace xStunit.Cli.Tests
{
    // Isolated temp-directory fixture rather than the shared FB_Counter one, so
    // a deliberately-failing suite doesn't pollute its all-green state.
    public class CliRunnerFailureTests : IDisposable
    {
        private readonly string _tempDir;

        public CliRunnerFailureTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "xStunitCliFailureFixture_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
            File.WriteAllText(Path.Combine(_tempDir, "FB_AlwaysFailsTests.TcPOU"), AlwaysFailsSuiteXml);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        [Fact]
        public void Run_SuiteWithFailingAssertion_ReturnsOneAndPrintsFail()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir }, output);

            Assert.Equal(1, exitCode);
            Assert.Contains("FAIL", output.ToString());
        }

        private const string AlwaysFailsSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_AlwaysFailsTests"" Id=""{00000000-0000-0000-0000-0000000000aa}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_AlwaysFailsTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[ThisAlwaysFails();]]></ST>
    </Implementation>
    <Method Name=""ThisAlwaysFails"" Id=""{00000000-0000-0000-0000-0000000000ab}"">
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
