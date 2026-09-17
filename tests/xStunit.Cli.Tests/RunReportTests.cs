using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using xStunit.Cli;
using xStunit.Interpreter;
using xStunit.Parser;
using xStunit.Runner;
using xStunit.Runner.TcUnitStub;
using Xunit;

namespace xStunit.Cli.Tests
{
    // The report shape, exercised without a run: results in, wire bytes out.
    //
    // What these pin is the JSON KEYS, not the C# types behind them. The
    // consumer of this output is out-of-process - it shells out to the CLI and
    // deserializes stdout, holding no reference to any type in this repo (see
    // src/xStunit.Vsix/TestRunner/XstunitModels.cs) - so renaming a report class
    // costs nothing and renaming a property silently breaks it. Comparing the
    // full key sequence, rather than probing one field, is what makes a dropped,
    // added or re-cased key a red test.
    public class RunReportTests
    {
        // The wording of the guidance appended to a message belongs to
        // FailureGuidance, which owns the failure-kind rules; these stand-ins
        // carry just enough shape to pin WHERE the builder applies it - once,
        // on the way in - without restating that prose here.
        private static RunReportBuilder Builder() =>
            new RunReportBuilder(
                (message, kind, isVerbatim, bodyLine) => isVerbatim ? message : message + " -- " + kind,
                failure => failure.Message);

        private static readonly IReadOnlyList<SkippedFile> NoSkips = new SkippedFile[0];
        private static readonly IReadOnlyList<DeclarationWarning> NoWarnings = new DeclarationWarning[0];

        private static string[] KeysOf(JsonElement element) =>
            element.EnumerateObject().Select(p => p.Name).ToArray();

