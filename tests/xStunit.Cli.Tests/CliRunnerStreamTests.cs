using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using xStunit.Cli;
using Xunit;

namespace xStunit.Cli.Tests
{
    // --stream is an opt-in NDJSON progress mode, so stdout must stay exactly
    // one JSON object per line: any stray text, or one event spread over two
    // lines, breaks every machine consumer reading it incrementally.
    //
    // The other half of the contract is that omitting the flag changes nothing
    // at all - callers that never pass --stream must see the byte-for-byte
    // output they always did.
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
            // The serializer options --format json uses write indented,
            // multi-line JSON; reusing them here would spread one event over
            // several lines and quietly break the NDJSON contract.
            var output = new StringWriter();

            CliRunner.Run(new[] { TestFixtures.FbCounterFixtureDir(), "--stream" }, output);

            var rawLines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(rawLines.Length >= 4);
            foreach (var rawLine in rawLines)
            {
                var trimmed = rawLine.TrimEnd('\r');
                Assert.False(string.IsNullOrWhiteSpace(trimmed));
                using var document = JsonDocument.Parse(trimmed);
                Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
            }
        }

        [Fact]
        public void Run_Stream_LoadErrorSuite_ReportsFailOutcomeWithoutAbortingStream()
        {
            // A throwing suite still owes the stream its own
            // suite-start/suite-result pair; skipping either would leave a
            // consumer waiting on an event that never arrives.
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
            // A run that never gets as far as discovery emits no suite events
            // at all - one "error" line, and the usual exit 2 for a usage
            // error.
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
            // Asserted against exact line counts and a literal last line rather
            // than a loose Contains, so any leak of streaming output into the
            // default path - one stray WriteLine, one inverted branch - fails
            // here.
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

        // The failing call sits in the suite's top-level body, outside any
        // TEST()/TEST_FINISHED() pair, so it faults before any test opens and
        // produces a genuine suite-level `error` - the same call inside a
        // TEST()-wrapped METHOD would be charged to that test instead.
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
