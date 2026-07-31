using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using xStunit.Cli;
using xStunit.Runner;
using Xunit;

namespace xStunit.Cli.Tests
{
    // `kind` is the machine-readable discriminator that lets a consumer tell
    // "your PLC code is wrong" from "the interpreter is behind" - two cases
    // demanding opposite responses (rewrite the POU vs. stop and escalate) that
    // are indistinguishable in the prose error string alone.
    //
    // Except where noted, the fixtures here fault OUTSIDE any
    // TEST()/TEST_FINISHED() bracket, which is what keeps the failure at suite
    // level: a fault inside a bracket is charged to that test instead, and
    // carries the same vocabulary - see CliRunnerTestBlastRadiusTests.
    public class CliRunnerFailureKindTests : IDisposable
    {
        private readonly string _tempDir;

        public CliRunnerFailureKindTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TcXunitCliFailureKindFixture_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        // 'SEL(TRUE, 1, 3)' is valid IEC 61131-3 that the interpreter simply
        // doesn't implement yet, which must never be reported as a defect in
        // the code under test.
        [Fact]
        public void Run_UnsupportedNativeCall_ReportsUnsupportedConstructKindAndNamesTheConstruct()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_SelSuiteTests.TcPOU"), SelSuiteXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            var suite = FirstSuite(output.ToString());
            Assert.Equal("unsupported-construct", suite.GetProperty("kind").GetString());
            Assert.Equal("SEL", suite.GetProperty("construct").GetString());
        }

