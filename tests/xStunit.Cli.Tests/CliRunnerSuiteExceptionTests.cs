using System;
using System.IO;
using xStunit.Cli;
using Xunit;

namespace xStunit.Cli.Tests
{
    // A suite that throws mid-run must not take the process down with it: the
    // other suites still report, and the throwing one shows up as a readable
    // FAIL line rather than a crash.
    public class CliRunnerSuiteExceptionTests : IDisposable
    {
        private readonly string _tempDir;

        public CliRunnerSuiteExceptionTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "xStunitCliSuiteExceptionFixture_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
            File.WriteAllText(Path.Combine(_tempDir, "FB_ThrowingSuiteTests.TcPOU"), ThrowingSuiteXml);
            File.WriteAllText(Path.Combine(_tempDir, "FB_CleanSuiteTests.TcPOU"), CleanSuiteXml);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        [Fact]
        public void Run_OneSuiteThrows_OtherSuiteStillReportsAndExitsOne()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir }, output);
            var text = output.ToString();

            Assert.Equal(1, exitCode);
            Assert.Contains("FB_ThrowingSuiteTests", text);
            Assert.Contains("FAIL", text);
            Assert.Contains("ThisPasses: PASS", text);
        }

        private const string ThrowingSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_ThrowingSuiteTests"" Id=""{00000000-0000-0000-0000-0000000000ac}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_ThrowingSuiteTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[ThisThrows();]]></ST>
    </Implementation>
    <Method Name=""ThisThrows"" Id=""{00000000-0000-0000-0000-0000000000ad}"">
      <Declaration><![CDATA[METHOD PRIVATE ThisThrows
]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('ThisThrows');

ThisMethodDoesNotExist();

TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";

        private const string CleanSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_CleanSuiteTests"" Id=""{00000000-0000-0000-0000-0000000000ae}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_CleanSuiteTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[ThisPasses();]]></ST>
    </Implementation>
    <Method Name=""ThisPasses"" Id=""{00000000-0000-0000-0000-0000000000af}"">
      <Declaration><![CDATA[METHOD PRIVATE ThisPasses
]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('ThisPasses');

AssertTrue(Condition := (1 = 1),
           Message := 'always true');

TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";
    }
}
