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
                                ""failures"": [ ""FAILED TEST 'RetriesOnTimeout', EXP: 3, ACT: 1"" ],
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
            var failure = Assert.Single(suite.Tests[1].Failures);
            Assert.Equal("FAILED TEST 'RetriesOnTimeout', EXP: 3, ACT: 1", failure);
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
