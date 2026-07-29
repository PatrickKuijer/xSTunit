using System.Text.Json;
using TcXunit.Vsix.TestRunner;
using Xunit;

namespace TcXunit.Vsix.Tests
{
    // TcxunitModels.cs is deserialized in production by
    // System.Web.Script.Serialization.JavaScriptSerializer (net472-only, see
    // TcxunitProcessRunner.cs), which is not available under net8.0. These tests
    // exercise the same model classes with System.Text.Json instead --
    // JavaScriptSerializer's default object converter matches JSON member names to
    // CLR property names case-insensitively, so PropertyNameCaseInsensitive = true
    // here mirrors that production binding behavior rather than testing a
    // different contract. What's under test is the model's *shape* (property
    // names/types line up with the CLI's `--format json` output), not which
    // serializer library is used to fill it in.
    public class TcxunitModelsDeserializationTests
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        [Fact]
        public void Deserialize_PopulatedRun_MapsSuitesTestsAndCounts()
        {
            const string json = @"{
                ""suites"": [
                    {
                        ""name"": ""FB_WireRecordTests"",
                        ""filePath"": ""C:\\proj\\FB_WireRecordTests.TcPOU"",
                        ""error"": null,
                        ""durationMs"": 12,
                        ""tests"": [
                            { ""name"": ""EncodesSingleRecord"", ""passed"": true, ""failures"": [], ""durationMs"": 3 },
                            {
                                ""name"": ""RetriesOnTimeout"",
                                ""passed"": false,
                                ""failures"": [
                                    {
                                        ""message"": ""FAILED TEST 'RetriesOnTimeout', EXP: 3, ACT: 1"",
                                        ""kind"": ""assertion"",
                                        ""assert"": ""AssertEquals_INT"",
                                        ""expected"": ""3"",
                                        ""actual"": ""1"",
                                        ""pou"": ""FB_WireRecordTests"",
                                        ""method"": ""RetriesOnTimeout"",
                                        ""bodyLine"": 6
                                    }
                                ],
                                ""durationMs"": 9
                            }
                        ]
                    }
                ],
                ""passed"": 1,
                ""failed"": 1,
                ""exitCode"": 1
            }";

            var result = JsonSerializer.Deserialize<TcxunitRunResult>(json, Options);

            Assert.NotNull(result);
            Assert.Equal(1, result.Passed);
            Assert.Equal(1, result.Failed);
            Assert.Equal(1, result.ExitCode);
            Assert.Null(result.Error);

            Assert.Single(result.Suites);
            var suite = result.Suites[0];
            Assert.Equal("FB_WireRecordTests", suite.Name);
            Assert.Equal(@"C:\proj\FB_WireRecordTests.TcPOU", suite.FilePath);
            Assert.Null(suite.Error);
            Assert.Equal(12, suite.DurationMs);

            Assert.Equal(2, suite.Tests.Count);
            Assert.True(suite.Tests[0].Passed);
            Assert.Empty(suite.Tests[0].Failures);
            Assert.Equal(3, suite.Tests[0].DurationMs);

