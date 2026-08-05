using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using xStunit.Interpreter;
using xStunit.Parser;
using xStunit.Runner;
using xStunit.Runner.TcUnitStub;
using Xunit;

namespace xStunit.Cli.Tests
{
    // One writer per output format, driven event by event without a run: no
    // POUs on disk, no engine, no CliRunner.Run. What these pin is that the
    // choice of format changes only the bytes - the tally, the exit code and
    // the order events arrive in are the same for all three.
    //
    // The NDJSON assertions below are the emitting half of the published
    // contract the VSIX reads out-of-process, so a renamed property is a
    // silently null field there and never a compile error here.
    public class RunReportWriterTests
    {
        private const string SuitePath = "/POUs/FB_CounterTests.TcPOU";

        private static readonly IReadOnlyList<SkippedFile> NoSkips = new SkippedFile[0];

        private static readonly IReadOnlyDictionary<string, string> FilePaths =
            new Dictionary<string, string> { ["FB_CounterTests"] = SuitePath };

        private static TestCaseResult Passing() =>
            new TestCaseResult("CounterResets", new AssertionFailure[0], 2);

        private static TestCaseResult Failing() =>
            new TestCaseResult(
                "CounterAdds",
                new[]
                {
                    new AssertionFailure(
                        "FAILED TEST 'CounterAdds', EXP: 3, ACT: 2, MSG: sum",
                        "AssertEquals_INT",
                        "3",
                        "2",
                        "sum",
                        new AssertSite("FB_CounterTests", "CounterAdds", 42, 7))
                },
                5);

        private static string[] Lines(StringWriter output) =>
            output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

        private static string[] KeysOf(JsonElement element) =>
            element.EnumerateObject().Select(p => p.Name).ToArray();

        private static string EventOf(string line)
        {
            using var parsed = JsonDocument.Parse(line);
            return parsed.RootElement.GetProperty("event").GetString();
        }

        // A whole run of one suite with one passing and one failing test, the
        // shortest sequence that exercises every event a full run emits.
        private static int RunOneSuite(RunReportWriter writer)
        {
            writer.Discovery(new[] { "FB_CounterTests" }, FilePaths);
            writer.SuiteStart("FB_CounterTests");
            var tests = writer.ReportTests(new[] { Passing(), Failing() });
            writer.SuiteCompleted("FB_CounterTests", SuitePath, tests, 11);
            return writer.Summary(NoSkips, coverage: null);
        }

        [Fact]
        public void Ndjson_OfAWholeRun_IsOneJsonObjectPerLineInEventOrder()
        {
            var output = new StringWriter();

            var exitCode = RunOneSuite(RunReportWriter.Create(output, asJson: false, streaming: true));

            Assert.Equal(1, exitCode);
            var lines = Lines(output);
            // NDJSON's whole contract: a consumer reading line by line gets one
            // complete event per line, so an embedded newline splits one event
            // into two unparseable halves.
            foreach (var line in lines)
            {
                using var parsed = JsonDocument.Parse(line);
                Assert.Equal(JsonValueKind.Object, parsed.RootElement.ValueKind);
            }

            Assert.Equal(
                new[] { "discovery", "suite-start", "suite-result", "summary" },
                lines.Select(EventOf).ToArray());
        }

        [Fact]
        public void Ndjson_DiscoveryAndSuiteStart_CarryTheKeysAConsumerBuildsItsWaitingListFrom()
        {
            var output = new StringWriter();
            var writer = RunReportWriter.Create(output, asJson: false, streaming: true);

            writer.Discovery(new[] { "FB_CounterTests", "FB_UnknownTests" }, FilePaths);
            writer.SuiteStart("FB_CounterTests");

            var lines = Lines(output);
            using var discovery = JsonDocument.Parse(lines[0]);
            Assert.Equal(new[] { "event", "suites" }, KeysOf(discovery.RootElement));
            var discovered = discovery.RootElement.GetProperty("suites").EnumerateArray().ToList();
            Assert.Equal(new[] { "name", "filePath" }, KeysOf(discovered[0]));
            Assert.Equal(SuitePath, discovered[0].GetProperty("filePath").GetString());
            // A suite whose file path was never recorded still gets an entry,
            // or the consumer's waiting list comes up short.
            Assert.Equal(JsonValueKind.Null, discovered[1].GetProperty("filePath").ValueKind);

            using var start = JsonDocument.Parse(lines[1]);
            Assert.Equal(new[] { "event", "suite" }, KeysOf(start.RootElement));
            Assert.Equal("FB_CounterTests", start.RootElement.GetProperty("suite").GetString());
        }

