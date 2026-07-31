using System;
using System.IO;
using System.Linq;
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
            Assert.Equal("unsupported-construct", suite.GetProperty("kind").GetString());
            Assert.Equal("SEL", suite.GetProperty("construct").GetString());
        }

        // TcXunit-229.15 (BREAKING): suites[] used to spell these two
        // `errorKind`/`errorConstruct` while the top-level error and every
        // per-test failure spelled them `kind`/`construct`. One vocabulary, one
        // pair of keys, at every level - the old keys are gone, not aliased.
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

        // TcXunit-229.15: the message is the whole brief. TcXunit's consumer is
        // usually a model choosing its next edit from one JSON object, and
        // "TcUnit native call 'SEL' isn't supported yet" states a fact without
        // answering the only question that matters - which for THIS kind is the
        // one an agent gets catastrophically wrong by default.
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
            Assert.Equal("plc-fault", suite.GetProperty("kind").GetString());
            Assert.Equal(JsonValueKind.Null, suite.GetProperty("construct").ValueKind);
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
            Assert.Equal("plc-fault", suite.GetProperty("kind").GetString());
            Assert.Equal(JsonValueKind.Null, suite.GetProperty("construct").ValueKind);
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
            Assert.Equal("plc-fault", suite.GetProperty("kind").GetString());
        }

        // A passing suite carries no kind at all, rather than a sentinel a
        // consumer would have to special-case.
        [Fact]
        public void Run_PassingSuite_HasNoErrorKind()
        {
            var output = new StringWriter();

            CliRunner.Run(new[] { TestFixtures.FbCounterFixtureDir(), "--format", "json" }, output);

            var suite = FirstSuite(output.ToString());
            Assert.Equal(JsonValueKind.Null, suite.GetProperty("kind").ValueKind);
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

        // TcXunit-229.15: the fifth kind. ST the front end can't read used to
        // classify as plc-fault or load-error depending on nothing more than
        // whether an ExecuteBody frame happened to stamp a location on the way
        // out - and both answers were wrong. plc-fault says "your code is
        // broken", which TcXunit does not know; load-error says "nothing ran",
        // which is the container's story, not this body's.
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

        // The offending token rides in `construct` - the field
        // unsupported-construct already uses - so a consumer reads one pair of
        // keys for every kind rather than learning a parse-error-only field.
        [Fact]
        public void Run_UnreadableSt_NamesTheOffendingTokenInTheSharedConstructField()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_UnreadableTests.TcPOU"), UnreadableSuiteXml);
            var output = new StringWriter();

            CliRunner.Run(new[] { _tempDir, "--format", "json" }, output);

            Assert.Equal("@", FirstSuite(output.ToString()).GetProperty("construct").GetString());
        }

        // The message form TcXunit-229.9 settled on: say the body could not be
        // READ, cite the line, and name BOTH possible causes rather than
        // asserting one TcXunit cannot actually distinguish.
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

        // The acceptance criterion for the kind's own boundary: a parse-error
        // is a failure of ONE BODY inside a suite that loaded, not a failure of
        // the container - so its blast radius is the test that body was serving
        // and the suite's other tests still report their own verdicts. This is
        // exactly why it cannot be a load-error, whose contract is "nothing
        // ran".
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

            // The offending line, in the field an agent opens the source with -
            // reusing bodyLine rather than adding a parse-error-only position
            // field (TcXunit-229.15).
            Assert.Equal(2, failure.GetProperty("bodyLine").GetInt32());
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
        // bracket, which is what leaves a test open to charge the fault to -
        // the same containment TcXunit-3tx.3 built, reached by a parse failure
        // instead of a run-time one.
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
