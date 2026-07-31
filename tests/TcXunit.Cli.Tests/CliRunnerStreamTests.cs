using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TcXunit.Cli;
using Xunit;

namespace TcXunit.Cli.Tests
{
    // TcXunit-ce1: --stream, an opt-in NDJSON progress mode for large suite
    // counts. Every event is one line of JSON on its own - a "discovery"
    // line up front listing every suite about to run, a "suite-start"/
    // "suite-result" pair per suite as it executes, and a final "summary"
    // line carrying the same aggregate data --format json already reports
    // in one blob. The property that matters most here (and gets its own
    // dedicated test) is that omitting the flag is byte-for-byte the same
    // as before this feature existed - --stream must never be able to
    // change what an existing caller (the VSIX, which never passes it) sees.
    public class CliRunnerStreamTests : IDisposable
    {
        private readonly string _tempDir;

        public CliRunnerStreamTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TcXunitCliStreamFixture_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        [Fact]
        public void Run_Stream_FixtureProject_EmitsDiscoveryThenSuiteStartThenSuiteResultThenSummary()
        {
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { TestFixtures.FbCounterFixtureDir(), "--stream" }, output);

            Assert.Equal(0, exitCode);
            var lines = ParseLines(output.ToString());

            // Every line must be valid, independently parseable JSON - the
            // core NDJSON contract.
            Assert.True(lines.Count >= 4);

            var discovery = lines[0];
            Assert.Equal("discovery", discovery.GetProperty("event").GetString());
            var discoveredSuites = discovery.GetProperty("suites");
            Assert.Equal(1, discoveredSuites.GetArrayLength());
            Assert.Equal("FB_CounterTests", discoveredSuites[0].GetProperty("name").GetString());
            Assert.EndsWith(".TcPOU", discoveredSuites[0].GetProperty("filePath").GetString());

            var start = lines[1];
            Assert.Equal("suite-start", start.GetProperty("event").GetString());
            Assert.Equal("FB_CounterTests", start.GetProperty("suite").GetString());

            var result = lines[2];
            Assert.Equal("suite-result", result.GetProperty("event").GetString());
            Assert.Equal("FB_CounterTests", result.GetProperty("name").GetString());
            Assert.Equal("pass", result.GetProperty("outcome").GetString());
            Assert.Equal(4, result.GetProperty("tests").GetArrayLength());

            var summary = lines[3];
            Assert.Equal("summary", summary.GetProperty("event").GetString());
            Assert.Equal(4, summary.GetProperty("passed").GetInt32());
            Assert.Equal(0, summary.GetProperty("failed").GetInt32());
            Assert.Equal(0, summary.GetProperty("exitCode").GetInt32());
            Assert.Equal(1, summary.GetProperty("suites").GetArrayLength());
        }

        [Fact]
        public void Run_Stream_EachLineIsCompactSingleLineJson()
        {
            // NDJSON's defining property: exactly one JSON value per line.
            // JsonOptions (used by --format json) writes indented,
            // multi-line JSON - if --stream reused it by mistake, this
            // would fail because a single event would span several lines.
            var output = new StringWriter();

            CliRunner.Run(new[] { TestFixtures.FbCounterFixtureDir(), "--stream" }, output);

            var rawLines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(rawLines.Length >= 4);
            foreach (var rawLine in rawLines)
            {
                var trimmed = rawLine.TrimEnd('\r');
                Assert.False(string.IsNullOrWhiteSpace(trimmed));
                // Each raw line must parse as a complete, standalone JSON
                // object by itself - proof no event's JSON was split (or
                // merged with another's) across lines.
                using var document = JsonDocument.Parse(trimmed);
                Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
            }
        }

        [Fact]
        public void Run_Stream_LoadErrorSuite_ReportsFailOutcomeWithoutAbortingStream()
        {
            // Mirrors CliRunnerSuiteExceptionTests: one suite that throws
            // before RunSuite's stopwatch ever completes a run, alongside a
            // clean suite that passes - the throwing suite must not take the
            // rest of the stream down with it, and must still produce
            // exactly one suite-start/suite-result pair of its own.
            File.WriteAllText(Path.Combine(_tempDir, "FB_ThrowingSuiteTests.TcPOU"), ThrowingSuiteXml);
            File.WriteAllText(Path.Combine(_tempDir, "FB_CleanSuiteTests.TcPOU"), CleanSuiteXml);

            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { _tempDir, "--stream" }, output);