        [Fact]
        public void Ndjson_SuiteResultAndSummary_CarryTheFullWireShape()
        {
            var output = new StringWriter();

            RunOneSuite(RunReportWriter.Create(output, asJson: false, streaming: true));

            var lines = Lines(output);
            using var suite = JsonDocument.Parse(lines[2]);
            Assert.Equal(
                new[]
                {
                    "event", "outcome", "name", "filePath", "error", "kind", "construct",
                    "tests", "durationMs", "fileLine", "callStack"
                },
                KeysOf(suite.RootElement));
            Assert.Equal("fail", suite.RootElement.GetProperty("outcome").GetString());
            Assert.Equal(11, suite.RootElement.GetProperty("durationMs").GetInt64());

            using var summary = JsonDocument.Parse(lines[3]);
            // No "coverage": omitted when not requested, because an empty list
            // already means something else - that every POU is uncovered.
            Assert.Equal(
                new[] { "event", "suites", "passed", "failed", "exitCode", "skipped" },
                KeysOf(summary.RootElement));
            Assert.Equal(1, summary.RootElement.GetProperty("passed").GetInt32());
            Assert.Equal(1, summary.RootElement.GetProperty("failed").GetInt32());
            Assert.Equal(1, summary.RootElement.GetProperty("exitCode").GetInt32());
            // The suite reported on its own line is also carried in the summary,
            // so a consumer that joined late still sees the whole run.
            Assert.Single(summary.RootElement.GetProperty("suites").EnumerateArray());
        }

        [Fact]
        public void Ndjson_AFaultedSuite_StillEmitsExactlyOneSuiteResultLine()
        {
            var output = new StringWriter();
            var writer = RunReportWriter.Create(output, asJson: false, streaming: true);
            var fault = new PlcSourceLocationException(
                "FB_Counter", "Add", 12, 3, new InvalidOperationException("division by zero"));

            writer.SuiteStart("FB_CounterTests");
            var completed = writer.ReportTests(new[] { Passing() });
            writer.SuiteFailed("FB_CounterTests", SuitePath, fault, completed);
            var exitCode = writer.Summary(NoSkips, coverage: null);

            // A suite that never ran to completion still closes its own entry
            // on the consumer's waiting list, and still fails the run.
            Assert.Equal(1, exitCode);
            var lines = Lines(output);
            Assert.Equal(new[] { "suite-start", "suite-result", "summary" }, lines.Select(EventOf).ToArray());

            using var suite = JsonDocument.Parse(lines[1]);
            Assert.Equal("fail", suite.RootElement.GetProperty("outcome").GetString());
            Assert.Equal(FailureKind.PlcFault, suite.RootElement.GetProperty("kind").GetString());
            // The located exception owns the one rendering of "where", so the
            // wire error carries its prefix rather than a second composition.
            Assert.StartsWith("FB_Counter.Add(3): division by zero -- ", suite.RootElement.GetProperty("error").GetString());
            // A suite that never finished has nothing to time.
            Assert.Equal(JsonValueKind.Null, suite.RootElement.GetProperty("durationMs").ValueKind);
            Assert.Equal(12, suite.RootElement.GetProperty("fileLine").GetInt32());
            Assert.Single(suite.RootElement.GetProperty("tests").EnumerateArray());

            using var summary = JsonDocument.Parse(lines[2]);
            // The suite-level fault counts on top of the test that did finish:
            // a run cannot pass just because everything that got to run passed.
            Assert.Equal(1, summary.RootElement.GetProperty("passed").GetInt32());
            Assert.Equal(1, summary.RootElement.GetProperty("failed").GetInt32());
        }

