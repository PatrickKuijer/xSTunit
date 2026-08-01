using System.Collections.Generic;
using xStunit.Vsix.TestRunner;
using Xunit;

namespace xStunit.Vsix.Tests
{
    // XstunitArgumentBuilder.cs is source-linked into this net8.0 project (see the
    // csproj) because the extension it ships in targets net472 and does not build
    // outside Visual Studio. These are the only tests of the command line the
    // extension hands to xstunit.exe; a mistake there breaks no build, it surfaces
    // only as a bad invocation at runtime inside the IDE.
    public class XstunitArgumentBuilderTests
    {
        [Fact]
        public void BuildStreamingArguments_NullSuiteNames_OmitsSuiteFlag()
        {
            var arguments = XstunitArgumentBuilder.BuildStreamingArguments(
                "xstunit",
                new[] { "C:\\proj\\Pous" },
                suiteNames: null);

            Assert.Equal("\"xstunit\" \"C:\\proj\\Pous\" --stream", arguments);
        }

        [Fact]
        public void BuildStreamingArguments_EmptySuiteNames_OmitsSuiteFlag()
        {
            var arguments = XstunitArgumentBuilder.BuildStreamingArguments(
                "xstunit",
                new[] { "C:\\proj\\Pous" },
                suiteNames: new List<string>());

            Assert.Equal("\"xstunit\" \"C:\\proj\\Pous\" --stream", arguments);
        }

        [Fact]
        public void BuildStreamingArguments_OneFailedSuite_AppendsOneSuiteFlag()
        {
            var arguments = XstunitArgumentBuilder.BuildStreamingArguments(
                "xstunit",
                new[] { "C:\\proj\\Pous" },
                new[] { "FB_WidgetWireRecordsTests" });

            Assert.Equal(
                "\"xstunit\" \"C:\\proj\\Pous\" --stream --suite \"FB_WidgetWireRecordsTests\"",
                arguments);
        }

        [Fact]
        public void BuildStreamingArguments_MultipleFailedSuites_RepeatsSuiteFlagInOrder()
        {
            var arguments = XstunitArgumentBuilder.BuildStreamingArguments(
                "xstunit",
                new[] { "C:\\proj\\Pous" },
                new[] { "FB_WidgetWireRecordsTests", "FB_ChecksumTests" });

            Assert.Equal(
                "\"xstunit\" \"C:\\proj\\Pous\" --stream --suite \"FB_WidgetWireRecordsTests\" --suite \"FB_ChecksumTests\"",
                arguments);
        }

        [Fact]
        public void BuildStreamingArguments_MultiplePaths_QuotesEachPathSeparately()
        {
            var arguments = XstunitArgumentBuilder.BuildStreamingArguments(
                "xstunit",
                new[] { "C:\\proj\\PousA", "C:\\proj\\PousB" },
                suiteNames: null);

            Assert.Equal("\"xstunit\" \"C:\\proj\\PousA\" \"C:\\proj\\PousB\" --stream", arguments);
        }

        [Fact]
        public void BuildStreamingArguments_SuiteNameWithSpaces_IsQuotedAsOneToken()
        {
            // Suite type names are IEC 61131-3 identifiers and never contain spaces in
            // practice; the quoting must not rely on that.
            var arguments = XstunitArgumentBuilder.BuildStreamingArguments(
                "xstunit",
                new[] { "C:\\proj\\Pous" },
                new[] { "FB_Has Spaces" });

            Assert.Equal(
                "\"xstunit\" \"C:\\proj\\Pous\" --stream --suite \"FB_Has Spaces\"",
                arguments);
        }

        [Fact]
        public void BuildStreamingArguments_NullPluginsDirectory_OmitsPluginsFlag()
        {
            var arguments = XstunitArgumentBuilder.BuildStreamingArguments(
                "xstunit",
                new[] { "C:\\proj\\Pous" },
                suiteNames: null,
                pluginsDirectory: null);

            Assert.Equal("\"xstunit\" \"C:\\proj\\Pous\" --stream", arguments);
        }

        [Fact]
        public void BuildStreamingArguments_EmptyPluginsDirectory_OmitsPluginsFlag()
        {
            var arguments = XstunitArgumentBuilder.BuildStreamingArguments(
                "xstunit",
                new[] { "C:\\proj\\Pous" },
                suiteNames: null,
                pluginsDirectory: string.Empty);

            Assert.Equal("\"xstunit\" \"C:\\proj\\Pous\" --stream", arguments);
        }

