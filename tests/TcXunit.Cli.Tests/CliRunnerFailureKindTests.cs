using System;
using System.IO;
using System.Text.Json;
using TcXunit.Cli;
using Xunit;

namespace TcXunit.Cli.Tests
{
    // TcXunit-3tx.1: every failure class used to land in the same untyped
    // `error` string, so a consuming agent could not tell "your PLC code is
    // wrong" from "the interpreter is behind". The two cases demand opposite
    // responses - rewrite the POU vs. stop and escalate - so the JSON now
    // carries a machine-readable discriminator alongside the (unchanged)
    // single-line error text.
    //
    // The fixtures here fault OUTSIDE any TEST()/TEST_FINISHED() bracket, which
    // is what keeps a failure at suite level at all: a fault inside a bracket
    // is charged to that test instead (TcXunit-3tx.3), and carries the same
    // vocabulary on the failure - see CliRunnerTestBlastRadiusTests.
    public class CliRunnerFailureKindTests : IDisposable
    {
        private readonly string _tempDir;

        public CliRunnerFailureKindTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TcXunitCliFailureKindFixture_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        // The acceptance repro: 'SEL(TRUE, 1, 3)' is valid IEC 61131-3 and
        // compiles in TwinCAT - the interpreter simply doesn't implement it
        // yet, which must never be reported as a defect in the code under test.
        [Fact]
        public void Run_UnsupportedNativeCall_ReportsUnsupportedConstructKindAndNamesTheConstruct()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_SelSuiteTests.TcPOU"), SelSuiteXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            var suite = FirstSuite(output.ToString());
            Assert.Equal("unsupported-construct", suite.GetProperty("errorKind").GetString());
            Assert.Equal("SEL", suite.GetProperty("errorConstruct").GetString());
        }

        // Same run, opposite conclusion: the error string existing consumers
        // (the VSIX results tree) read is untouched by the new fields.
        [Fact]
        public void Run_UnsupportedNativeCall_PreservesSingleLineErrorText()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_SelSuiteTests.TcPOU"), SelSuiteXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            var error = FirstSuite(output.ToString()).GetProperty("error").GetString();
            Assert.Contains("TcUnit native call 'SEL' isn't supported yet", error);
        }

        // The other acceptance repro: a call to a method that genuinely does
        // not exist is a real defect in the PLC code, and stays distinguishable
        // from the unsupported-construct case above.
        [Fact]
        public void Run_MethodNotFound_ReportsPlcFaultKind()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_MissingMethodTests.TcPOU"), MethodNotFoundSuiteXml);
            File.WriteAllText(Path.Combine(_tempDir, "FB_KindHelper.TcPOU"), HelperXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            var suite = FirstSuite(output.ToString());
            Assert.Equal("plc-fault", suite.GetProperty("errorKind").GetString());
            Assert.Equal(JsonValueKind.Null, suite.GetProperty("errorConstruct").ValueKind);
        }

        // TcXunit-2o9.1: the bug this ticket fixes. An unqualified call from a
        // suite body used to be classified as unsupported-construct
        // unconditionally - even a plain typo of one of the suite's own
        // methods, which is a real, fixable defect and must classify the same
        // as the qualified-call case above (plc-fault), not "STOP, escalate".
        [Fact]
        public void Run_MisspelledUnqualifiedSuiteCall_ReportsPlcFaultKind()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_TypoSuiteTests.TcPOU"), UnqualifiedTypoSuiteXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            var suite = FirstSuite(output.ToString());
            Assert.Equal("plc-fault", suite.GetProperty("errorKind").GetString());
            Assert.Equal(JsonValueKind.Null, suite.GetProperty("errorConstruct").ValueKind);
        }

        // The inverted failure mode, and the one that matters most: a run-time
        // type error in the code under test is thrown as a plain
        // NotSupportedException, the same base type the interpreter's own
        // grow-on-demand gaps use. Classifying on that base type would tell an
        // agent to stop and escalate over its own bug, which is exactly as
        // useless as not discriminating at all.
        [Fact]
        public void Run_OperandTypeErrorInTheCodeUnderTest_IsPlcFaultNotUnsupportedConstruct()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_BadOperandTests.TcPOU"), OperandTypeErrorSuiteXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            var suite = FirstSuite(output.ToString());
            Assert.Contains("is not supported between", suite.GetProperty("error").GetString());
            Assert.Equal("plc-fault", suite.GetProperty("errorKind").GetString());
        }

        // A passing suite carries no kind at all, rather than a sentinel a
        // consumer would have to special-case.
        [Fact]
        public void Run_PassingSuite_HasNoErrorKind()
        {
            var output = new StringWriter();

            CliRunner.Run(new[] { TestFixtures.FbCounterFixtureDir(), "--format", "json" }, output);

            var suite = FirstSuite(output.ToString());
            Assert.Equal(JsonValueKind.Null, suite.GetProperty("errorKind").ValueKind);
        }

        // Run-level discovery failures use the same vocabulary, so a consumer
        // reads `kind` the same way whether the run died before any suite ran
        // or one suite failed inside it.
        [Fact]
        public void Run_NoSuitesFound_ReportsLoadErrorKindOnTheRunLevelError()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            Assert.Equal(2, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Equal("load-error", document.RootElement.GetProperty("kind").GetString());
        }

        private static JsonElement FirstSuite(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetProperty("suites")[0].Clone();
        }

        private const string SelSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_SelSuiteTests"" Id=""{00000000-0000-0000-0000-0000000000b0}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_SelSuiteTests EXTENDS TcUnit.FB_TestSuite
VAR
	value : INT;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[value := SEL(TRUE, 1, 3);]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        // Called on a helper FB rather than bare: an UNQUALIFIED call a suite
        // fails to resolve is deliberately reported as an unwired TcUnit API
        // (TcXunit-6k2), so it would classify as unsupported-construct. A
        // qualified call to a method a real POU genuinely doesn't have is the
        // unambiguous plc-fault.
        private const string MethodNotFoundSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_MissingMethodTests"" Id=""{00000000-0000-0000-0000-0000000000b2}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_MissingMethodTests EXTENDS TcUnit.FB_TestSuite
VAR
	helper : FB_KindHelper;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[helper.NoSuchMethod();]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        // 'sText < 1' - comparing a STRING against an INT has no IEC 61131-3
        // semantics; the engine guards it with a descriptive
        // NotSupportedException.
        private const string OperandTypeErrorSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_BadOperandTests"" Id=""{00000000-0000-0000-0000-0000000000b4}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_BadOperandTests EXTENDS TcUnit.FB_TestSuite
VAR
	sText : STRING;
	bResult : BOOL;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[bResult := sText < 1;]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        // Bare/unqualified (unlike MethodNotFoundSuiteXml's qualified
        // 'helper.NoSuchMethod()'): names nothing the ancestry walk, the
        // TcUnit stub (Assert*/TEST*/IS_TEST*), a POU or a native function
        // resolves - a plain typo, not an unwired TcUnit API (TcXunit-2o9.1).
        private const string UnqualifiedTypoSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_TypoSuiteTests"" Id=""{00000000-0000-0000-0000-0000000000b5}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_TypoSuiteTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[CounterStartsAtZeroo();]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        private const string HelperXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_KindHelper"" Id=""{00000000-0000-0000-0000-0000000000b3}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_KindHelper]]></Declaration>
    <Implementation>
      <ST><![CDATA[]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";
    }
}