        [Fact]
        public void Ndjson_AnError_IsOneSelfContainedLineAndExitCodeTwo()
        {
            var output = new StringWriter();
            var writer = RunReportWriter.Create(output, asJson: false, streaming: true);
            var skips = new[] { new SkippedFile("/POUs/FB_Odd.TcPOU", "unsupported file") };

            // An error can fire before any discovery or suite event has been
            // emitted, so its line has to stand alone.
            var exitCode = writer.Error("no TcUnit suites found under /POUs", skips, coverage: null);

            Assert.Equal(2, exitCode);
            var line = Assert.Single(Lines(output));
            using var error = JsonDocument.Parse(line);
            Assert.Equal(new[] { "event", "error", "kind", "skipped" }, KeysOf(error.RootElement));
            Assert.Equal("error", error.RootElement.GetProperty("event").GetString());
            Assert.Equal(FailureKind.LoadError, error.RootElement.GetProperty("kind").GetString());
            Assert.StartsWith("no TcUnit suites found under /POUs -- ", error.RootElement.GetProperty("error").GetString());
            var skip = error.RootElement.GetProperty("skipped").EnumerateArray().Single();
            Assert.Equal(new[] { "filePath", "reason" }, KeysOf(skip));
        }

        [Fact]
        public void Json_WritesNothingUntilTheSummary_ThenOneIndentedBlob()
        {
            var output = new StringWriter();
            var writer = RunReportWriter.Create(output, asJson: true, streaming: false);

            writer.Discovery(new[] { "FB_CounterTests" }, FilePaths);
            writer.SuiteStart("FB_CounterTests");
            var tests = writer.ReportTests(new[] { Passing(), Failing() });
            writer.SuiteCompleted("FB_CounterTests", SuitePath, tests, 11);
            Assert.Equal(string.Empty, output.ToString());

            var exitCode = writer.Summary(NoSkips, coverage: null);

            Assert.Equal(1, exitCode);
            using var blob = JsonDocument.Parse(output.ToString());
            // No "event": its absence is what tells a --format json consumer
            // apart from a --stream one.
            Assert.Equal(
                new[] { "suites", "passed", "failed", "exitCode", "skipped" },
                KeysOf(blob.RootElement));
            Assert.Equal(1, blob.RootElement.GetProperty("exitCode").GetInt32());
            Assert.Contains("\n", output.ToString());
        }

        [Fact]
        public void Text_ReportsEachTestAsItFinishes_AndCountsThemInTheSummary()
        {
            var output = new StringWriter();
            var writer = RunReportWriter.Create(output, asJson: false, streaming: false);

            var exitCode = RunOneSuite(writer);

            Assert.Equal(1, exitCode);
            Assert.Equal(
                new[]
                {
                    "CounterResets: PASS",
                    "CounterAdds: FAIL (FAILED TEST 'CounterAdds', EXP: 3, ACT: 2, MSG: sum)",
                    "1 passed, 1 failed"
                },
                Lines(output));
        }

        [Fact]
        public void Text_AFaultedSuite_PrintsTheLocatedFailLineWithItsFramesBeneath()
        {
            var output = new StringWriter();
            var writer = RunReportWriter.Create(output, asJson: false, streaming: false);
            var frames = new[]
            {
                new PlcCallStackFrame("FB_Counter", "Add", 12, 3),
                new PlcCallStackFrame("FB_CounterTests", "CounterAdds", 8, 2)
            };
            var fault = new PlcSourceLocationException(
                "FB_Counter", "Add", 12, 3, frames, new InvalidOperationException("division by zero"));

            writer.SuiteFailed("FB_CounterTests", SuitePath, fault, new TestReport[0]);
            var exitCode = writer.Summary(NoSkips, coverage: null);

            // Frames print beneath the FAIL line, never instead of it, innermost
            // first: a consumer that reads only the FAIL line must keep working,
            // and a reordered chain points the reader at the wrong source line.
            Assert.Equal(1, exitCode);
            Assert.Equal(
                new[]
                {
                    "FB_CounterTests: FAIL (in FB_Counter.Add(3): division by zero)",
                    "    at FB_Counter.Add(3)",
                    "    at FB_CounterTests.CounterAdds(2)",
                    "0 passed, 1 failed"
                },
                Lines(output));
        }

