using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using xStunit.Interpreter;
using xStunit.Parser;
using xStunit.Runner;
using xStunit.Runner.TcUnitStub;
using xStunit.Vsix.TestRunner;
using Xunit;

namespace xStunit.Cli.Tests
{
    // The two halves of the CLI's published wire contract, joined: the CLI's
    // own serializer writes, the VSIX's own models and deserializer read, and
    // nothing hand-written sits between them.
    //
    // Each side is otherwise pinned only against its own literal -
    // RunReportTests against the key sequence the CLI emits,
    // XstunitModelsDeserializationTests against a JSON blob typed by hand - so
    // the two can drift into agreement with nothing at all. The extension has
    // no reference to the CLI (it shells out and reads stdout), so a renamed
    // property breaks no build; it produces a silently null field and a results
    // tree that renders a blank. That is the failure these tests turn red:
    // rename a property on either side, or change an event name, and the round
    // trip stops arriving.
    //
    // WHAT THESE DO NOT CATCH. Only fields the VSIX models declare are checked -
    // the CLI also emits coverage, and per-failure kind/construct/assert/
    // expected/actual/assertMessage/pou/method/bodyLine/line/callStack, which
    // the extension deliberately does not model (XstunitFailure carries the
    // message alone); RunReportTests is what guards those keys. Nothing here
    // executes the CLI, so how a real run populates a report is out of scope,
    // as is anything the WebView2 page does with the JSON afterwards. And a
    // field that is null on BOTH sides of a shape - the run-level error and kind
    // of a summary, say - is pinned only by the shape that populates it, which
    // is why the error shapes below are round-tripped too rather than assumed.
    public class WireContractRoundTripTests
    {
        private const string RanSuitePath = "/POUs/FB_WireRecordTests.TcPOU";
        private const string FaultedSuitePath = "/POUs/FB_ChecksumTests.TcPOU";
        private const string SkippedPath = "/POUs/FB_Malformed.TcPOU";
        private const string SkipReason = "could not read this body at line 2";
        private const string AssertMessage = "FAILED TEST 'RetriesOnTimeout', EXP: 3, ACT: 1, MSG: retries";
        private const string FaultMessage = "TcUnit native call 'SEL' isn't supported yet";

        // Identity guidance. What FailureGuidance appends to a message is its own rule
        // with its own tests; a value that arrives here changed is a mapping
        // fault, and a guidance suffix in the middle would hide one.
        private static RunReportBuilder Builder() =>
            new RunReportBuilder((message, kind, isVerbatim, bodyLine) => message, failure => failure.Message);

        private static readonly IReadOnlyList<SkippedFile> Skips =
            new[] { new SkippedFile(SkippedPath, SkipReason) };

        [Fact]
        public void JsonBlob_OfAPopulatedSummary_ArrivesWholeInTheExtensionsRunResult()
        {
            var blob = RunReportJson.Blob(Summary());

            var result = JsonSerializer.Deserialize<XstunitRunResult>(blob, XstunitEventStream.SerializerOptions);

            AssertEveryFieldSurvives(
                result,
                new Dictionary<string, object>
                {
                    ["Suites"] = Check<List<XstunitSuiteResult>>(AssertBothSuitesSurvived),
                    ["Passed"] = 1,
                    ["Failed"] = 2,
                    ["ExitCode"] = 1,
                    ["Skipped"] = Check<List<XstunitSkippedFile>>(AssertSkipsSurvived),
                    // The summary shape carries no run-level failure: those two
                    // keys belong to the early-exit shape below, which is where
                    // a rename of either shows up.
                    ["Error"] = null,
                    ["Kind"] = null
                },
                notOnTheWire: nameof(XstunitRunResult.RawJson));
        }

