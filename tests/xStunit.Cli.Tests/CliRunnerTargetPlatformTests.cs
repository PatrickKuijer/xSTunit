using System;
using System.IO;
using xStunit.Cli;
using Xunit;

namespace xStunit.Cli.Tests
{
    // --target is how a run says which machine the code under test is built
    // for, and an address width is the only thing that answer changes. Every
    // case below runs a suite that asserts a width outright, so the flag's
    // effect is read off a passing or failing assertion rather than assumed.
    public class CliRunnerTargetPlatformTests
    {
        [Theory]
        [InlineData("x86", 4)]
        [InlineData("X86", 4)]
        [InlineData("x64", 8)]
        public void Run_Target_DecidesHowWideAnAddressIs(string target, int expectedAddressBytes)
        {
            var dir = NewFixtureDirectory(expectedAddressBytes);
            try
            {
                var output = new StringWriter();

                var exitCode = CliRunner.Run(new[] { dir, "--target", target }, output);

                Assert.Equal(0, exitCode);
                Assert.Contains("PASS", output.ToString());
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // A run that names no target must compute at x64, because that is what
        // a real TwinCAT 3 machine is. This is not a duplicate of the explicit
        // --target x64 case above and must not be deleted as one: that case
        // stays green for any default whatsoever, so only this one catches the
        // default quietly moving to x86. The negative half is what makes it a
        // pin rather than a smoke test - the same flagless command line
        // against a suite demanding 4-byte addresses has to FAIL.
        [Fact]
        public void Run_NoTargetFlag_ComputesAtTheSixtyFourBitAddressWidth()
        {
            var wide = NewFixtureDirectory(8);
            var narrow = NewFixtureDirectory(4);
            try
            {
                var wideOutput = new StringWriter();
                var narrowOutput = new StringWriter();

                Assert.Equal(0, CliRunner.Run(new[] { wide }, wideOutput));
                Assert.Contains("PASS", wideOutput.ToString());

                Assert.Equal(1, CliRunner.Run(new[] { narrow }, narrowOutput));
                Assert.Contains("FAIL", narrowOutput.ToString());
            }
            finally
            {
                Directory.Delete(wide, recursive: true);
                Directory.Delete(narrow, recursive: true);
            }
        }

        // The =value spelling reaches the same place as the two-token one; a
        // flag parsed by only one of its forms is a flag half the callers
        // cannot use.
        [Fact]
        public void Run_TargetWithAttachedValue_IsReadTheSameAsTheSeparateOne()
        {
            var dir = NewFixtureDirectory(4);
            try
            {
                var output = new StringWriter();

                Assert.Equal(0, CliRunner.Run(new[] { dir, "--target=x86" }, output));
                Assert.Contains("PASS", output.ToString());
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

        // A suite whose single test passes only when an address is exactly
        // expectedAddressBytes wide, so the run's exit code reports the width
        // the interpreter actually laid out at.
        private static string NewFixtureDirectory(int expectedAddressBytes)
        {
            var dir = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), "xstunit-cli-target-" + Guid.NewGuid()));
            File.WriteAllText(
                Path.Combine(dir.FullName, "FB_AddressWidthTests.TcPOU"),
                AddressWidthSuitePouXml(expectedAddressBytes));
            return dir.FullName;
        }

        private static string AddressWidthSuitePouXml(int expectedAddressBytes) =>
            @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <POU Name=""FB_AddressWidthTests"" Id=""{00000000-0000-0000-0000-0000000000e1}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_AddressWidthTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[AddressWidth();]]></ST>
    </Implementation>
    <Method Name=""AddressWidth"" Id=""{00000000-0000-0000-0000-0000000000e2}"">
      <Declaration><![CDATA[METHOD PRIVATE AddressWidth
VAR
	target : LREAL;
	address : POINTER TO LREAL;
END_VAR
]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('AddressWidth');

address := ADR(target);

AssertEquals_INT(Expected := " + expectedAddressBytes + @",
                  Actual := SIZEOF(address),
                  Message := 'address width');

TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";
    }
}
