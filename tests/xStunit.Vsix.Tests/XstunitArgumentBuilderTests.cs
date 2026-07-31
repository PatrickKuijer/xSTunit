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
        public void BuildArguments_NullSuiteNames_OmitsSuiteFlag()
        {
            var arguments = XstunitArgumentBuilder.BuildArguments(
                "xstunit",
                new[] { "C:\\proj\\Pous" },
                suiteNames: null);

            Assert.Equal("\"xstunit\" \"C:\\proj\\Pous\" --format json", arguments);
        }

        [Fact]
        public void BuildArguments_EmptySuiteNames_OmitsSuiteFlag()
        {
            var arguments = XstunitArgumentBuilder.BuildArguments(
                "xstunit",
                new[] { "C:\\proj\\Pous" },
                suiteNames: new List<string>());

            Assert.Equal("\"xstunit\" \"C:\\proj\\Pous\" --format json", arguments);
        }

        [Fact]
        public void BuildArguments_OneFailedSuite_AppendsOneSuiteFlag()
        {
            var arguments = XstunitArgumentBuilder.BuildArguments(
                "xstunit",
                new[] { "C:\\proj\\Pous" },
                new[] { "FB_WidgetWireRecordsTests" });

            Assert.Equal(
                "\"xstunit\" \"C:\\proj\\Pous\" --format json --suite \"FB_WidgetWireRecordsTests\"",
                arguments);
        }

        [Fact]
        public void BuildArguments_MultipleFailedSuites_RepeatsSuiteFlagInOrder()
        {
            var arguments = XstunitArgumentBuilder.BuildArguments(
                "xstunit",
                new[] { "C:\\proj\\Pous" },
                new[] { "FB_WidgetWireRecordsTests", "FB_ChecksumTests" });

            Assert.Equal(
                "\"xstunit\" \"C:\\proj\\Pous\" --format json --suite \"FB_WidgetWireRecordsTests\" --suite \"FB_ChecksumTests\"",
                arguments);
        }

        [Fact]
        public void BuildArguments_MultiplePaths_QuotesEachPathSeparately()
        {
            var arguments = XstunitArgumentBuilder.BuildArguments(
                "xstunit",
                new[] { "C:\\proj\\PousA", "C:\\proj\\PousB" },
                suiteNames: null);

            Assert.Equal("\"xstunit\" \"C:\\proj\\PousA\" \"C:\\proj\\PousB\" --format json", arguments);
        }

        [Fact]
        public void BuildArguments_SuiteNameWithSpaces_IsQuotedAsOneToken()
        {
            // Suite type names are IEC 61131-3 identifiers and never contain spaces in
            // practice; the quoting must not rely on that.
            var arguments = XstunitArgumentBuilder.BuildArguments(
                "xstunit",
                new[] { "C:\\proj\\Pous" },
                new[] { "FB_Has Spaces" });

            Assert.Equal(
                "\"xstunit\" \"C:\\proj\\Pous\" --format json --suite \"FB_Has Spaces\"",
                arguments);
        }

        [Fact]
        public void BuildArguments_NullPluginsDirectory_OmitsPluginsFlag()
        {
            var arguments = XstunitArgumentBuilder.BuildArguments(
                "xstunit",
                new[] { "C:\\proj\\Pous" },
                suiteNames: null,
                pluginsDirectory: null);

            Assert.Equal("\"xstunit\" \"C:\\proj\\Pous\" --format json", arguments);
        }

        [Fact]
        public void BuildArguments_EmptyPluginsDirectory_OmitsPluginsFlag()
        {
            var arguments = XstunitArgumentBuilder.BuildArguments(
                "xstunit",
                new[] { "C:\\proj\\Pous" },
                suiteNames: null,
                pluginsDirectory: string.Empty);

            Assert.Equal("\"xstunit\" \"C:\\proj\\Pous\" --format json", arguments);
        }

        [Fact]
        public void BuildArguments_PluginsDirectorySet_AppendsPluginsFlag()
        {
            var arguments = XstunitArgumentBuilder.BuildArguments(
                "xstunit",
                new[] { "C:\\proj\\Pous" },
                suiteNames: null,
                pluginsDirectory: "C:\\proj\\Plugins");

            Assert.Equal(
                "\"xstunit\" \"C:\\proj\\Pous\" --format json --plugins \"C:\\proj\\Plugins\"",
                arguments);
        }

        [Fact]
        public void BuildArguments_PluginsDirectoryAndSuiteNames_AppendsPluginsBeforeSuites()
        {
            var arguments = XstunitArgumentBuilder.BuildArguments(
                "xstunit",
                new[] { "C:\\proj\\Pous" },
                new[] { "FB_WidgetWireRecordsTests" },
                pluginsDirectory: "C:\\proj\\Plugins");

            Assert.Equal(
                "\"xstunit\" \"C:\\proj\\Pous\" --format json --plugins \"C:\\proj\\Plugins\" --suite \"FB_WidgetWireRecordsTests\"",
                arguments);
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
