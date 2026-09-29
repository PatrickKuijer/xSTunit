using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using xStunit.Runner;

namespace xStunit.Cli
{
    // The only way a report reaches the wire, so the camelCase policy cannot be
    // left off by a caller: the JSON property names below - not the C# type or
    // member names - are the published contract, read out-of-process by
    // consumers holding no reference to these types (see
    // src/xStunit.Vsix/TestRunner/XstunitModels.cs). Renaming a type here is
    // free; renaming a property is a break.
    //
    // Plain text is a rendering of these same reports rather than a second
    // derivation from the runner's results, so a few properties below carry
    // what only the console needs and are marked [JsonIgnore]: they are part of
    // the model, not of the contract.
    internal static class RunReportJson
    {
        // The `--format json` shape: one indented object for the whole run.
        public static string Blob<T>(T report) => JsonSerializer.Serialize(report, BlobOptions);

        // The `--stream` shape. Compact, unlike Blob above: an indented object
        // spans lines and would break NDJSON's one-line-per-event contract.
        public static string Line<T>(T report) => JsonSerializer.Serialize(report, LineOptions);

        private static readonly JsonSerializerOptions BlobOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static readonly JsonSerializerOptions LineOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    internal sealed class ErrorReport
    {
        public ErrorReport(
            string error,
            IReadOnlyList<SkipReport> skipped,
            IReadOnlyList<WarningReport> warnings,
            IReadOnlyList<CoverageReport> coverage,
            string streamEvent = null)
        {
            Event = streamEvent;
            Error = error;
            Skipped = skipped;
            Warnings = warnings;
            Coverage = coverage;
        }

        // Set only for the --stream NDJSON line; null, and so omitted, from
        // the --format json shape.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Event { get; }

        public string Error { get; }

        // Every error reported through this shape is a usage or discovery
        // failure, so it is always load-error. Emitted as a constant rather
        // than omitted, so a consumer reads `kind` the same way whether the
        // run died before any suite ran or one suite failed inside it.
        public string Kind => FailureKind.LoadError;

        // Skips collected before the error surfaced - e.g. "no suites
        // found" in a tree where every candidate POU was outside the parse
        // subset. Without them the caller sees only "nothing found", with
        // no reason why.
        public IReadOnlyList<SkipReport> Skipped { get; }

        // Declaration lines lost before the error surfaced. Reported for the
        // same reason the skips above are: what was already dropped is part of
        // why the run ended where it did.
        public IReadOnlyList<WarningReport> Warnings { get; }

        // Reported even on a failed run: "no suites found" is a usage error
        // for a run, but for a work list it is the most informative answer
        // there is - every POU in the tree is uncovered.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<CoverageReport> Coverage { get; }
    }

    // FilePath is the full on-disk path, so the offending file stays
    // unambiguous when several merged directories hold same-named POUs.
    internal sealed class SkipReport
    {
        public SkipReport(string filePath, string reason)
        {
            FilePath = filePath;
            Reason = reason;
        }

        public string FilePath { get; }
        public string Reason { get; }
    }

    // A file that loaded but lost declaration lines on the way in, as opposed
    // to a SkipReport's file that produced no types at all. Separate on the
    // wire as well as in the model, so a consumer counting skips to report
    // reduced coverage does not start counting these too.
    internal sealed class WarningReport
    {
        public WarningReport(string filePath, IReadOnlyList<string> lines, IReadOnlyList<RejectionReport> rejections = null)
        {
            FilePath = filePath;
            Lines = lines;
            Rejections = rejections;
        }

        public string FilePath { get; }

        // The declaration lines that could not be read, plus the declarations
        // the runtime refused. Never empty: a file with nothing to report
        // produces no WarningReport at all.
        public IReadOnlyList<string> Lines { get; }

        // The refused declarations among Lines, each with its reason. Omitted
        // from the wire when nothing was refused, so a consumer written before
        // it existed sees the same shape as before.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<RejectionReport> Rejections { get; }
    }

    internal sealed class RejectionReport
    {
        public RejectionReport(string line, string reason)
        {
            Line = line;
            Reason = reason;
        }

        public string Line { get; }
        public string Reason { get; }
    }

    // Root of the `--format json` output, and with it the *Report family
    // below. Kept separate from TestCaseResult/AssertionFailure on purpose:
    // the wire format has to stay stable even when the interpreter's
    // internal model changes.
    internal sealed class RunReport
    {
        public RunReport(
            IReadOnlyList<SuiteReport> suites,
            int passed,
            int failed,
            int exitCode,
            IReadOnlyList<SkipReport> skipped,
            IReadOnlyList<WarningReport> warnings,
            IReadOnlyList<CoverageReport> coverage,
            string streamEvent = null)
        {
            Event = streamEvent;
            Suites = suites;
            Passed = passed;
            Failed = failed;
            ExitCode = exitCode;
            Skipped = skipped;
            Warnings = warnings;
            Coverage = coverage;
        }