        [Fact]
        public void Summary_FromSuiteResults_SerializesTheCamelCaseWireShape()
        {
            var builder = Builder();
            var failing = new TestCaseResult(
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
            var passing = new TestCaseResult("CounterResets", new AssertionFailure[0], 2);

            var tests = new[] { builder.Test(failing), builder.Test(passing) };
            var suite = builder.Suite("FB_CounterTests", "/POUs/FB_CounterTests.TcPOU", tests, 11);
            var json = RunReportJson.Blob(
                builder.Summary(new[] { suite }, passed: 1, failed: 1, exitCode: 1, NoSkips, NoWarnings, coverage: null));

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // No "event" and no "coverage": both are omitted when null, which is
            // what tells a --format json consumer apart from a --stream one, and
            // an unrequested coverage list apart from an empty one.
            Assert.Equal(new[] { "suites", "passed", "failed", "exitCode", "skipped", "warnings" }, KeysOf(root));
            Assert.Equal(1, root.GetProperty("passed").GetInt32());
            Assert.Equal(1, root.GetProperty("failed").GetInt32());
            Assert.Equal(1, root.GetProperty("exitCode").GetInt32());
            Assert.Empty(root.GetProperty("skipped").EnumerateArray());

            var suiteElement = root.GetProperty("suites").EnumerateArray().Single();
            Assert.Equal(
                new[]
                {
                    "outcome", "name", "filePath", "error", "kind", "construct", "tests", "durationMs",
                    "fileLine", "callStack"
                },
                KeysOf(suiteElement));
            Assert.Equal("FB_CounterTests", suiteElement.GetProperty("name").GetString());
            Assert.Equal("/POUs/FB_CounterTests.TcPOU", suiteElement.GetProperty("filePath").GetString());
            Assert.Equal(11, suiteElement.GetProperty("durationMs").GetInt64());
            Assert.Equal(JsonValueKind.Null, suiteElement.GetProperty("error").ValueKind);
            Assert.Equal(JsonValueKind.Null, suiteElement.GetProperty("callStack").ValueKind);

            var testElements = suiteElement.GetProperty("tests").EnumerateArray().ToList();
            Assert.Equal(new[] { "name", "passed", "failures", "durationMs" }, KeysOf(testElements[0]));
            Assert.False(testElements[0].GetProperty("passed").GetBoolean());
            Assert.Equal(5, testElements[0].GetProperty("durationMs").GetInt64());
            Assert.True(testElements[1].GetProperty("passed").GetBoolean());
            Assert.Empty(testElements[1].GetProperty("failures").EnumerateArray());

            var failure = testElements[0].GetProperty("failures").EnumerateArray().Single();
            Assert.Equal(
                new[]
                {
                    "message", "kind", "construct", "assert", "expected", "actual", "assertMessage",
                    "pou", "method", "bodyLine", "line", "callStack"
                },
                KeysOf(failure));
            // A formatted TcUnit assert line reaches the wire verbatim - the one
            // message no guidance may be appended to.
            Assert.Equal("FAILED TEST 'CounterAdds', EXP: 3, ACT: 2, MSG: sum", failure.GetProperty("message").GetString());
            Assert.Equal(FailureKind.Assertion, failure.GetProperty("kind").GetString());
            Assert.Equal("AssertEquals_INT", failure.GetProperty("assert").GetString());
            Assert.Equal("3", failure.GetProperty("expected").GetString());
            Assert.Equal("2", failure.GetProperty("actual").GetString());
            Assert.Equal("sum", failure.GetProperty("assertMessage").GetString());
            Assert.Equal("FB_CounterTests", failure.GetProperty("pou").GetString());
            Assert.Equal("CounterAdds", failure.GetProperty("method").GetString());
            // bodyLine is the editor-relative line, line the raw XML one; they
            // are never interchangeable and never the same number by accident.
            Assert.Equal(7, failure.GetProperty("bodyLine").GetInt32());
            Assert.Equal(42, failure.GetProperty("line").GetInt32());
        }

        // Whether a suite failed is decided here, on the side that owns the exit
        // code, and travels on the wire. A consumer that re-derives it from
        // `error` and `tests` is the thing this makes unnecessary - and the
        // faulted suite below is where a re-derivation from `tests` alone gets
        // it wrong, because every test it got to run passed.
        [Fact]
        public void Summary_EverySuite_CarriesAnOutcomeIncludingOneThatFaultedWithAllTestsPassing()
        {
            var builder = Builder();
            var passing = builder.Test(new TestCaseResult("CounterResets", new AssertionFailure[0], 2));
            var failing = builder.Test(
                new TestCaseResult("CounterAdds", new[] { new AssertionFailure("EXP: 3, ACT: 2") }, 5));
            var suites = new[]
            {
                builder.Suite("FB_PassingTests", "/POUs/FB_PassingTests.TcPOU", new[] { passing }, 3),
                builder.Suite("FB_FailingTests", "/POUs/FB_FailingTests.TcPOU", new[] { passing, failing }, 8),
                builder.SuiteError(
                    "FB_FaultedTests",
                    "/POUs/FB_FaultedTests.TcPOU",
                    "boom",
                    "in FB_Widget.Step(3): boom",
                    FailureKind.PlcFault,
                    null,
                    new[] { passing },
                    new PlcSourceLocationException("FB_Widget", "Step", 12, 3, new InvalidOperationException("boom")))
            };

            var json = RunReportJson.Blob(
                builder.Summary(suites, passed: 3, failed: 2, exitCode: 1, NoSkips, NoWarnings, coverage: null));

            using var doc = JsonDocument.Parse(json);
            Assert.Equal(
                new[] { "pass", "fail", "fail" },
                doc.RootElement.GetProperty("suites").EnumerateArray()
                    .Select(suite => suite.GetProperty("outcome").GetString())
                    .ToArray());
        }

        [Fact]
        public void Test_FailureWithNoKnownLocation_EmitsNullsRatherThanZeroLines()
        {
            var report = Builder().Test(
                new TestCaseResult("NoLocation", new[] { new AssertionFailure("something broke") }, 1));

            using var doc = JsonDocument.Parse(RunReportJson.Blob(report));
            var failure = doc.RootElement.GetProperty("failures").EnumerateArray().Single();
            // The UnknownLine sentinel is 0, and 0 is a line number a consumer
            // could act on. It must never reach the wire as one.
            Assert.Equal(JsonValueKind.Null, failure.GetProperty("bodyLine").ValueKind);
            Assert.Equal(JsonValueKind.Null, failure.GetProperty("line").ValueKind);
            Assert.Equal(JsonValueKind.Null, failure.GetProperty("pou").ValueKind);
            Assert.Equal(JsonValueKind.Null, failure.GetProperty("method").ValueKind);
        }

        [Fact]
        public void SuiteError_WithLocatedFault_CarriesTheCallStackAndNoFabricatedDuration()
        {
            var located = new PlcSourceLocationException("FB_Widget", "Step", 12, 3, new InvalidOperationException("boom"));

            var suite = Builder().SuiteError(
                "FB_WidgetTests",
                "/POUs/FB_WidgetTests.TcPOU",
                "boom",
                "in FB_Widget.Step(3): boom",
                FailureKind.PlcFault,
                null,
                new TestReport[0],
                located).AsStreamEvent("suite-result");

            using var doc = JsonDocument.Parse(RunReportJson.Line(suite));
            var root = doc.RootElement;
            Assert.Equal("suite-result", root.GetProperty("event").GetString());
            Assert.Equal("fail", root.GetProperty("outcome").GetString());
            Assert.Equal(FailureKind.PlcFault, root.GetProperty("kind").GetString());
            // A suite that never finished has nothing to time, so the duration
            // is absent rather than a 0 a consumer would render as "instant".
            Assert.Equal(JsonValueKind.Null, root.GetProperty("durationMs").ValueKind);
            Assert.Equal(12, root.GetProperty("fileLine").GetInt32());

            var frame = root.GetProperty("callStack").EnumerateArray().Single();
            Assert.Equal(new[] { "pouTypeName", "methodName", "line", "bodyLine" }, KeysOf(frame));
            Assert.Equal("FB_Widget", frame.GetProperty("pouTypeName").GetString());
            Assert.Equal("Step", frame.GetProperty("methodName").GetString());
            Assert.Equal(12, frame.GetProperty("line").GetInt32());
            Assert.Equal(3, frame.GetProperty("bodyLine").GetInt32());
        }

        [Fact]
        public void Error_AppliesGuidanceOnce_AndAlwaysReportsLoadErrorKind()
        {
            var skipped = new[] { new SkippedFile("/POUs/FB_Odd.TcPOU", "unsupported file") };

            var json = RunReportJson.Blob(Builder().Error("no TcUnit suites found under /POUs", skipped, NoWarnings, coverage: null));

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal(new[] { "error", "kind", "skipped", "warnings" }, KeysOf(root));
            Assert.Equal("no TcUnit suites found under /POUs -- load-error", root.GetProperty("error").GetString());
            // Constant, not derived: every error in this shape is a run that
            // produced no results at all.
            Assert.Equal(FailureKind.LoadError, root.GetProperty("kind").GetString());

            var skip = root.GetProperty("skipped").EnumerateArray().Single();
            Assert.Equal(new[] { "filePath", "reason" }, KeysOf(skip));
            Assert.Equal("/POUs/FB_Odd.TcPOU", skip.GetProperty("filePath").GetString());
            Assert.Equal("unsupported file", skip.GetProperty("reason").GetString());
        }

        [Fact]
        public void Summary_WithCoverage_ReportsEveryPouIncludingTheUncoveredOnes()
        {
            var coverage = new[]
            {
                new PouCoverage("FB_Counter", new[] { "FB_CounterTests" }),
                new PouCoverage("F_ComputeChecksum", new string[0])
            };

            var json = RunReportJson.Blob(
                Builder().Summary(new SuiteReport[0], passed: 0, failed: 0, exitCode: 0, NoSkips, NoWarnings, coverage));

            using var doc = JsonDocument.Parse(json);
            var entries = doc.RootElement.GetProperty("coverage").EnumerateArray().ToList();
            Assert.Equal(new[] { "pou", "suites" }, KeysOf(entries[0]));
            Assert.Equal("FB_Counter", entries[0].GetProperty("pou").GetString());
            Assert.Equal("FB_CounterTests", entries[0].GetProperty("suites").EnumerateArray().Single().GetString());
            // The empty list is the point of the flag: a POU with no suite is a
            // directly usable next task.
            Assert.Empty(entries[1].GetProperty("suites").EnumerateArray());
        }

        [Fact]
        public void StreamEvents_AreEachExactlyOneJsonObjectOnOneLine()
        {
            var builder = Builder();
            var filePaths = new Dictionary<string, string> { ["FB_CounterTests"] = "/POUs/FB_CounterTests.TcPOU" };

            var lines = new[]
            {
                RunReportJson.Line(RunReportBuilder.Discovery(new[] { "FB_CounterTests", "FB_UnknownTests" }, filePaths)),
                RunReportJson.Line(RunReportBuilder.SuiteStart("FB_CounterTests")),
                RunReportJson.Line(builder
                    .Suite("FB_CounterTests", "/POUs/FB_CounterTests.TcPOU", new TestReport[0], 4)
                    .AsStreamEvent("suite-result")),
                RunReportJson.Line(builder.Summary(new SuiteReport[0], 0, 0, 0, NoSkips, NoWarnings, null, "summary")),
                RunReportJson.Line(builder.Error("bad path", NoSkips, NoWarnings, null, "error"))
            };

            // NDJSON's whole contract: a consumer reading line by line must get
            // one complete event per line, so an embedded newline anywhere in
            // this shape splits one event into two unparseable halves.
            foreach (var line in lines)
            {
                Assert.DoesNotContain("\n", line);
                Assert.DoesNotContain("\r", line);
                using var parsed = JsonDocument.Parse(line);
                Assert.Equal(JsonValueKind.Object, parsed.RootElement.ValueKind);
            }

            Assert.Equal(
                new[] { "discovery", "suite-start", "suite-result", "summary", "error" },
                lines.Select(line =>
                {
                    using var parsed = JsonDocument.Parse(line);
                    return parsed.RootElement.GetProperty("event").GetString();
                }).ToArray());

            using var discovery = JsonDocument.Parse(lines[0]);
            var discovered = discovery.RootElement.GetProperty("suites").EnumerateArray().ToList();
            Assert.Equal(new[] { "name", "filePath" }, KeysOf(discovered[0]));
            Assert.Equal("/POUs/FB_CounterTests.TcPOU", discovered[0].GetProperty("filePath").GetString());
            // A suite whose file path was never recorded still gets an entry:
            // the discovery line is what a consumer builds its "waiting" list
            // from, and a missing entry would leave that list short.
            Assert.Equal(JsonValueKind.Null, discovered[1].GetProperty("filePath").ValueKind);
        }

        [Fact]
        public void Blob_IsIndented_AndTheStreamLineOfTheSameReportIsNot()
        {
            var report = Builder().Summary(new SuiteReport[0], passed: 0, failed: 0, exitCode: 0, NoSkips, NoWarnings, null);

            // Same report, two renderings: the human-facing blob may span lines,
            // the NDJSON line may not. Nothing else about them differs.
            Assert.Contains("\n", RunReportJson.Blob(report));
            Assert.DoesNotContain("\n", RunReportJson.Line(report));
        }
    }
}
