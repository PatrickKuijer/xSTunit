using xStunit.Interpreter;
using xStunit.Runner;
using xStunit.Runner.TcUnitStub;

namespace xStunit.Cli
{
    // The message a consumer reads, followed by what to DO about a failure of
    // that kind: the consumer is usually a model choosing its next edit from one
    // JSON object, and the two possible responses - fix the ST, or stop and
    // escalate - are opposites that the factual half never distinguishes.
    internal static class FailureGuidance
    {
        // The exception is a formatted TcUnit assert line ("FAILED TEST 'X',
        // EXP: 99, ACT: 3, MSG: ..."), reproduced byte for byte from upstream
        // TcUnit's FB_AdsAssertMessageFormatter: it has a verbatim contract, so
        // no guidance may be appended to it. `Expected != null` is what
        // identifies one, because FB_TestSuite.Fail() is both the only path
        // that formats that string and the only path that populates
        // Assert/Expected/Actual/AssertMessage. Keying on the KIND instead
        // would exempt every assertion-kind failure, including ones that never
        // went near the formatter.
        public static string For(AssertionFailure failure) =>
            For(failure.Message, failure.Kind, isVerbatim: failure.Expected != null, failure.Site.BodyLine);

        // Overload for the call sites that have no failure object at all: a
        // suite-level error and the run-level ErrorReport. isVerbatim has no
        // default on purpose - the verbatim exemption must never be something a
        // caller gets by omission.
        public static string For(
            string message, string kind, bool isVerbatim, int bodyLine = PlcSourceLocationException.UnknownLine)
        {
            if (isVerbatim)
                return message;

            var guidance = FailureKind.Guidance(kind);
            if (string.IsNullOrEmpty(guidance))
                return message;

            if (kind == FailureKind.ParseError)
            {
                // Says plainly that the body could not be READ, and that the
                // cause is one of two things the runner genuinely cannot tell
                // apart - it must never assert which.
                var at = bodyLine != PlcSourceLocationException.UnknownLine
                    ? $" at line {bodyLine}"
                    : string.Empty;
                guidance = $"xStunit could not read this body{at} - " +
                    "either it uses ST beyond xStunit's subset, or it is invalid ST. " + guidance;
            }

            // " -- " rather than a space: the factual half often ends in ST
            // punctuation (";", ")") or in raw body text, so a bare space runs
            // the two halves into one sentence. The delimiter is where "what
            // happened" stops and "what to do" starts.
            return string.IsNullOrEmpty(message) ? guidance : message + " -- " + guidance;
        }
    }
}