        // Set only for --stream's final NDJSON line; null, and so omitted,
        // from the --format json blob.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Event { get; }

        public IReadOnlyList<SuiteReport> Suites { get; }
        public int Passed { get; }
        public int Failed { get; }
        public int ExitCode { get; }

        // Always present, empty when nothing was skipped, so a consumer can
        // read it unconditionally.
        public IReadOnlyList<SkipReport> Skipped { get; }

        // Always present, empty when nothing was lost, so a consumer can read
        // it unconditionally the same way it reads Skipped.
        public IReadOnlyList<WarningReport> Warnings { get; }

        // One entry per non-suite POU. Omitted entirely without --coverage,
        // because an empty list already means something else: that every
        // POU in the tree is uncovered.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<CoverageReport> Coverage { get; }
    }

    // An entry whose `suites` is empty is the interesting one: it is a
    // directly usable next task ("write a suite for F_ComputeChecksum").
    internal sealed class CoverageReport
    {
        public CoverageReport(string pou, IReadOnlyList<string> suites)
        {
            Pou = pou;
            Suites = suites;
        }

        public string Pou { get; }
        public IReadOnlyList<string> Suites { get; }
    }

    // One suite of --stream's first NDJSON line, emitted before any suite
    // runs so a consumer can render the full "waiting" list up front rather
    // than learning the suite count from the final summary.
    internal sealed class SuiteDiscoveryEntry
    {
        public SuiteDiscoveryEntry(string name, string filePath)
        {
            Name = name;
            FilePath = filePath;
        }

        public string Name { get; }
        public string FilePath { get; }
    }

    internal sealed class DiscoveryEvent
    {
        public DiscoveryEvent(IReadOnlyList<SuiteDiscoveryEntry> suites)
        {
            Suites = suites;
        }

        public string Event => StreamEventNames.Discovery;
        public IReadOnlyList<SuiteDiscoveryEntry> Suites { get; }
    }

    // Emitted immediately before a suite runs, and paired with the
    // suite-result line (a SuiteReport with Event="suite-result") emitted
    // once it finishes.
    internal sealed class SuiteStartEvent
    {
        public SuiteStartEvent(string suite)
        {
            Suite = suite;
        }

        public string Event => StreamEventNames.SuiteStart;
        public string Suite { get; }
    }

    internal sealed class SuiteReport
    {
        public SuiteReport(
            string name,
            string filePath,
            string error,
            string detail,
            string kind,
            string construct,
            IReadOnlyList<TestReport> tests,
            long? durationMs,
            int? fileLine,
            IReadOnlyList<CallStackFrameReport> callStack,
            string outcome,
            string streamEvent = null)
        {
            Event = streamEvent;
            Outcome = outcome;
            Name = name;
            FilePath = filePath;
            Error = error;
            Detail = detail;
            Kind = kind;
            Construct = construct;
            Tests = tests;
            DurationMs = durationMs;
            FileLine = fileLine;
            CallStack = callStack;
        }

        // Set only when this report is serialized standalone as one
        // --stream NDJSON line; null, and so omitted, both inside the
        // summary's suites[] array and in the --format json blob.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Event { get; }

        // "pass"/"fail", on every shape that carries a suite - the standalone
        // --stream line and the suites[] entry alike - so no consumer has to
        // decide for itself what a failed suite is. A suite that never ran to
        // completion reports "fail" rather than a third "skip" state: it
        // already counts toward the exit code like any failing TEST(), and
        // `kind` is what says why.
        public string Outcome { get; }

        // The suite reported on its own --stream line and the same suite inside
        // the summary's suites[] are one report; only the discriminator that
        // marks a standalone line differs. Copying rather than rebuilding is
        // what stops the two from describing the suite differently.
        public SuiteReport AsStreamEvent(string streamEvent) =>
            new SuiteReport(
                Name, FilePath, Error, Detail, Kind, Construct, Tests, DurationMs, FileLine, CallStack,
                Outcome, streamEvent);

        public string Name { get; }
        public string FilePath { get; }
        public string Error { get; }

        // The same fault as Error, rendered for a console instead of for a
        // consumer holding one JSON object: no guidance appended, and read as a
        // sentence ("in FB_Y.MethodZ(3): ..."). Null for a suite that didn't
        // fail.
        [JsonIgnore]
        public string Detail { get; }

        // The machine-readable counterpart to Error - one of the
        // FailureKind constants, null for a suite that didn't fail. The
        // distinction that matters to a consumer is unsupported-construct
        // vs. everything else: it means the ST is correct and the runner is
        // behind, so the POU must not be edited.
        public string Kind { get; }

        // The specific construct behind the error: the unimplemented ST
        // construct for an unsupported-construct (e.g. "SEL"), or the
        // offending token for a parse error, so escalation can name it
        // without parsing Error. Null for every other kind, and for a throw
        // site that knew only that something was unsupported.
        public string Construct { get; }
        public IReadOnlyList<TestReport> Tests { get; }