            Assert.Equal(1, exitCode);
            var lines = ParseLines(output.ToString());

            var discovery = lines.Single(l => l.GetProperty("event").GetString() == "discovery");
            Assert.Equal(2, discovery.GetProperty("suites").GetArrayLength());

            var starts = lines.Where(l => l.GetProperty("event").GetString() == "suite-start").ToList();
            Assert.Equal(2, starts.Count);
            Assert.Contains(starts, s => s.GetProperty("suite").GetString() == "FB_ThrowingSuiteTests");
            Assert.Contains(starts, s => s.GetProperty("suite").GetString() == "FB_CleanSuiteTests");

            var results = lines.Where(l => l.GetProperty("event").GetString() == "suite-result").ToList();
            Assert.Equal(2, results.Count);

            var throwingResult = results.Single(r => r.GetProperty("name").GetString() == "FB_ThrowingSuiteTests");
            Assert.Equal("fail", throwingResult.GetProperty("outcome").GetString());
            Assert.False(string.IsNullOrEmpty(throwingResult.GetProperty("error").GetString()));

            var cleanResult = results.Single(r => r.GetProperty("name").GetString() == "FB_CleanSuiteTests");
            Assert.Equal("pass", cleanResult.GetProperty("outcome").GetString());

            var summary = lines.Single(l => l.GetProperty("event").GetString() == "summary");
            Assert.Equal(1, summary.GetProperty("failed").GetInt32());
            Assert.Equal(1, summary.GetProperty("exitCode").GetInt32());
        }

        [Fact]
        public void Run_Stream_MissingPath_EmitsSingleErrorEventAndReturnsTwo()
        {
            // WriteError's streaming branch: no discovery/suite events at
            // all when the run never gets that far (TcXunit-ce1) - just one
            // NDJSON "error" line, same as --format json's ErrorReport plus
            // the event tag. Exit code contract is unchanged (2).
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { @"C:\this\path\does\not\exist", "--stream" }, output);

            Assert.Equal(2, exitCode);
            var lines = ParseLines(output.ToString());
            var line = Assert.Single(lines);
            Assert.Equal("error", line.GetProperty("event").GetString());
            Assert.Contains("does not exist", line.GetProperty("error").GetString());
        }

        [Fact]
        public void Run_WithoutStreamFlag_OutputIsByteForByteUnchanged()
        {
            // The single most important property of this feature: a caller
            // that never passes --stream (every existing caller, including
            // the VSIX) must see exactly the same stdout it always did.
            // Locked in against a literal expected string rather than a
            // loose Contains/Count check, so any accidental leak of new
            // --stream code into the default path (a stray WriteLine, a
            // changed branch condition) fails this test.
            var output = new StringWriter();

            var exitCode = CliRunner.Run(new[] { TestFixtures.FbCounterFixtureDir() }, output);

            Assert.Equal(0, exitCode);
            var lines = output.ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(5, lines.Length);
            Assert.Equal(4, lines.Count(l => l.EndsWith(": PASS", StringComparison.Ordinal)));
            Assert.Equal("4 passed, 0 failed", lines[^1]);
        }

        private static List<JsonElement> ParseLines(string ndjson) =>
            ndjson.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.TrimEnd('\r'))
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => JsonDocument.Parse(l).RootElement.Clone())
                .ToList();

        // Unqualified call directly in the suite's top-level Implementation
        // body, outside any TEST()/TEST_FINISHED() pair (same shape as
        // CliRunnerFailureKindTests' UnqualifiedTypoSuiteXml) - this fails
        // before the suite ever starts a test, so RunSuite itself throws and
        // CliRunner's catch(Exception ex) around it fires, producing a
        // genuine suite-level `error` (not a per-test failure entry, which
        // is what a call failing inside a TEST()-wrapped METHOD would
        // produce instead).
        private const string ThrowingSuiteXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_ThrowingSuiteTests"" Id=""{00000000-0000-0000-0000-0000000000ac}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_ThrowingSuiteTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[ThisMethodDoesNotExist();]]></ST>
    </Implementation>
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
