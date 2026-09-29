using System;
using System.IO;
using xStunit.Cli;
using Xunit;

namespace xStunit.Cli.Tests
{
    // A non-conformant IMPLEMENTS is a per-file skip: the run still executes
    // every other suite and ends by test outcome, never as a usage error.
    public class CliRunnerImplementsConformanceTests : IDisposable
    {
        private readonly string _tempDir;

        public CliRunnerImplementsConformanceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "xStunitCliConformanceFixture_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
            File.WriteAllText(Path.Combine(_tempDir, "I_Sensor.TcIO"), InterfaceXml);
            File.WriteAllText(Path.Combine(_tempDir, "FB_Sensor.TcPOU"), NonConformantFbXml);
            File.WriteAllText(Path.Combine(_tempDir, "FB_PassingTests.TcPOU"), PassingSuiteXml);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        [Fact]
        public void Run_NonConformantFbAlongsideSuite_ReportsSkipAndStillExitsByOutcome()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir }, output);

            var text = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("1 passed, 0 failed, 1 skipped", text);
            Assert.Contains("FB_Sensor.TcPOU", text);
            Assert.Contains("I_Sensor", text);
            Assert.Contains("Reset", text);
        }

        private const string InterfaceXml = @"<TcPlcObject Version=""1.1.0.1"">
  <Itf Name=""I_Sensor"" Id=""{00000000-0000-0000-0000-0000000000d1}"">
    <Declaration><![CDATA[INTERFACE I_Sensor]]></Declaration>
    <Method Name=""Reset"" Id=""{00000000-0000-0000-0000-0000000000d2}"">
      <Declaration><![CDATA[METHOD Reset : BOOL]]></Declaration>
      <Implementation><ST><![CDATA[]]></ST></Implementation>
    </Method>
  </Itf>
</TcPlcObject>";

        private const string NonConformantFbXml = @"<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_Sensor"" Id=""{00000000-0000-0000-0000-0000000000d3}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_Sensor IMPLEMENTS I_Sensor]]></Declaration>
    <Implementation><ST><![CDATA[]]></ST></Implementation>
  </POU>
</TcPlcObject>";

        private const string PassingSuiteXml = @"<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_PassingTests"" Id=""{00000000-0000-0000-0000-0000000000d4}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_PassingTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation><ST><![CDATA[ThisAlwaysPasses();]]></ST></Implementation>
    <Method Name=""ThisAlwaysPasses"" Id=""{00000000-0000-0000-0000-0000000000d5}"">
      <Declaration><![CDATA[METHOD PRIVATE ThisAlwaysPasses
]]></Declaration>
      <Implementation><ST><![CDATA[TEST('ThisAlwaysPasses');
AssertTrue(Condition := (1 = 1), Message := 'ok');
TEST_FINISHED();]]></ST></Implementation>
    </Method>
  </POU>
</TcPlcObject>";
    }
}