        [Fact]
        public void BuildStreamingArguments_PluginsDirectorySet_AppendsPluginsFlag()
        {
            var arguments = XstunitArgumentBuilder.BuildStreamingArguments(
                "xstunit",
                new[] { "C:\\proj\\Pous" },
                suiteNames: null,
                pluginsDirectory: "C:\\proj\\Plugins");

            Assert.Equal(
                "\"xstunit\" \"C:\\proj\\Pous\" --stream --plugins \"C:\\proj\\Plugins\"",
                arguments);
        }

        [Fact]
        public void BuildStreamingArguments_PluginsDirectoryAndSuiteNames_AppendsPluginsBeforeSuites()
        {
            var arguments = XstunitArgumentBuilder.BuildStreamingArguments(
                "xstunit",
                new[] { "C:\\proj\\Pous" },
                new[] { "FB_WidgetWireRecordsTests" },
                pluginsDirectory: "C:\\proj\\Plugins");

            Assert.Equal(
                "\"xstunit\" \"C:\\proj\\Pous\" --stream --plugins \"C:\\proj\\Plugins\" --suite \"FB_WidgetWireRecordsTests\"",
                arguments);
        }

        // The command line the runner retries with when the streaming one produced no
        // NDJSON at all -- a CLI predating --stream reads that flag as a directory name
        // and dies. It is the line the extension shipped before streaming existed, so
        // it must keep working unchanged; only the output flag may differ from the
        // streaming form.
        [Fact]
        public void BuildJsonArguments_NoSuitesOrPlugins_AsksForTheBufferedBlob()
        {
            var arguments = XstunitArgumentBuilder.BuildJsonArguments(
                "xstunit",
                new[] { "C:\\proj\\Pous" },
                suiteNames: null);

            Assert.Equal("\"xstunit\" \"C:\\proj\\Pous\" --format json", arguments);
        }

        [Fact]
        public void BuildJsonArguments_PluginsDirectoryAndSuiteNames_AppendsPluginsBeforeSuites()
        {
            var arguments = XstunitArgumentBuilder.BuildJsonArguments(
                "xstunit",
                new[] { "C:\\proj\\PousA", "C:\\proj\\PousB" },
                new[] { "FB_WidgetWireRecordsTests", "FB_ChecksumTests" },
                pluginsDirectory: "C:\\proj\\Plugins");

            Assert.Equal(
                "\"xstunit\" \"C:\\proj\\PousA\" \"C:\\proj\\PousB\" --format json --plugins \"C:\\proj\\Plugins\" "
                    + "--suite \"FB_WidgetWireRecordsTests\" --suite \"FB_ChecksumTests\"",
                arguments);
        }

        // The retry must re-run the same work, or the fallback would silently change
        // what a run covers on an older CLI.
        [Fact]
        public void BuildJsonArguments_DiffersFromStreamingOnlyInTheOutputFlag()
        {
            var paths = new[] { "C:\\proj\\Pous" };
            var suiteNames = new[] { "FB_ChecksumTests" };

            var streaming = XstunitArgumentBuilder.BuildStreamingArguments(
                "xstunit", paths, suiteNames, "C:\\proj\\Plugins");
            var json = XstunitArgumentBuilder.BuildJsonArguments(
                "xstunit", paths, suiteNames, "C:\\proj\\Plugins");

            Assert.Equal(streaming.Replace("--stream", "--format json"), json);
        }

        [Fact]
        public void EscapeArgument_TrailingBackslash_DoublesItSoClosingQuoteSurvives()
        {
            // Win32/CommandLineToArgvW rule: a run of backslashes immediately before the
            // closing quote must be doubled, or the last backslash escapes the quote
            // instead of terminating the token.
            var escaped = XstunitArgumentBuilder.EscapeArgument("C:\\proj\\Pous\\");

            Assert.Equal("\"C:\\proj\\Pous\\\\\"", escaped);
        }

        [Fact]
        public void EscapeArgument_EmbeddedQuote_IsEscaped()
        {
            var escaped = XstunitArgumentBuilder.EscapeArgument("FB_Has\"Quote");

            Assert.Equal("\"FB_Has\\\"Quote\"", escaped);
        }
    }
}
