using System.Text.Json;
using xStunit.Vsix.TestRunner;
using Xunit;

namespace xStunit.Vsix.Tests
{
    // The extension reads the CLI's NDJSON out of process and has no ProjectReference
    // to it, so a renamed event or key breaks no build -- it comes back as an
    // unrecognized line, which the fold below reads as "this CLI cannot stream" and
    // answers with a whole second run. These tests are the only thing pinning the
    // event names and shapes against xStunit.Cli.CliRunner's serialization.
    public class XstunitEventStreamTests
    {
        [Fact]
        public void Append_DiscoveryLine_NamesEverySuiteWithTheFileItLivesIn()
        {
            var stream = new XstunitEventStream();

            var streamed = stream.Append(
                @"{""event"":""discovery"",""suites"":[" +
                @"{""name"":""FB_WireRecordTests"",""filePath"":""C:\\proj\\FB_WireRecordTests.TcPOU""}," +
                @"{""name"":""FB_ChecksumTests"",""filePath"":""C:\\proj\\FB_ChecksumTests.TcPOU""}]}");

            var discovery = Assert.IsType<XstunitDiscoveryEvent>(streamed);
            Assert.Equal(2, discovery.Suites.Count);
            Assert.Equal("FB_WireRecordTests", discovery.Suites[0].Name);
            Assert.Equal(@"C:\proj\FB_WireRecordTests.TcPOU", discovery.Suites[0].FilePath);
            Assert.Equal("FB_ChecksumTests", discovery.Suites[1].Name);
        }

        [Fact]
        public void Append_SuiteStartLine_NamesTheSuiteUnderTheSuiteKey()
        {
            var stream = new XstunitEventStream();

            var streamed = stream.Append(@"{""event"":""suite-start"",""suite"":""FB_WireRecordTests""}");

            var start = Assert.IsType<XstunitSuiteStartEvent>(streamed);
            Assert.Equal("FB_WireRecordTests", start.Suite);
        }

        // A suite-result line is a whole suite report with two extra keys, not a
        // reference to one the consumer is expected to have kept: everything the
        // summary's suites[] entry carries is already on it.
        [Fact]
        public void Append_SuiteResultLine_CarriesTheFullSuiteReportAndItsOutcome()
        {
            var stream = new XstunitEventStream();

            var streamed = stream.Append(
                @"{""event"":""suite-result"",""outcome"":""fail"",""name"":""FB_ChecksumTests""," +
                @"""filePath"":""C:\\proj\\FB_ChecksumTests.TcPOU""," +
                @"""error"":""TcUnit native call 'SEL' isn't supported yet"",""kind"":""unsupported-construct""," +
                @"""construct"":""SEL"",""tests"":[],""durationMs"":null,""fileLine"":12," +
                @"""callStack"":[{""pouTypeName"":""FB_ChecksumTests"",""methodName"":null,""line"":8,""bodyLine"":null}]}");

            var suite = Assert.IsType<XstunitSuiteResultEvent>(streamed);
            Assert.Equal("fail", suite.Outcome);
            Assert.Equal("FB_ChecksumTests", suite.Name);
            Assert.Equal(@"C:\proj\FB_ChecksumTests.TcPOU", suite.FilePath);
            Assert.Equal("unsupported-construct", suite.Kind);
            Assert.Equal("SEL", suite.Construct);
            Assert.Null(suite.DurationMs);
            Assert.Equal("FB_ChecksumTests", Assert.Single(suite.CallStack).PouTypeName);
        }

        [Fact]
        public void Append_SuiteResultLine_ForAPassingSuite_ReportsThePassOutcome()
        {
            var stream = new XstunitEventStream();

            var streamed = stream.Append(
                @"{""event"":""suite-result"",""outcome"":""pass"",""name"":""FB_WireRecordTests""," +
                @"""tests"":[{""name"":""EncodesSingleRecord"",""passed"":true,""failures"":[],""durationMs"":3}]," +
                @"""durationMs"":12}");

            var suite = Assert.IsType<XstunitSuiteResultEvent>(streamed);
            Assert.Equal("pass", suite.Outcome);
            Assert.True(Assert.Single(suite.Tests).Passed);
            Assert.Equal(12, suite.DurationMs);
        }