        [Fact]
        public void Text_AFaultChargedToATest_PrintsTheFactualLineWithoutTheGuidanceTheWireCarries()
        {
            var output = new StringWriter();
            var writer = RunReportWriter.Create(output, asJson: false, streaming: false);
            var site = new AssertSite("FB_Counter", "Add", 12, 3);
            var fault = AssertionFailure.Fault(
                "FB_Counter.Add(3): TcUnit native call 'SEL' isn't supported yet",
                FailureKind.UnsupportedConstruct,
                "SEL",
                site,
                new[] { site });

            writer.ReportTests(new[] { new TestCaseResult("CounterAdds", new[] { fault }, 5) });

            // Guidance - "STOP: escalate it as a grammar gap", and the rest of
            // the sentence FailureKind carries - belongs to the structured
            // shapes, whose consumer has nothing else to go on. A human at a
            // console has the terminal around it and reads the factual line.
            var lines = Lines(output);
            Assert.Equal(
                new[]
                {
                    "CounterAdds: FAIL (FB_Counter.Add(3): TcUnit native call 'SEL' isn't supported yet)",
                    "    at FB_Counter.Add(3)"
                },
                lines);
            Assert.DoesNotContain("escalate", string.Join(string.Empty, lines));
        }

        [Fact]
        public void Text_UnderStreamingOrJson_NeverInterleavesAPlainLine()
        {
            foreach (var streaming in new[] { true, false })
            {
                var output = new StringWriter();
                var writer = RunReportWriter.Create(output, asJson: !streaming, streaming: streaming);

                // Plugin banners are the one line with no structured
                // counterpart, so they are also the easiest to leak into an
                // NDJSON stream and break its one-object-per-line contract.
                writer.PluginsLoaded(new[] { "SamplePlugins.dll" });

                Assert.Equal(string.Empty, output.ToString());
            }
        }

        [Fact]
        public void SkippedFiles_AreReportedWithoutChangingTheExitCode()
        {
            var skips = new[] { new SkippedFile("/POUs/FB_Odd.TcPOU", "unsupported file") };
            var output = new StringWriter();
            var writer = RunReportWriter.Create(output, asJson: false, streaming: false);

            writer.SuiteCompleted("FB_CounterTests", SuitePath, writer.ReportTests(new[] { Passing() }), 4);
            var exitCode = writer.Summary(skips, coverage: null);

            // A run that skipped unsupported POUs but ran everything else is
            // still a completed run. Exit 2 stays reserved for a run that
            // produced no results at all.
            Assert.Equal(0, exitCode);
            Assert.Equal(
                new[] { "CounterResets: PASS", "skipped: /POUs/FB_Odd.TcPOU (unsupported file)", "1 passed, 0 failed, 1 skipped" },
                Lines(output));
        }

        [Fact]
        public void Coverage_IsReportedByEveryFormat_IncludingTheUncoveredPous()
        {
            var coverage = new[]
            {
                new PouCoverage("FB_Counter", new[] { "FB_CounterTests" }),
                new PouCoverage("F_ComputeChecksum", new string[0])
            };
            var text = new StringWriter();
            var json = new StringWriter();

            RunReportWriter.Create(text, asJson: false, streaming: false).Summary(NoSkips, coverage);
            RunReportWriter.Create(json, asJson: true, streaming: false).Summary(NoSkips, coverage);

            Assert.Equal(
                new[] { "0 passed, 0 failed", "FB_Counter  suites: FB_CounterTests", "F_ComputeChecksum  suites: (none)" },
                Lines(text));

            using var blob = JsonDocument.Parse(json.ToString());
            var entries = blob.RootElement.GetProperty("coverage").EnumerateArray().ToList();
            Assert.Equal(new[] { "pou", "suites" }, KeysOf(entries[0]));
            Assert.Empty(entries[1].GetProperty("suites").EnumerateArray());
        }
    }
}
