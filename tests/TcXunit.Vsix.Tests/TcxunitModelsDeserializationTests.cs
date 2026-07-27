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
                        ""tests"": [
                            { ""name"": ""EncodesSingleRecord"", ""passed"": true, ""failures"": [] },
                            {
                                ""name"": ""RetriesOnTimeout"",
                                ""passed"": false,
                                ""failures"": [ ""FAILED TEST 'RetriesOnTimeout', EXP: 3, ACT: 1"" ]
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

            Assert.Equal(2, suite.Tests.Count);
            Assert.True(suite.Tests[0].Passed);
            Assert.Empty(suite.Tests[0].Failures);

            Assert.False(suite.Tests[1].Passed);
            Assert.Equal("RetriesOnTimeout", suite.Tests[1].Name);
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
