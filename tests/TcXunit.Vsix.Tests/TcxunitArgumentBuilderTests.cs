using System.Collections.Generic;
using TcXunit.Vsix.TestRunner;
using Xunit;

namespace TcXunit.Vsix.Tests
{
    // TcXunit-1tt.8's rerun-failed feature re-invokes tcxunit with --suite <name>
    // repeated once per failed suite (TcXunit-6fb.3's CLI flag). The epic's own
    // Testing Decisions call this out explicitly as in-scope for this project:
    // "--suite argument construction for rerun-failed". TcxunitArgumentBuilder is
    // the pure (no JavaScriptSerializer/VS SDK dependency) class that construction
    // was pulled out into so it's testable under net8.0 -- see
    // TcXunit.Vsix.Tests.csproj's own comment on why TcxunitProcessRunner.cs itself
    // can't be source-linked here.
    public class TcxunitArgumentBuilderTests
    {
        [Fact]
        public void BuildArguments_NullSuiteNames_OmitsSuiteFlag()
        {
            var arguments = TcxunitArgumentBuilder.BuildArguments(
                "tcxunit",
                new[] { "C:\\proj\\Pous" },
                suiteNames: null);

            Assert.Equal("\"tcxunit\" \"C:\\proj\\Pous\" --format json", arguments);
        }

        [Fact]
        public void BuildArguments_EmptySuiteNames_OmitsSuiteFlag()
        {
            var arguments = TcxunitArgumentBuilder.BuildArguments(
                "tcxunit",
                new[] { "C:\\proj\\Pous" },
                suiteNames: new List<string>());

            Assert.Equal("\"tcxunit\" \"C:\\proj\\Pous\" --format json", arguments);
        }

        [Fact]
        public void BuildArguments_OneFailedSuite_AppendsOneSuiteFlag()
        {
            var arguments = TcxunitArgumentBuilder.BuildArguments(
                "tcxunit",
                new[] { "C:\\proj\\Pous" },
                new[] { "FB_RemoteWireRecordsTests" });

            Assert.Equal(
                "\"tcxunit\" \"C:\\proj\\Pous\" --format json --suite \"FB_RemoteWireRecordsTests\"",
                arguments);
        }

        [Fact]
        public void BuildArguments_MultipleFailedSuites_RepeatsSuiteFlagInOrder()
        {
            var arguments = TcxunitArgumentBuilder.BuildArguments(
                "tcxunit",
                new[] { "C:\\proj\\Pous" },
                new[] { "FB_RemoteWireRecordsTests", "FB_ChecksumTests" });

            Assert.Equal(
                "\"tcxunit\" \"C:\\proj\\Pous\" --format json --suite \"FB_RemoteWireRecordsTests\" --suite \"FB_ChecksumTests\"",
                arguments);
        }

        [Fact]
        public void BuildArguments_MultiplePaths_QuotesEachPathSeparately()
        {
            var arguments = TcxunitArgumentBuilder.BuildArguments(
                "tcxunit",
                new[] { "C:\\proj\\PousA", "C:\\proj\\PousB" },
                suiteNames: null);

            Assert.Equal("\"tcxunit\" \"C:\\proj\\PousA\" \"C:\\proj\\PousB\" --format json", arguments);
        }

        [Fact]
        public void BuildArguments_SuiteNameWithSpaces_IsQuotedAsOneToken()
        {
            // Suite type names are IEC 61131-3 identifiers and never contain spaces in
            // practice, but the quoting must not assume that -- same defensive stance
            // EscapeArgument already takes for paths.
            var arguments = TcxunitArgumentBuilder.BuildArguments(
                "tcxunit",
                new[] { "C:\\proj\\Pous" },
                new[] { "FB_Has Spaces" });

            Assert.Equal(
                "\"tcxunit\" \"C:\\proj\\Pous\" --format json --suite \"FB_Has Spaces\"",
                arguments);
        }

        [Fact]
        public void EscapeArgument_TrailingBackslash_DoublesItSoClosingQuoteSurvives()
        {
            // Win32/CommandLineToArgvW rule: a run of backslashes immediately before the
            // closing quote must be doubled, or the last backslash escapes the quote
            // instead of terminating the token. Regression coverage for the exact bug
            // class BuildStartInfo's own comment (TcxunitProcessRunner.cs) describes.
            var escaped = TcxunitArgumentBuilder.EscapeArgument("C:\\proj\\Pous\\");

            Assert.Equal("\"C:\\proj\\Pous\\\\\"", escaped);
        }

        [Fact]
        public void EscapeArgument_EmbeddedQuote_IsEscaped()
        {
            var escaped = TcxunitArgumentBuilder.EscapeArgument("FB_Has\"Quote");

            Assert.Equal("\"FB_Has\\\"Quote\"", escaped);
        }
    }
}