        [Fact]
        public void JsonBlob_OfAnEarlyExitError_ArrivesWithTheRunLevelErrorKindAndSkips()
        {
            var blob = RunReportJson.Blob(Builder().Error("no TcUnit suites found under /POUs", Skips, coverage: null));

            var result = JsonSerializer.Deserialize<XstunitRunResult>(blob, XstunitEventStream.SerializerOptions);

            AssertEveryFieldSurvives(
                result,
                new Dictionary<string, object>
                {
                    ["Error"] = "no TcUnit suites found under /POUs",
                    ["Kind"] = FailureKind.LoadError,
                    // Skips collected before the run died are the only account
                    // of why nothing was found, so they have to survive a shape
                    // that carries no suites at all.
                    ["Skipped"] = Check<List<XstunitSkippedFile>>(AssertSkipsSurvived),
                    ["Suites"] = null,
                    ["Passed"] = 0,
                    ["Failed"] = 0,
                    ["ExitCode"] = 0
                },
                notOnTheWire: nameof(XstunitRunResult.RawJson));
        }

        [Fact]
        public void StreamLine_Discovery_ArrivesAsTheExtensionsDiscoveryEvent()
        {
            var filePaths = new Dictionary<string, string> { ["FB_WireRecordTests"] = RanSuitePath };
            var line = RunReportJson.Line(
                RunReportBuilder.Discovery(new[] { "FB_WireRecordTests" }, filePaths));

            var discovery = Assert.IsType<XstunitDiscoveryEvent>(new XstunitEventStream().Append(line));

            AssertEveryFieldSurvives(
                discovery,
                new Dictionary<string, object>
                {
                    ["Event"] = XstunitStreamEventNames.Discovery,
                    ["Suites"] = Check<List<XstunitDiscoveredSuite>>(suites =>
                    {
                        var suite = Assert.Single(suites);
                        AssertEveryFieldSurvives(
                            suite,
                            new Dictionary<string, object>
                            {
                                ["Name"] = "FB_WireRecordTests",
                                ["FilePath"] = RanSuitePath
                            });
                    })
                });
        }

        [Fact]
        public void StreamLine_SuiteStart_ArrivesAsTheExtensionsSuiteStartEvent()
        {
            var line = RunReportJson.Line(RunReportBuilder.SuiteStart("FB_WireRecordTests"));

            var start = Assert.IsType<XstunitSuiteStartEvent>(new XstunitEventStream().Append(line));

            AssertEveryFieldSurvives(
                start,
                new Dictionary<string, object>
                {
                    ["Event"] = XstunitStreamEventNames.SuiteStart,
                    ["Suite"] = "FB_WireRecordTests"
                });
        }

        [Fact]
        public void StreamLine_SuiteResultOfASuiteThatRan_CarriesItsTestsAndDuration()
        {
            var line = RunReportJson.Line(RanSuite(Builder(), "suite-result", "pass"));

            var suite = Assert.IsType<XstunitSuiteResultEvent>(new XstunitEventStream().Append(line));

            var expected = RanSuiteFields();
            expected["Event"] = XstunitStreamEventNames.SuiteResult;
            expected["Outcome"] = "pass";
            AssertEveryFieldSurvives(suite, expected);
        }

        [Fact]
        public void StreamLine_SuiteResultOfASuiteThatFaulted_CarriesItsKindConstructAndCallStack()
        {
            var line = RunReportJson.Line(FaultedSuite(Builder(), "suite-result", "fail"));

            var suite = Assert.IsType<XstunitSuiteResultEvent>(new XstunitEventStream().Append(line));

            var expected = FaultedSuiteFields();
            expected["Event"] = XstunitStreamEventNames.SuiteResult;
            expected["Outcome"] = "fail";
            AssertEveryFieldSurvives(suite, expected);
        }

        [Fact]
        public void StreamLine_Summary_ArrivesWholeAndFinishesTheStream()
        {
            var line = RunReportJson.Line(Summary("summary"));
            var stream = new XstunitEventStream();

            var summary = Assert.IsType<XstunitSummaryEvent>(stream.Append(line));

            AssertEveryFieldSurvives(
                summary,
                new Dictionary<string, object>
                {
                    ["Event"] = XstunitStreamEventNames.Summary,
                    ["Suites"] = Check<List<XstunitSuiteResult>>(AssertBothSuitesSurvived),
                    ["Passed"] = 1,
                    ["Failed"] = 2,
                    ["ExitCode"] = 1,
                    ["Skipped"] = Check<List<XstunitSkippedFile>>(AssertSkipsSurvived),
                    ["Error"] = null,
                    ["Kind"] = null,
                    // Stashed by the extension itself, not read off the wire:
                    // the page is handed the CLI's own bytes rather than a
                    // re-serialization of this object.
                    ["RawJson"] = line
                });
            // A streamed run is finished by its summary line and by nothing
            // earlier, so the same line that deserialized must also complete it.
            Assert.Same(summary, stream.Result);
        }

