using System.Text.Json;
using xStunit.Vsix.TestRunner;
using Xunit;

namespace xStunit.Vsix.Tests
{
    // The only guard on the JSON wire shape xStunit.Cli emits. The extension reads
    // that JSON out of process and has no ProjectReference to the CLI, so renaming a
    // key there breaks no build -- the property just comes back null and the results
    // tree renders a blank. XstunitModels.cs is source-linked here (see the csproj)
    // and the options below match XstunitProcessRunner's, so what is pinned is the
    // models' shape, not the serializer filling them.
    public class XstunitModelsDeserializationTests
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

            var result = JsonSerializer.Deserialize<XstunitRunResult>(json, Options);

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

            var result = JsonSerializer.Deserialize<XstunitRunResult>(json, Options);

            var suite = Assert.Single(result.Suites);
            Assert.Equal("Unresolved type FB_CrcHelper referenced from VAR block.", suite.Error);
            Assert.Empty(suite.Tests);
        }

        [Fact]
        public void Deserialize_SuiteFailedToLoad_WithExplicitNullDurationMs_LeavesDurationMsNull()
        {
            // A suite that failed to load never ran, so the CLI emits durationMs:
            // null rather than a fabricated 0; a UI showing "0 ms" for a suite that
            // never executed would be a lie.
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

            var result = JsonSerializer.Deserialize<XstunitRunResult>(json, Options);

            var suite = Assert.Single(result.Suites);
            Assert.Null(suite.DurationMs);
        }

        [Fact]
        public void Deserialize_SuiteWithoutDurationMsField_LeavesDurationMsNull()
        {
            const string json = @"{
                ""suites"": [ { ""name"": ""FB_ChecksumTests"", ""tests"": [] } ],
                ""passed"": 0,
                ""failed"": 0,
                ""exitCode"": 0
            }";

            var result = JsonSerializer.Deserialize<XstunitRunResult>(json, Options);

            var suite = Assert.Single(result.Suites);
            Assert.Null(suite.DurationMs);
        }

        [Fact]
        public void Deserialize_TestDurationMs_MapsNonNegativeLong()
        {
            // Every test entry the CLI emits ran -- a suite that failed to load
            // emits no test entries at all -- which is why this duration is a
            // plain long while the suite's is nullable.
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

            var result = JsonSerializer.Deserialize<XstunitRunResult>(json, Options);

            var suite = Assert.Single(result.Suites);
            var test = Assert.Single(suite.Tests);
            Assert.Equal(0L, test.DurationMs);
        }

        [Fact]
        public void Deserialize_EarlyExitErrorShape_MapsTopLevelError()
        {
            const string json = @"{ ""error"": ""no TcUnit suites found under /path"", ""kind"": ""load-error"" }";

            var result = JsonSerializer.Deserialize<XstunitRunResult>(json, Options);

            Assert.Equal("no TcUnit suites found under /path", result.Error);
            Assert.Equal("load-error", result.Kind);
            Assert.Null(result.Suites);
        }

        // A suite error reuses the same "kind"/"construct" keys the top-level error
        // shape uses, so a consumer reads them identically at every level of the JSON.
        [Fact]
        public void Deserialize_SuiteError_MapsKindAndConstructUnderTheSharedKeys()
        {
            const string json = @"{
                ""suites"": [
                    {
                        ""name"": ""FB_SelSuiteTests"",
                        ""error"": ""TcUnit native call 'SEL' isn't supported yet"",
                        ""kind"": ""unsupported-construct"",
                        ""construct"": ""SEL"",
                        ""tests"": []
                    }
                ],
                ""passed"": 0,
                ""failed"": 1,
                ""exitCode"": 1
            }";

            var result = JsonSerializer.Deserialize<XstunitRunResult>(json, Options);

            var suite = Assert.Single(result.Suites);
            Assert.Equal("unsupported-construct", suite.Kind);
            Assert.Equal("SEL", suite.Construct);
        }

        // A parse error carries its offending token in the same `construct` field the
        // other kinds use, so a renderer needs no extra branch for it.
        [Fact]
        public void Deserialize_SuiteParseError_MapsTheOffendingTokenAsTheConstruct()
        {
            const string json = @"{
                ""suites"": [
                    {
                        ""name"": ""FB_UnreadableTests"",
                        ""error"": ""FB_UnreadableTests: xStunit could not read this body at line 2"",
                        ""kind"": ""parse-error"",
                        ""construct"": ""@"",
                        ""tests"": []
                    }
                ],
                ""passed"": 0,
                ""failed"": 1,
                ""exitCode"": 1
            }";

            var result = JsonSerializer.Deserialize<XstunitRunResult>(json, Options);

            var suite = Assert.Single(result.Suites);
            Assert.Equal("parse-error", suite.Kind);
            Assert.Equal("@", suite.Construct);
        }

        // A passing suite emits neither key; both must land as null rather than as an
        // empty string a renderer would read as "there is a kind".
        [Fact]
        public void Deserialize_SuiteWithoutKindOrConstruct_LeavesBothNull()
        {
            const string json = @"{
                ""suites"": [ { ""name"": ""FB_PassingTests"", ""tests"": [] } ],
                ""passed"": 1,
                ""failed"": 0,
                ""exitCode"": 0
            }";

            var result = JsonSerializer.Deserialize<XstunitRunResult>(json, Options);

            var suite = Assert.Single(result.Suites);
            Assert.Null(suite.Kind);
            Assert.Null(suite.Construct);
        }

        [Fact]
        public void Deserialize_SuiteFailedWithCallStack_MapsFramesInnermostFirst()
        {
            // The CLI orders callStack innermost frame first, suite entry point
            // last; reversing it would point the reader at the wrong line.
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

            var result = JsonSerializer.Deserialize<XstunitRunResult>(json, Options);

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
            // Where nothing interpreted ever ran, the CLI omits callStack entirely
            // rather than emitting an empty array.
            const string json = @"{
                ""suites"": [ { ""name"": ""FB_WireRecordTests"", ""tests"": [] } ],
                ""passed"": 0,
                ""failed"": 0,
                ""exitCode"": 0
            }";

            var result = JsonSerializer.Deserialize<XstunitRunResult>(json, Options);

            var suite = Assert.Single(result.Suites);
            Assert.Null(suite.CallStack);
        }

        [Fact]
        public void Deserialize_SuiteWithoutFilePath_LeavesFilePathNull()
        {
            // The current CLI always emits filePath; an older or partial payload that
            // omits it must still load, with navigation-to-source simply unavailable.
            const string json = @"{
                ""suites"": [ { ""name"": ""FB_NoPathTests"", ""tests"": [] } ],
                ""passed"": 0,
                ""failed"": 0,
                ""exitCode"": 0
            }";

            var result = JsonSerializer.Deserialize<XstunitRunResult>(json, Options);

            var suite = Assert.Single(result.Suites);
            Assert.Null(suite.FilePath);
        }
    }
}