            Assert.False(suite.Tests[1].Passed);
            Assert.Equal("RetriesOnTimeout", suite.Tests[1].Name);
            Assert.Equal(9, suite.Tests[1].DurationMs);
            // TcXunit-3tx.2: failures are objects now; `message` still holds
            // the same formatted line the bare string used to be, which is what
            // the results tree renders.
            var failure = Assert.Single(suite.Tests[1].Failures);
            Assert.Equal("FAILED TEST 'RetriesOnTimeout', EXP: 3, ACT: 1", failure.Message);
        }

        [Fact]
        public void Deserialize_SuiteFailedToLoad_MapsErrorWithNoTests()
        {
            const string json = @"{
                ""suites"": [
                    {
                        ""name"": ""FB_ChecksumTests"",
                        ""filePath"": ""C:\\proj\\FB_ChecksumTests.TcPOU"",
                        ""error"": ""Unresolved type FB_CrcHelper referenced from VAR block."",
                        ""tests"": []
                    }
                ],
                ""passed"": 0,
                ""failed"": 1,
                ""exitCode"": 1
            }";

            var result = JsonSerializer.Deserialize<TcxunitRunResult>(json, Options);

            var suite = Assert.Single(result.Suites);
            Assert.Equal("Unresolved type FB_CrcHelper referenced from VAR block.", suite.Error);
            Assert.Empty(suite.Tests);
        }

        [Fact]
        public void Deserialize_SuiteFailedToLoad_WithExplicitNullDurationMs_LeavesDurationMsNull()
        {
            // TcXunit-6fb.2: a suite that failed to load never ran, so the CLI
            // emits durationMs: null rather than a fabricated 0 -- this model's
            // long? must round-trip that null rather than throwing or coercing
            // it to 0.
            const string json = @"{
                ""suites"": [
                    {
                        ""name"": ""FB_ChecksumTests"",
                        ""filePath"": ""C:\\proj\\FB_ChecksumTests.TcPOU"",
                        ""error"": ""Unresolved type FB_CrcHelper referenced from VAR block."",
                        ""durationMs"": null,
                        ""tests"": []
                    }
                ],
                ""passed"": 0,
                ""failed"": 1,
                ""exitCode"": 1
            }";

            var result = JsonSerializer.Deserialize<TcxunitRunResult>(json, Options);

            var suite = Assert.Single(result.Suites);
            Assert.Null(suite.DurationMs);
        }

        [Fact]
        public void Deserialize_SuiteWithoutDurationMsField_LeavesDurationMsNull()
        {
            // Belt-and-braces alongside the explicit-null case above: a payload
            // that simply omits durationMs (rather than emitting it as JSON
            // null) must also leave the nullable long at its default of null,
            // not throw.
            const string json = @"{
                ""suites"": [ { ""name"": ""FB_ChecksumTests"", ""tests"": [] } ],
                ""passed"": 0,
                ""failed"": 0,
                ""exitCode"": 0
            }";

            var result = JsonSerializer.Deserialize<TcxunitRunResult>(json, Options);

            var suite = Assert.Single(result.Suites);
            Assert.Null(suite.DurationMs);
        }

        [Fact]
        public void Deserialize_TestDurationMs_MapsNonNegativeLong()
        {
            // TcXunit-6fb.1: every test entry that appears in the JSON ran (a
            // suite that failed to load has no test entries at all), so
            // TcxunitTestResult.DurationMs is a plain non-nullable long.
            const string json = @"{
                ""suites"": [
                    {
                        ""name"": ""FB_WireRecordTests"",
                        ""tests"": [
                            { ""name"": ""EncodesSingleRecord"", ""passed"": true, ""failures"": [], ""durationMs"": 0 }
                        ]
                    }
                ],
                ""passed"": 1,
                ""failed"": 0,
                ""exitCode"": 0
            }";

            var result = JsonSerializer.Deserialize<TcxunitRunResult>(json, Options);

            var suite = Assert.Single(result.Suites);
            var test = Assert.Single(suite.Tests);
            Assert.Equal(0L, test.DurationMs);
        }

        [Fact]
        public void Deserialize_EarlyExitErrorShape_MapsTopLevelError()
        {
            const string json = @"{ ""error"": ""no TcUnit suites found under /path"" }";

            var result = JsonSerializer.Deserialize<TcxunitRunResult>(json, Options);

            Assert.Equal("no TcUnit suites found under /path", result.Error);
            Assert.Null(result.Suites);
        }

        [Fact]
        public void Deserialize_SuiteFailedWithCallStack_MapsFramesInnermostFirst()
        {
            // TcXunit-7s6: suites[].callStack, innermost frame first, suite
            // entry point last -- TcXunit-9fs's model must round-trip it.
            const string json = @"{
                ""suites"": [
                    {
                        ""name"": ""FB_ChecksumTests"",
                        ""filePath"": ""C:\\proj\\FB_ChecksumTests.TcPOU"",
                        ""error"": ""F_nCheckSum(1): Object reference not set to an instance of an object."",
                        ""tests"": [],
                        ""callStack"": [
                            { ""pouTypeName"": ""F_nCheckSum"", ""methodName"": null, ""line"": 12, ""bodyLine"": 1 },
                            { ""pouTypeName"": ""FB_ChecksumHelper"", ""methodName"": ""Compute"", ""line"": 30, ""bodyLine"": 4 },
                            { ""pouTypeName"": ""FB_ChecksumTests"", ""methodName"": null, ""line"": 8, ""bodyLine"": null }
                        ]
                    }
                ],
                ""passed"": 0,
                ""failed"": 1,
                ""exitCode"": 1
            }";

            var result = JsonSerializer.Deserialize<TcxunitRunResult>(json, Options);

            var suite = Assert.Single(result.Suites);
            Assert.Equal(3, suite.CallStack.Count);

            Assert.Equal("F_nCheckSum", suite.CallStack[0].PouTypeName);
            Assert.Null(suite.CallStack[0].MethodName);
            Assert.Equal(12, suite.CallStack[0].Line);
            Assert.Equal(1, suite.CallStack[0].BodyLine);

            Assert.Equal("FB_ChecksumHelper", suite.CallStack[1].PouTypeName);
            Assert.Equal("Compute", suite.CallStack[1].MethodName);

            Assert.Equal("FB_ChecksumTests", suite.CallStack[2].PouTypeName);
            Assert.Null(suite.CallStack[2].BodyLine);
        }

        [Fact]
        public void Deserialize_SuiteWithoutCallStackField_LeavesCallStackNull()
        {
            // A passing suite or a load-level failure that never entered an
            // interpreted ST body -- the CLI omits callStack entirely rather
            // than emitting an empty array.
            const string json = @"{
                ""suites"": [ { ""name"": ""FB_WireRecordTests"", ""tests"": [] } ],
                ""passed"": 0,
                ""failed"": 0,
                ""exitCode"": 0
            }";

            var result = JsonSerializer.Deserialize<TcxunitRunResult>(json, Options);

            var suite = Assert.Single(result.Suites);
            Assert.Null(suite.CallStack);
        }

        [Fact]
        public void Deserialize_SuiteWithoutFilePath_LeavesFilePathNull()
        {
            // filePath is always emitted by the current CLI (TcXunit-8gj), but the
            // model must not throw if a field is simply absent from the payload --
            // deserializers leave unset properties at their default.
            const string json = @"{
                ""suites"": [ { ""name"": ""FB_NoPathTests"", ""tests"": [] } ],
                ""passed"": 0,
                ""failed"": 0,
                ""exitCode"": 0
            }";

            var result = JsonSerializer.Deserialize<TcxunitRunResult>(json, Options);

            var suite = Assert.Single(result.Suites);
            Assert.Null(suite.FilePath);
        }
    }
}