        // Nothing is complete until the summary arrives: a fold that treated the last
        // suite-result as the end of the run would render before the counts exist.
        [Fact]
        public void Append_BeforeTheSummaryLine_HasNoResultYet()
        {
            var stream = new XstunitEventStream();

            stream.Append(@"{""event"":""discovery"",""suites"":[{""name"":""FB_A"",""filePath"":null}]}");
            stream.Append(@"{""event"":""suite-start"",""suite"":""FB_A""}");
            stream.Append(@"{""event"":""suite-result"",""outcome"":""pass"",""name"":""FB_A"",""tests"":[]}");

            Assert.Null(stream.Result);
        }

        [Fact]
        public void Append_SummaryLine_CompletesTheRunAndKeepsThatLineAsTheRawJson()
        {
            const string summaryLine =
                @"{""event"":""summary"",""suites"":[{""name"":""FB_A"",""tests"":[]}]," +
                @"""passed"":3,""failed"":1,""exitCode"":1,""skipped"":[]}";
            var stream = new XstunitEventStream();

            var streamed = stream.Append(summaryLine);

            var summary = Assert.IsType<XstunitSummaryEvent>(streamed);
            Assert.Same(summary, stream.Result);
            Assert.Equal(3, stream.Result.Passed);
            Assert.Equal(1, stream.Result.Failed);
            // The page is handed the CLI's own JSON rather than a re-serialization of
            // this object, so the one line the run ends on has to survive verbatim.
            Assert.Equal(summaryLine, stream.Result.RawJson);
        }

        [Fact]
        public void Append_ErrorLine_CompletesTheRunWithTheTopLevelError()
        {
            var stream = new XstunitEventStream();

            var streamed = stream.Append(
                @"{""event"":""error"",""error"":""path does not exist: C:\\nope"",""kind"":""load-error"",""skipped"":[]}");

            Assert.IsType<XstunitErrorEvent>(streamed);
            Assert.Equal(@"path does not exist: C:\nope", stream.Result.Error);
            Assert.Equal("load-error", stream.Result.Kind);
            Assert.Null(stream.Result.Suites);
            Assert.False(stream.NeedsJsonFallback);
        }

        /// <summary>
        /// The whole point of folding the stream: a streamed run and a buffered
        /// `--format json` run of the same work must produce the same result object,
        /// down to every modelled field.
        /// </summary>
        /// <remarks>
        /// They are the same CLI type serialized twice (xStunit.Cli.CliRunner's
        /// RunReport), so the summary line differs from the blob only by the "event"
        /// key and the indentation. If this goes red, streaming has started rendering
        /// something the buffered path would not have.
        /// </remarks>
        [Fact]
        public void Append_SummaryLine_HoldsTheSameContentAsTheBufferedBlob()
        {
            const string blob = @"{
                ""suites"": [
                    {
                        ""name"": ""FB_FailingLineTests"",
                        ""filePath"": ""C:\\proj\\FB_FailingLineTests.TcPOU"",
                        ""error"": null,
                        ""kind"": null,
                        ""construct"": null,
                        ""tests"": [
                            {
                                ""name"": ""FaultsDeepInAHelper"",
                                ""passed"": false,
                                ""failures"": [
                                    {
                                        ""message"": ""FB_FailingLineHelper.ThrowsFromNestedCall(5): boom"",
                                        ""kind"": ""plc-fault"",
                                        ""pou"": ""FB_FailingLineHelper"",
                                        ""method"": ""ThrowsFromNestedCall"",
                                        ""bodyLine"": 5,
                                        ""line"": 14
                                    }
                                ],
                                ""durationMs"": 4
                            }
                        ],
                        ""durationMs"": 9,
                        ""fileLine"": null,
                        ""callStack"": [
                            { ""pouTypeName"": ""FB_FailingLineHelper"", ""methodName"": ""ThrowsFromNestedCall"", ""line"": 14, ""bodyLine"": 5 }
                        ]
                    },
                    {
                        ""name"": ""FB_CounterTests"",
                        ""filePath"": ""C:\\proj\\FB_CounterTests.TcPOU"",
                        ""error"": null,
                        ""kind"": null,
                        ""construct"": null,
                        ""tests"": [
                            { ""name"": ""CounterStartsAtZero"", ""passed"": true, ""failures"": [], ""durationMs"": 1 }
                        ],
                        ""durationMs"": 3,
                        ""fileLine"": null,
                        ""callStack"": null
                    }
                ],
                ""passed"": 1,
                ""failed"": 1,
                ""exitCode"": 1,
                ""skipped"": []
            }";

            const string summaryLine =
                @"{""event"":""summary"",""suites"":[" +
                @"{""name"":""FB_FailingLineTests"",""filePath"":""C:\\proj\\FB_FailingLineTests.TcPOU""," +
                @"""error"":null,""kind"":null,""construct"":null,""tests"":[" +
                @"{""name"":""FaultsDeepInAHelper"",""passed"":false,""failures"":[" +
                @"{""message"":""FB_FailingLineHelper.ThrowsFromNestedCall(5): boom"",""kind"":""plc-fault""," +
                @"""pou"":""FB_FailingLineHelper"",""method"":""ThrowsFromNestedCall"",""bodyLine"":5,""line"":14}]," +
                @"""durationMs"":4}],""durationMs"":9,""fileLine"":null,""callStack"":[" +
                @"{""pouTypeName"":""FB_FailingLineHelper"",""methodName"":""ThrowsFromNestedCall"",""line"":14,""bodyLine"":5}]}," +
                @"{""name"":""FB_CounterTests"",""filePath"":""C:\\proj\\FB_CounterTests.TcPOU""," +
                @"""error"":null,""kind"":null,""construct"":null,""tests"":[" +
                @"{""name"":""CounterStartsAtZero"",""passed"":true,""failures"":[],""durationMs"":1}]," +
                @"""durationMs"":3,""fileLine"":null,""callStack"":null}]," +
                @"""passed"":1,""failed"":1,""exitCode"":1,""skipped"":[]}";

            var buffered = JsonSerializer.Deserialize<XstunitRunResult>(
                blob, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var stream = new XstunitEventStream();
            stream.Append(summaryLine);

            AssertSameContent(buffered, stream.Result);
        }

        // A CLI too old to know --stream reads the flag as a directory name and prints
        // one plain-text error. Recognizing that as "no stream here" rather than
        // throwing is what makes the fallback possible at all.
        [Fact]
        public void Append_PlainTextLine_IsIgnoredAndAsksForTheJsonFallback()
        {
            var stream = new XstunitEventStream();

            Assert.Null(stream.Append("error: path does not exist: --stream"));
            Assert.Null(stream.Result);
            Assert.True(stream.NeedsJsonFallback);
        }

        // A CLI that never wrote anything is a broken invocation, not an old CLI:
        // re-running it would only fail a second time.
        [Fact]
        public void Append_NothingButBlankLines_DoesNotAskForTheJsonFallback()
        {
            var stream = new XstunitEventStream();

            stream.Append(string.Empty);
            stream.Append("   ");

            Assert.False(stream.NeedsJsonFallback);
        }

        [Fact]
        public void Append_CompleteStream_NeverAsksForTheJsonFallback()
        {
            var stream = new XstunitEventStream();

            stream.Append(@"{""event"":""discovery"",""suites"":[]}");
            stream.Append(@"{""event"":""summary"",""suites"":[],""passed"":0,""failed"":0,""exitCode"":0,""skipped"":[]}");

            Assert.False(stream.NeedsJsonFallback);
        }

        // A newer CLI may add event names this build has never heard of. Skipping them
        // keeps the run renderable; treating them as unparseable would send an
        // otherwise healthy run down the fallback path and run everything twice.
        [Fact]
        public void Append_UnknownEventName_IsSkippedWithoutLosingTheRun()
        {
            var stream = new XstunitEventStream();

            Assert.Null(stream.Append(@"{""event"":""test-start"",""suite"":""FB_A"",""test"":""DoesAThing""}"));

            stream.Append(@"{""event"":""summary"",""suites"":[],""passed"":0,""failed"":0,""exitCode"":0,""skipped"":[]}");
            Assert.NotNull(stream.Result);
            Assert.False(stream.NeedsJsonFallback);
        }

        // A stream that emitted events and then died is a failed run, not a CLI that
        // cannot stream. Re-running it under --format json would execute the user's whole
        // suite a second time - every timer and convergence loop, and any side effect
        // with it - to learn what the stream already said.
        [Fact]
        public void Append_RecognizedEventsThenNoSummary_DoesNotAskForTheJsonFallback()
        {
            var stream = new XstunitEventStream();

            stream.Append(@"{""event"":""discovery"",""suites"":[{""name"":""FB_A"",""filePath"":null}]}");
            stream.Append(@"{""event"":""suite-start"",""suite"":""FB_A""}");

            Assert.Null(stream.Result);
            Assert.False(stream.NeedsJsonFallback);
            Assert.True(stream.EndedMidStream);
        }

        // The fold runs on the process's output thread, where nothing catches: a payload
        // this build cannot read has to be skipped like any other unrecognized line
        // rather than thrown on.
        [Fact]
        public void Append_KnownEventNameWithAMalformedPayload_IsSkippedRatherThanThrown()
        {
            var stream = new XstunitEventStream();

            Assert.Null(stream.Append(
                @"{""event"":""suite-result"",""outcome"":""pass"",""name"":""FB_A"",""tests"":[],""durationMs"":""soon""}"));

            // The name was one this build knows, so the CLI does speak --stream: the
            // answer to a payload it cannot read is never a whole second run.
            Assert.False(stream.NeedsJsonFallback);
        }

        // Half a summary is not a finished run: completing on one would render counts
        // that were never parsed.
        [Fact]
        public void Append_SummaryLineWithAMalformedPayload_LeavesTheRunUnfinished()
        {
            var stream = new XstunitEventStream();

            Assert.Null(stream.Append(
                @"{""event"":""summary"",""suites"":[],""passed"":""three"",""failed"":0,""exitCode"":0,""skipped"":[]}"));

            Assert.Null(stream.Result);
            Assert.False(stream.NeedsJsonFallback);
        }

        [Fact]
        public void Append_JsonLineWithoutAnEventKey_IsIgnored()
        {
            var stream = new XstunitEventStream();

            Assert.Null(stream.Append(@"{""suites"":[],""passed"":0,""failed"":0,""exitCode"":0}"));
            Assert.Null(stream.Result);
        }

        private static void AssertSameContent(XstunitRunResult expected, XstunitRunResult actual)
        {
            Assert.Equal(expected.Passed, actual.Passed);
            Assert.Equal(expected.Failed, actual.Failed);
            Assert.Equal(expected.ExitCode, actual.ExitCode);
            Assert.Equal(expected.Error, actual.Error);
            Assert.Equal(expected.Kind, actual.Kind);
            Assert.Equal(expected.Suites.Count, actual.Suites.Count);

            for (var i = 0; i < expected.Suites.Count; i++)
            {
                var expectedSuite = expected.Suites[i];
                var actualSuite = actual.Suites[i];
                Assert.Equal(expectedSuite.Name, actualSuite.Name);
                Assert.Equal(expectedSuite.FilePath, actualSuite.FilePath);
                Assert.Equal(expectedSuite.Error, actualSuite.Error);
                Assert.Equal(expectedSuite.Kind, actualSuite.Kind);
                Assert.Equal(expectedSuite.Construct, actualSuite.Construct);
                Assert.Equal(expectedSuite.DurationMs, actualSuite.DurationMs);
                Assert.Equal(expectedSuite.Tests.Count, actualSuite.Tests.Count);

                for (var t = 0; t < expectedSuite.Tests.Count; t++)
                {
                    var expectedTest = expectedSuite.Tests[t];
                    var actualTest = actualSuite.Tests[t];
                    Assert.Equal(expectedTest.Name, actualTest.Name);
                    Assert.Equal(expectedTest.Passed, actualTest.Passed);
                    Assert.Equal(expectedTest.DurationMs, actualTest.DurationMs);
                    Assert.Equal(expectedTest.Failures.Count, actualTest.Failures.Count);

                    for (var f = 0; f < expectedTest.Failures.Count; f++)
                    {
                        Assert.Equal(expectedTest.Failures[f].Message, actualTest.Failures[f].Message);
                    }
                }

                if (expectedSuite.CallStack == null)
                {
                    Assert.Null(actualSuite.CallStack);
                    continue;
                }

                Assert.Equal(expectedSuite.CallStack.Count, actualSuite.CallStack.Count);
                for (var f = 0; f < expectedSuite.CallStack.Count; f++)
                {
                    Assert.Equal(expectedSuite.CallStack[f].PouTypeName, actualSuite.CallStack[f].PouTypeName);
                    Assert.Equal(expectedSuite.CallStack[f].MethodName, actualSuite.CallStack[f].MethodName);
                    Assert.Equal(expectedSuite.CallStack[f].Line, actualSuite.CallStack[f].Line);
                    Assert.Equal(expectedSuite.CallStack[f].BodyLine, actualSuite.CallStack[f].BodyLine);
                }
            }
        }
    }
}