        // One pair of keys at every level: the earlier suite-only
        // `errorKind`/`errorConstruct` spellings are gone, not aliased.
        [Fact]
        public void Run_SuiteError_UsesTheSameKindAndConstructKeysAsEveryOtherLevel()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_SelSuiteTests.TcPOU"), SelSuiteXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            var suite = FirstSuite(output.ToString());
            Assert.False(suite.TryGetProperty("errorKind", out _));
            Assert.False(suite.TryGetProperty("errorConstruct", out _));
        }

        // The consumer is usually a model choosing its next edit from this one
        // object, and its default move on any failure is to rewrite the POU -
        // exactly the wrong response to an interpreter gap, so the message has
        // to say so outright.
        [Fact]
        public void Run_UnsupportedNativeCall_ErrorMessageCarriesTheStopAndEscalateGuidance()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_SelSuiteTests.TcPOU"), SelSuiteXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            var error = FirstSuite(output.ToString()).GetProperty("error").GetString();
            Assert.Contains("STOP", error);
            Assert.Contains("Never rewrite the POU", error);
        }

        // The structured fields are additive: the error string existing
        // consumers render (the VSIX results tree) is untouched by them.
        [Fact]
        public void Run_UnsupportedNativeCall_PreservesSingleLineErrorText()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_SelSuiteTests.TcPOU"), SelSuiteXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            var error = FirstSuite(output.ToString()).GetProperty("error").GetString();
            Assert.Contains("TcUnit native call 'SEL' isn't supported yet", error);
        }

        [Fact]
        public void Run_MethodNotFound_ReportsPlcFaultKind()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_MissingMethodTests.TcPOU"), MethodNotFoundSuiteXml);
            File.WriteAllText(Path.Combine(_tempDir, "FB_KindHelper.TcPOU"), HelperXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            var suite = FirstSuite(output.ToString());
            Assert.Equal("plc-fault", suite.GetProperty("kind").GetString());
            Assert.Equal(JsonValueKind.Null, suite.GetProperty("construct").ValueKind);
        }

        // An unresolved unqualified call from a suite body is reported as an
        // unwired TcUnit API, but a plain typo of one of the suite's own
        // methods is a real, fixable defect - so it has to classify like the
        // qualified case above, not as "STOP, escalate".
        [Fact]
        public void Run_MisspelledUnqualifiedSuiteCall_ReportsPlcFaultKind()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_TypoSuiteTests.TcPOU"), UnqualifiedTypoSuiteXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            var suite = FirstSuite(output.ToString());
            Assert.Equal("plc-fault", suite.GetProperty("kind").GetString());
            Assert.Equal(JsonValueKind.Null, suite.GetProperty("construct").ValueKind);
        }

        // A run-time type error in the code under test is thrown as a plain
        // NotSupportedException - the same base type the interpreter's own
        // grow-on-demand gaps use. Classifying on that base type would tell an
        // agent to stop and escalate over its own bug.
        [Fact]
        public void Run_OperandTypeErrorInTheCodeUnderTest_IsPlcFaultNotUnsupportedConstruct()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_BadOperandTests.TcPOU"), OperandTypeErrorSuiteXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            var suite = FirstSuite(output.ToString());
            Assert.Contains("is not supported between", suite.GetProperty("error").GetString());
            Assert.Equal("plc-fault", suite.GetProperty("kind").GetString());
        }

        [Fact]
        public void Run_PassingSuite_HasNoErrorKind()
        {
            var output = new StringWriter();

            CliRunner.Run(new[] { TestFixtures.FbCounterFixtureDir(), "--format", "json" }, output);

            var suite = FirstSuite(output.ToString());
            Assert.Equal(JsonValueKind.Null, suite.GetProperty("kind").ValueKind);
        }

        [Fact]
        public void Run_NoSuitesFound_ReportsLoadErrorKindOnTheRunLevelError()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            Assert.Equal(2, exitCode);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Equal("load-error", document.RootElement.GetProperty("kind").GetString());
        }

        // ST the front end can't read is neither of the two neighbouring kinds:
        // plc-fault says "your code is broken", which we do not know, and
        // load-error says "nothing ran", which is the container's story rather
        // than this body's.
        [Fact]
        public void Run_UnreadableSt_ReportsParseErrorKind()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_UnreadableTests.TcPOU"), UnreadableSuiteXml);
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            var suite = FirstSuite(output.ToString());
            Assert.Equal(1, exitCode);
            Assert.Equal("parse-error", suite.GetProperty("kind").GetString());
        }

        [Fact]
        public void Run_UnreadableSt_NamesTheOffendingTokenInTheSharedConstructField()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_UnreadableTests.TcPOU"), UnreadableSuiteXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            Assert.Equal("@", FirstSuite(output.ToString()).GetProperty("construct").GetString());
        }

        // Both possible causes have to be named: whether the body is beyond the
        // supported subset or simply invalid ST is something we cannot
        // distinguish, so the message must not assert either one.
        [Fact]
        public void Run_UnreadableSt_ErrorMessageCitesTheLineAndBothPossibleCauses()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_UnreadableTests.TcPOU"), UnreadableSuiteXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            var error = FirstSuite(output.ToString()).GetProperty("error").GetString();
            Assert.Contains("could not read this body at line 2", error);
            Assert.Contains("beyond TcXunit's subset, or it is invalid ST", error);
            Assert.Contains("STOP and escalate", error);
        }

        // A parse-error is a failure of ONE BODY inside a suite that loaded, so
        // its blast radius is the test that body was serving - which is exactly
        // why it cannot be a load-error, whose contract is "nothing ran".
        [Fact]
        public void Run_UnreadableStInOneTest_SiblingTestsInTheSameSuiteStillReportTheirVerdicts()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_ParseIsolationTests.TcPOU"), ParseErrorIsolationSuiteXml);
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            using var document = JsonDocument.Parse(output.ToString());
            var root = document.RootElement;
            var suite = root.GetProperty("suites")[0];

            // A test failed inside a suite that LOADED, so exit 1 - not the 2
            // reserved for usage/discovery errors that produced no results.
            Assert.Equal(1, exitCode);
            Assert.Equal(1, root.GetProperty("passed").GetInt32());
            Assert.Equal(1, root.GetProperty("failed").GetInt32());
            Assert.Equal(JsonValueKind.Null, suite.GetProperty("error").ValueKind);

            var tests = suite.GetProperty("tests");
            Assert.Equal(2, tests.GetArrayLength());
            var healthy = tests.EnumerateArray().Single(t => t.GetProperty("name").GetString() == "Healthy");
            Assert.True(healthy.GetProperty("passed").GetBoolean());

            var failure = tests.EnumerateArray()
                .Single(t => !t.GetProperty("passed").GetBoolean())
                .GetProperty("failures")[0];
            Assert.Equal("parse-error", failure.GetProperty("kind").GetString());
            Assert.Equal("@", failure.GetProperty("construct").GetString());

            Assert.Equal(2, failure.GetProperty("bodyLine").GetInt32());
        }

        // Only FB_TestSuite.Fail() has a verbatim-message contract that exempts
        // it from carrying guidance, and it is the only path filling
        // expected/actual. Keying that carve-out on the assertion KIND instead
        // would exempt every assertion failure and make the assertion guidance
        // dead code - a convergence failure compares nothing, so it must still
        // carry it.
        //
        // This fixture faults INSIDE a TEST()/TEST_FINISHED() bracket, which is
        // what routes it through the per-test failure path rather than the
        // suite-level one the rest of this class uses.
        [Fact]
        public void Run_ConvergenceAssertion_CarriesTheAssertionGuidanceEvenThoughItsKindIsAssertion()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_ConvergenceTests.TcPOU"), ConvergenceSuiteXml);
            File.WriteAllText(Path.Combine(_tempDir, "FB_ConvergenceMaster.TcPOU"), ConvergenceMasterXml);
            File.WriteAllText(Path.Combine(_tempDir, "FB_ConvergenceRamp.TcPOU"), ConvergenceRampXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            using var document = JsonDocument.Parse(output.ToString());
            var failure = document.RootElement
                .GetProperty("suites")[0]
                .GetProperty("tests")
                .EnumerateArray()
                .Single(t => !t.GetProperty("passed").GetBoolean())
                .GetProperty("failures")[0];

            Assert.Equal("assertion", failure.GetProperty("kind").GetString());
            // No expected/actual pair: nothing was compared by a TcUnit assert,
            // so there is no verbatim formatter line to protect.
            Assert.Equal(JsonValueKind.Null, failure.GetProperty("expected").ValueKind);
            var message = failure.GetProperty("message").GetString();
            Assert.Contains("AssertConverges: fields did not converge", message);
            Assert.Contains(FailureKind.Guidance(FailureKind.Assertion), message);
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

        // Qualified on a helper FB rather than bare: an unqualified call is
        // reported as an unwired TcUnit API and would classify as
        // unsupported-construct, whereas a qualified call to a method a real
        // POU genuinely lacks is the unambiguous plc-fault.
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

        // Bare/unqualified, and names nothing the ancestry walk, the TcUnit stub
        // (Assert*/TEST*/IS_TEST*), a POU or a native function resolves - a
        // plain typo rather than an unwired TcUnit API.
        private const string UnqualifiedTypoSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_TypoSuiteTests"" Id=""{00000000-0000-0000-0000-0000000000b5}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_TypoSuiteTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[CounterStartsAtZeroo();]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        // '@' is not a character the ST lexer has any meaning for, in this or
        // any future subset - so this fixture stays a parse-error even as the
        // grammar grows, unlike a construct the parser could later learn to
        // recognize by name (which would be PROMOTED to unsupported-construct).
        private const string UnreadableSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_UnreadableTests"" Id=""{00000000-0000-0000-0000-0000000000b6}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_UnreadableTests EXTENDS TcUnit.FB_TestSuite
VAR
	n : INT;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[n := 1;
n := @ 2;]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        // The unreadable body sits one call BELOW the TEST()/TEST_FINISHED()
        // bracket, which is what leaves a test open for the fault to be charged
        // to - the containment path a run-time fault takes, reached by a parse
        // failure instead.
        private const string ParseErrorIsolationSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_ParseIsolationTests"" Id=""{00000000-0000-0000-0000-0000000000b7}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_ParseIsolationTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[UsesUnreadableSt();
Healthy();]]></ST>
    </Implementation>
    <Method Name=""UsesUnreadableSt"" Id=""{00000000-0000-0000-0000-0000000000b8}"">
      <Declaration><![CDATA[METHOD PRIVATE UsesUnreadableSt]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('UsesUnreadableSt');
Unreadable();
TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
    <Method Name=""Unreadable"" Id=""{00000000-0000-0000-0000-0000000000b9}"">
      <Declaration><![CDATA[METHOD PRIVATE Unreadable
VAR
	n : INT;
END_VAR]]></Declaration>
      <Implementation>
        <ST><![CDATA[n := 1;
n := @ 2;]]></ST>
      </Implementation>
    </Method>
    <Method Name=""Healthy"" Id=""{00000000-0000-0000-0000-0000000000ba}"">
      <Declaration><![CDATA[METHOD PRIVATE Healthy]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('Healthy');
AssertEquals_INT(Expected := 1, Actual := 1, Message := '');
TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";

        // The master holds a value the ramping proxy never reaches within the
        // cycle budget, so AssertConverges throws - an `assertion`-kind failure
        // raised nowhere near FB_TestSuite.Fail().
        private const string ConvergenceSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_ConvergenceTests"" Id=""{00000000-0000-0000-0000-0000000000c0}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_ConvergenceTests EXTENDS TcUnit.FB_TestSuite
VAR
	master : FB_ConvergenceMaster;
	proxy : FB_ConvergenceRamp;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[NeverConverges();]]></ST>
    </Implementation>
    <Method Name=""NeverConverges"" Id=""{00000000-0000-0000-0000-0000000000c1}"">
      <Declaration><![CDATA[METHOD PRIVATE NeverConverges]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('NeverConverges');
master.Value := 99;
AssertConverges(master, proxy, ['Value'], 2);
TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";

        private const string ConvergenceMasterXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_ConvergenceMaster"" Id=""{00000000-0000-0000-0000-0000000000c2}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_ConvergenceMaster
VAR
	Value : INT;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        private const string ConvergenceRampXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_ConvergenceRamp"" Id=""{00000000-0000-0000-0000-0000000000c3}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_ConvergenceRamp
VAR
	Value : INT;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[Value := Value + 1;]]></ST>
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
