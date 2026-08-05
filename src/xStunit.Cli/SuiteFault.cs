using System;
using xStunit.Interpreter;
using xStunit.Runner;

namespace xStunit.Cli
{
    // A suite-level exception, classified once so every output format reports
    // the same thing about it. The message is prose for a human and is never
    // what a consumer switches on; Kind is.
    internal sealed class SuiteFault
    {
        private SuiteFault(
            Exception exception, PlcSourceLocationException located, string error, string kind, string construct)
        {
            Exception = exception;
            Located = located;
            Error = error;
            Kind = kind;
            Construct = construct;
        }

        public static SuiteFault Classify(Exception ex)
        {
            // Non-null only when an interpreted ST body actually faulted; a
            // load-level failure (an unresolvable type in default-value
            // construction, say) has no PLC location, and every
            // location-derived field stays null for it.
            var located = ex as PlcSourceLocationException;
            // FailureKind.Assertion is a legitimate answer here, not only on
            // the per-test path: an assertion that escapes the
            // TEST()/TEST_FINISHED() bracket has no test to charge and lands as
            // a suite-level error. Re-homing it as FailureKind.PlcFault would
            // claim the PLC faulted, which is false, and would trade the
            // assertion guidance for advice to go fix code under test that is
            // not what broke.
            var kind = FailureClassifier.Classify(ex, out var construct);
            // A parse error's body line comes from the front end's own
            // structured field, never from re-parsing ex.Message.
            var bodyLine = kind == FailureKind.ParseError
                ? FailureClassifier.UnwrapParseException(ex)?.BodyLine ?? PlcSourceLocationException.UnknownLine
                : PlcSourceLocationException.UnknownLine;
            // Guidance is appended so the JSON object is self-contained: a
            // consumer never has to have read this repo to know whether to edit
            // the POU or stop and escalate.
            var error = FailureGuidance.For(ex.Message, kind, isVerbatim: false, bodyLine);
            return new SuiteFault(ex, located, error, kind, construct);
        }

        public Exception Exception { get; }

        public PlcSourceLocationException Located { get; }

        public string Error { get; }

        public string Kind { get; }

        public string Construct { get; }
    }
}