        // Suite-level wall-clock time. Null, not a fabricated 0, when the
        // suite never ran to completion.
        public long? DurationMs { get; }

        // The raw .TcPOU XML line, for a consumer opening the file directly
        // rather than through XAE; Error's own "FB_Y.MethodZ(N): ..." prefix
        // carries the editor-relative line instead. Null for a passing
        // suite, and for a failure with no known PLC location.
        public int? FileLine { get; }

        // The full interpreted call chain behind Error, innermost frame
        // first (CallStack[0] describes the same fault as Error/FileLine
        // above), suite entry point last. Null - not an empty array - for a
        // passing suite or a failure that never entered an ST body.
        public IReadOnlyList<CallStackFrameReport> CallStack { get; }
    }

    internal sealed class CallStackFrameReport
    {
        public CallStackFrameReport(
            string pouTypeName, string methodName, int? line, int? bodyLine, string locationWithLine)
        {
            PouTypeName = pouTypeName;
            MethodName = methodName;
            Line = line;
            BodyLine = bodyLine;
            LocationWithLine = locationWithLine;
        }

        // AssertSite's own "location(line)" rendering, carried rather than
        // recomposed from the fields below: text output prints its frames from
        // this report, and a second composition here is exactly what would let a
        // frame in a console log and the same frame inside an exception message
        // drift apart.
        [JsonIgnore]
        public string LocationWithLine { get; }

        public string PouTypeName { get; }

        // Null for a frame with no method to name: a suite body, a
        // bare-invoked FB body, or a StepCycles cycle.
        public string MethodName { get; }

        // The raw .TcPOU XML line, null when unknown.
        public int? Line { get; }

        // The XAE-implementation-editor-relative line, null when unknown.
        public int? BodyLine { get; }
    }

    internal sealed class TestReport
    {
        public TestReport(string name, bool passed, IReadOnlyList<FailureReport> failures, long durationMs)
        {
            Name = name;
            Passed = passed;
            Failures = failures;
            DurationMs = durationMs;
        }

        public string Name { get; }
        public bool Passed { get; }
        public IReadOnlyList<FailureReport> Failures { get; }
        public long DurationMs { get; }
    }

    // One per-test failure. Message carries the formatted line a human
    // reads; every field beside it is there so a consumer never has to
    // regex that string back apart.
    internal sealed class FailureReport
    {
        public FailureReport(
            string message,
            string detail,
            string kind,
            string construct,
            string assert,
            string expected,
            string actual,
            string assertMessage,
            string pou,
            string method,
            int? bodyLine,
            int? line,
            IReadOnlyList<CallStackFrameReport> callStack)
        {
            CallStack = callStack;
            Message = message;
            Detail = detail;
            Kind = kind;
            Construct = construct;
            Assert = assert;
            Expected = expected;
            Actual = actual;
            AssertMessage = assertMessage;
            Pou = pou;
            Method = method;
            BodyLine = bodyLine;
            Line = line;
        }

        // For an assert failure, the formatted TcUnit line verbatim; for a
        // fault charged to this test, the located message plus that kind's
        // guidance.
        public string Message { get; }

        // Message without the guidance: what text output prints, and for an
        // assert failure the same string as Message, which carries none.
        [JsonIgnore]
        public string Detail { get; }

        // One of the FailureKind constants - the same vocabulary, under the
        // same key, that the top-level error and suites[].kind use.
        public string Kind { get; }

        // The unimplemented ST construct behind an unsupported-construct
        // failure; null otherwise.
        public string Construct { get; }

        // The TcUnit assert that failed, e.g. "AssertEquals_INT" - null
        // for a failure that wasn't raised by an assert at all.
        public string Assert { get; }

        // The compared values, already formatted by the assert's own type
        // rules (so REAL shows its "+/- delta" tolerance, an array shows
        // "ARRAY[i] = v"). Null for a non-assertion failure.
        public string Expected { get; }
        public string Actual { get; }

        // The Message:= argument the suite author wrote, on its own -
        // Message above embeds it as ", MSG: ..." along with everything
        // else. Null or empty when the assert was called without one.
        public string AssertMessage { get; }

        // Where the assert is written: POU type, method (null for a suite
        // body), and the XAE-implementation-editor-relative line - the same
        // location model suites[].callStack uses. With three asserts in one
        // method this is what says which of them failed.
        public string Pou { get; }
        public string Method { get; }
        public int? BodyLine { get; }

        // The raw .TcPOU XML line, for a consumer opening the file
        // directly rather than through XAE - same pairing as
        // suites[].fileLine vs. the body-relative line in its error text.
        public int? Line { get; }

        // For a fault charged to this test, the same call chain (and the
        // same contract) suites[].callStack carries for a suite-level
        // error. Null for an assertion failure, whose Pou/Method/BodyLine
        // above already say where it is written.
        public IReadOnlyList<CallStackFrameReport> CallStack { get; }
    }
}