        [Fact]
        public void StreamLine_Error_ArrivesWholeAndFinishesTheStream()
        {
            var line = RunReportJson.Line(
                Builder().Error("no TcUnit suites found under /POUs", Skips, coverage: null, streamEvent: "error"));
            var stream = new XstunitEventStream();

            var error = Assert.IsType<XstunitErrorEvent>(stream.Append(line));

            AssertEveryFieldSurvives(
                error,
                new Dictionary<string, object>
                {
                    ["Event"] = XstunitStreamEventNames.Error,
                    ["Error"] = "no TcUnit suites found under /POUs",
                    ["Kind"] = FailureKind.LoadError,
                    ["Skipped"] = Check<List<XstunitSkippedFile>>(AssertSkipsSurvived),
                    ["Suites"] = null,
                    ["Passed"] = 0,
                    ["Failed"] = 0,
                    ["ExitCode"] = 0,
                    ["RawJson"] = line
                });
            Assert.Same(error, stream.Result);
        }

        private static RunReport Summary(string streamEvent = null)
        {
            var builder = Builder();
            return builder.Summary(
                new[] { RanSuite(builder), FaultedSuite(builder) },
                passed: 1,
                failed: 2,
                exitCode: 1,
                Skips,
                coverage: null,
                streamEvent);
        }

        // A suite that ran to completion: it has a duration and tests, and none
        // of the fields only a fault populates.
        private static SuiteReport RanSuite(
            RunReportBuilder builder, string streamEvent = null, string outcome = null)
        {
            var failing = builder.Test(new TestCaseResult(
                "RetriesOnTimeout",
                new[]
                {
                    new AssertionFailure(
                        AssertMessage,
                        "AssertEquals_INT",
                        "3",
                        "1",
                        "retries",
                        new AssertSite("FB_WireRecordTests", "RetriesOnTimeout", 42, 7))
                },
                9));
            var passing = builder.Test(new TestCaseResult("EncodesSingleRecord", new AssertionFailure[0], 3));

            return builder.Suite(
                "FB_WireRecordTests", RanSuitePath, new[] { failing, passing }, 12, streamEvent, outcome);
        }

        // A suite that faulted: it has an error, a kind, a construct and a call
        // stack, and no duration - it never finished, so there is nothing to
        // time. Between this suite and the one above, every field the
        // extension's suite model declares arrives populated at least once.
        private static SuiteReport FaultedSuite(
            RunReportBuilder builder, string streamEvent = null, string outcome = null)
        {
            var frames = new[]
            {
                new PlcCallStackFrame("F_nCheckSum", null, 12, 1),
                new PlcCallStackFrame("FB_ChecksumTests", "ChecksumRejectsShortFrames", 8, 2)
            };
            var located = new PlcSourceLocationException(
                "F_nCheckSum", null, 12, 1, frames, new NotSupportedException(FaultMessage));

            return builder.SuiteError(
                "FB_ChecksumTests",
                FaultedSuitePath,
                FaultMessage,
                FailureKind.UnsupportedConstruct,
                "SEL",
                new TestReport[0],
                located,
                streamEvent,
                outcome);
        }

        private static void AssertBothSuitesSurvived(List<XstunitSuiteResult> suites)
        {
            Assert.Equal(2, suites.Count);
            AssertEveryFieldSurvives(suites[0], RanSuiteFields());
            AssertEveryFieldSurvives(suites[1], FaultedSuiteFields());
        }

