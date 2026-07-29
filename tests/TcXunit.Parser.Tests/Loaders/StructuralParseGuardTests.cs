using System;
using System.Xml;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Parser.Tests
{
    // TcXunit-jql: StructuralParseGuard.TryParseOrSkip is the shared "skip
    // this one file, keep scanning the rest" boundary every multi-file loader
    // (CliRunner's own POU loop, DutStructLoader, GvlLoader, DutAliasLoader)
    // relies on. Before this ticket its catch filter only recognized
    // XmlException/NullReferenceException - the two shapes a malformed/short
    // .TcPOU actually produces - so any OTHER exception type raised while
    // parsing a single file (an IOException from a file that disappeared or
    // got locked mid-scan, or any parser failure mode nobody had hit yet)
    // propagated straight out of this method uncaught, taking the entire
    // remaining scan down with it: every other file after the bad one -
    // including a perfectly good sibling suite - never got parsed at all.
    // That silent, total loss of every later file is indistinguishable from
    // "the run stopped after one suite failed" from the caller's side, which
    // is exactly the bug TcXunit-jql investigated.
    public class StructuralParseGuardTests
    {
        [Fact]
        public void TryParseOrSkip_ParseThrowsXmlException_ReturnsFalseWithSkip()
        {
            var ok = StructuralParseGuard.TryParseOrSkip<string>(
                "FB_Bad.TcPOU",
                () => throw new XmlException("malformed"),
                out var result,
                out var skipped);

            Assert.False(ok);
            Assert.Null(result);
            Assert.Equal("FB_Bad.TcPOU", skipped.FileKey);
            Assert.Contains("malformed", skipped.Message);
        }

        [Fact]
        public void TryParseOrSkip_ParseThrowsNullReferenceException_ReturnsFalseWithSkip()
        {
            var ok = StructuralParseGuard.TryParseOrSkip<string>(
                "FB_Bad.TcPOU",
                () => throw new NullReferenceException("missing element"),
                out var result,
                out var skipped);

            Assert.False(ok);
            Assert.Contains("missing element", skipped.Message);
        }

        // TcXunit-jql: the actual regression. Before the fix, this exception
        // type (deliberately something other than XmlException/
        // NullReferenceException/TcPouRejectedException - an IOException
        // stands in for "the file vanished/got locked mid-scan", but any
        // other type not on the old allow-list reproduces the same escape)
        // propagated straight out of TryParseOrSkip instead of becoming a
        // skip, aborting the whole multi-file scan for every caller (see
        // CliRunner.Run's foreach over MultiDirectoryPouLoader.FindPouFiles).
        [Fact]
        public void TryParseOrSkip_ParseThrowsUnanticipatedExceptionType_ReturnsFalseWithSkipInsteadOfPropagating()
        {
            var ok = StructuralParseGuard.TryParseOrSkip<string>(
                "FB_Bad.TcPOU",
                () => throw new System.IO.IOException("file vanished mid-scan"),
                out var result,
                out var skipped);

            Assert.False(ok);
            Assert.Null(result);
            Assert.Equal("FB_Bad.TcPOU", skipped.FileKey);
            Assert.Contains("file vanished mid-scan", skipped.Message);
        }

        [Fact]
        public void TryParseOrSkip_ParseThrowsAnotherUnanticipatedExceptionType_ReturnsFalseWithSkipInsteadOfPropagating()
        {
            var ok = StructuralParseGuard.TryParseOrSkip<string>(
                "FB_Bad.TcPOU",
                () => throw new InvalidOperationException("unexpected shape"),
                out var result,
                out var skipped);

            Assert.False(ok);
            Assert.Contains("unexpected shape", skipped.Message);
        }

        // TcPouRejectedException keeps its own distinct handling: CliRunner.Run
        // catches it separately (a different, already-informative message
        // shape - "'X' uses 'Y', which is outside the v1 parse subset...")
        // rather than folding it into this guard's generic
        // "Failed to parse '<file>': ..." wrapper, so it must still propagate
        // out of TryParseOrSkip uncaught.
        [Fact]
        public void TryParseOrSkip_ParseThrowsTcPouRejectedException_PropagatesUncaught()
        {
            Assert.Throws<TcPouRejectedException>(() =>
                StructuralParseGuard.TryParseOrSkip<string>(
                    "FB_Bad.TcPOU",
                    () => throw new TcPouRejectedException("outside the v1 parse subset"),
                    out _,
                    out _));
        }

        [Fact]
        public void TryParseOrSkip_ParseSucceeds_ReturnsTrueWithResult()
        {
            var ok = StructuralParseGuard.TryParseOrSkip(
                "FB_Good.TcPOU",
                () => "parsed-value",
                out var result,
                out var skipped);

            Assert.True(ok);
            Assert.Equal("parsed-value", result);
            Assert.Null(skipped.FileKey);
        }
    }
}
