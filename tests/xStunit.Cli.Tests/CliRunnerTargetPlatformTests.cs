using System;
using System.IO;
using xStunit.Cli;
using Xunit;

namespace xStunit.Cli.Tests
{
    // --target is how a run says which machine the code under test is built
    // for, and an address width is the only thing that answer changes. The
    // suite below asserts the 32-bit answer outright, so the same source, the
    // same suite and the same command line differ only in the flag - which is
    // what makes the flag's effect visible rather than assumed.
    public class CliRunnerTargetPlatformTests
    {
        [Theory]
        [InlineData(null, 0)]
        [InlineData("x86", 0)]
        [InlineData("X86", 0)]
        [InlineData("x64", 1)]
        public void Run_Target_DecidesHowWideAnAddressIs(string target, int expectedExitCode)
        {
            var dir = NewFixtureDirectory();
            try
            {
                var args = target == null
                    ? new[] { dir }
                    : new[] { dir, "--target", target };
                var output = new StringWriter();

                var exitCode = CliRunner.Run(args, output);

                Assert.Equal(expectedExitCode, exitCode);
                Assert.Contains(expectedExitCode == 0 ? "PASS" : "FAIL", output.ToString());
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // The =value spelling reaches the same place as the two-token one; a
        // flag parsed by only one of its forms is a flag half the callers
        // cannot use.
        [Fact]
        public void Run_TargetWithAttachedValue_IsReadTheSameAsTheSeparateOne()
        {
            var dir = NewFixtureDirectory();
            try
            {
                var output = new StringWriter();

                Assert.Equal(1, CliRunner.Run(new[] { dir, "--target=x64" }, output));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // A machine name xStunit does not know must stop the run rather than
        // quietly fall back to the default: the whole point of naming a target
        // is that the caller does not get one by accident.
        [Theory]
        [InlineData("arm64")]
        [InlineData("")]
        public void Run_UnknownTarget_ReturnsTwoAndNamesTheExpectedValues(string target)
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { ".", "--target", target }, output);

            Assert.Equal(2, exitCode);
            var text = output.ToString();
            Assert.Contains("--target", text);
            Assert.Contains("x86|x64", text);
        }

        [Fact]
        public void Run_TargetWithNoValue_ReturnsTwoAndSaysWhatItNeeds()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { "--target" }, output);

            Assert.Equal(2, exitCode);
            Assert.Contains("--target requires a value", output.ToString());
        }

        private static string NewFixtureDirectory()
        {
            var dir = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), "xstunit-cli-target-" + Guid.NewGuid()));
            File.WriteAllText(Path.Combine(dir.FullName, "FB_AddressWidthTests.TcPOU"), AddressWidthSuitePouXml);
            return dir.FullName;
        }

        private const string AddressWidthSuitePouXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <POU Name=""FB_AddressWidthTests"" Id=""{00000000-0000-0000-0000-0000000000e1}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_AddressWidthTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[PointerIsFourBytes();]]></ST>
    </Implementation>
    <Method Name=""PointerIsFourBytes"" Id=""{00000000-0000-0000-0000-0000000000e2}"">
      <Declaration><![CDATA[METHOD PRIVATE PointerIsFourBytes
VAR
	target : LREAL;
	address : POINTER TO LREAL;
END_VAR
]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('PointerIsFourBytes');

address := ADR(target);

AssertEquals_INT(Expected := 4,
                  Actual := SIZEOF(address),
                  Message := 'address width');

TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";
    }
}
