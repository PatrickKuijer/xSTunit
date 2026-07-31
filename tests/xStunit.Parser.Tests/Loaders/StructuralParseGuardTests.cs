using System;
using System.Xml;
using xStunit.Parser;
using Xunit;

namespace xStunit.Parser.Tests
{
    // These pin the per-file isolation every multi-file loader depends on: an
    // exception type that escapes TryParseOrSkip instead of becoming a skip
    // aborts the whole scan, so every file after the bad one - good sibling
    // suites included - is silently never parsed.
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

        // The exception type here is deliberately one no parser is expected to
        // raise: narrowing the guard to an enumerated list of "expected" types
        // is what lets a scan-killing escape back in.
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

        // The one exclusion: callers report an unsupported-construct rejection
        // with their own message shape, which the guard's generic "Failed to
        // parse '<file>'" wrapper would swallow.
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