        private static Dictionary<string, object> RanSuiteFields() =>
            new Dictionary<string, object>
            {
                ["Name"] = "FB_WireRecordTests",
                ["FilePath"] = RanSuitePath,
                ["DurationMs"] = 12L,
                ["Tests"] = Check<List<XstunitTestResult>>(tests =>
                {
                    Assert.Equal(2, tests.Count);
                    AssertEveryFieldSurvives(
                        tests[0],
                        new Dictionary<string, object>
                        {
                            ["Name"] = "RetriesOnTimeout",
                            ["Passed"] = false,
                            ["DurationMs"] = 9L,
                            ["Failures"] = Check<List<XstunitFailure>>(failures =>
                                AssertEveryFieldSurvives(
                                    Assert.Single(failures),
                                    new Dictionary<string, object> { ["Message"] = AssertMessage }))
                        });
                    AssertEveryFieldSurvives(
                        tests[1],
                        new Dictionary<string, object>
                        {
                            ["Name"] = "EncodesSingleRecord",
                            ["Passed"] = true,
                            ["DurationMs"] = 3L,
                            // Empty, not null: a passing test emits the array
                            // anyway so a renderer reads it unconditionally.
                            ["Failures"] = Check<List<XstunitFailure>>(Assert.Empty)
                        });
                }),
                ["Error"] = null,
                ["Kind"] = null,
                ["Construct"] = null,
                ["CallStack"] = null
            };

        private static Dictionary<string, object> FaultedSuiteFields() =>
            new Dictionary<string, object>
            {
                ["Name"] = "FB_ChecksumTests",
                ["FilePath"] = FaultedSuitePath,
                ["Error"] = FaultMessage,
                ["Kind"] = FailureKind.UnsupportedConstruct,
                ["Construct"] = "SEL",
                // A suite that never ran to completion has nothing to time, so
                // this stays null rather than becoming a 0 a UI would render as
                // "instant".
                ["DurationMs"] = null,
                ["Tests"] = Check<List<XstunitTestResult>>(Assert.Empty),
                ["CallStack"] = Check<List<XstunitCallStackFrame>>(frames =>
                {
                    // Innermost frame first, suite entry point last: reversed,
                    // the extension would point the reader at the wrong line.
                    Assert.Equal(2, frames.Count);
                    AssertEveryFieldSurvives(
                        frames[0],
                        new Dictionary<string, object>
                        {
                            ["PouTypeName"] = "F_nCheckSum",
                            // A POU's own body has no method to name.
                            ["MethodName"] = null,
                            ["Line"] = 12,
                            ["BodyLine"] = 1
                        });
                    AssertEveryFieldSurvives(
                        frames[1],
                        new Dictionary<string, object>
                        {
                            ["PouTypeName"] = "FB_ChecksumTests",
                            ["MethodName"] = "ChecksumRejectsShortFrames",
                            ["Line"] = 8,
                            ["BodyLine"] = 2
                        });
                })
            };

        private static void AssertSkipsSurvived(List<XstunitSkippedFile> skipped) =>
            AssertEveryFieldSurvives(
                Assert.Single(skipped),
                new Dictionary<string, object>
                {
                    ["FilePath"] = SkippedPath,
                    ["Reason"] = SkipReason
                });

        // Compares the model's DECLARED properties against the expected set
        // before comparing any value, so a property added to an extension model
        // and left uncovered fails here by name instead of passing unnoticed -
        // an uncovered property is exactly how a silently-null field reaches the
        // wire. `notOnTheWire` is for a property the CLI never emits, and every
        // use of it is a claim that has to be argued at the call site.
        private static void AssertEveryFieldSurvives(
            object model, IReadOnlyDictionary<string, object> expected, params string[] notOnTheWire)
        {
            Assert.NotNull(model);

            var declared = model.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .Except(notOnTheWire)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(declared, expected.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray());

            foreach (var field in expected)
            {
                var actual = model.GetType().GetProperty(field.Key).GetValue(model);
                if (field.Value is Action<object> inspect)
                {
                    inspect(actual);
                    continue;
                }

                Assert.True(
                    Equals(field.Value, actual),
                    $"{model.GetType().Name}.{field.Key}: expected {Describe(field.Value)}, " +
                    $"round-tripped {Describe(actual)}");
            }
        }

        // Lets a nested model or collection be asserted in place, keeping one
        // expectation per property so the declared-property comparison above
        // still sees a complete set.
        private static object Check<T>(Action<T> inspect) =>
            new Action<object>(value => inspect(Assert.IsAssignableFrom<T>(value)));

        private static string Describe(object value) => value == null ? "null" : $"'{value}'";
    }
}
