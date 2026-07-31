using System.Collections.Generic;

namespace xStunit.Vsix.TestRunner
{
    /// <summary>
    /// Mirrors the camelCase JSON emitted by `xstunit run --format json` (see
    /// xStunit.Cli.CliRunner):
    /// { suites: [{ name, filePath, error, kind, construct, durationMs, callStack, tests: [{ name, passed, failures, durationMs }] }], passed, failed, exitCode }
    /// and its early-exit error shape: { error, kind }.
    /// </summary>
    /// <remarks>
    /// This extension has NO ProjectReference to xStunit.Cli - it targets net472,
    /// shells out to the xstunit executable and deserializes its stdout - so a rename
    /// on the CLI's side cannot produce a compile error here. It produces a silently
    /// null property instead. Any change to the CLI's wire shape has to be mirrored
    /// into this file by hand, in the same change; XstunitModelsDeserializationTests is
    /// where that gets pinned.
    /// </remarks>
    internal sealed class XstunitRunResult
    {
        public List<XstunitSuiteResult> Suites { get; set; }

        public int Passed { get; set; }

        public int Failed { get; set; }

        public int ExitCode { get; set; }

        public string Error { get; set; }

        // Classifies the run-level Error above. Always "load-error" - every error
        // reported through this shape is a usage/discovery failure that produced no
        // results at all - and modelled only so the same kind/construct pair can be read
        // at every level of the JSON without special-casing the top one.
        public string Kind { get; set; }

        // Not part of the CLI's wire shape: XstunitProcessRunner stashes the exact stdout
        // text here after deserializing it, so ResultsToolWindowControl can forward that
        // same camelCase JSON straight into the WebView2 page (see
        // BuildRenderResultScript) instead of re-serializing this object and risking a
        // drift between what the CLI emitted and what the tree renders from.
        public string RawJson { get; set; }
    }

    internal sealed class XstunitSuiteResult
    {
        public string Name { get; set; }

        public string FilePath { get; set; }

        public string Error { get; set; }

        // The machine-readable counterpart to Error: one of the CLI's FailureKind
        // strings ("assertion", "plc-fault", "unsupported-construct", "load-error",
        // "parse-error"), null for a suite that didn't fail. Modelled ahead of a
        // consumer so the next renderer that wants to show "the interpreter is behind"
        // differently from "the PLC code is wrong" doesn't have to rediscover it.
        public string Kind { get; set; }

        // The construct behind Kind: the unimplemented ST construct for an
        // unsupported-construct (e.g. "SEL"), or the offending token for a parse-error.
        // Null for every other kind, and whenever the throw site knew only that
        // something was unsupported.
        public string Construct { get; set; }

        public List<XstunitTestResult> Tests { get; set; }

        // Nullable because a suite that failed to load never ran, so there is nothing to
        // time.
        public long? DurationMs { get; set; }

        // The interpreted call chain behind Error, innermost frame first. Null rather
        // than an empty list for a passing suite, or for a load-level failure that never
        // entered an interpreted ST body - the CLI's own null-vs-empty-array convention.
        public List<XstunitCallStackFrame> CallStack { get; set; }
    }

    // One frame of XstunitSuiteResult.CallStack, mirroring the CLI's
    // CallStackFrameReport shape.
    internal sealed class XstunitCallStackFrame
    {
        public string PouTypeName { get; set; }

        // Null for a frame with no method to name (a suite body, a bare-invoked FB body,
        // or a StepCycles cycle).
        public string MethodName { get; set; }

        // The raw .TcPOU XML line, or null when unknown.
        public int? Line { get; set; }

        // The XAE-implementation-editor-relative line, or null when unknown.
        public int? BodyLine { get; set; }
    }

    internal sealed class XstunitTestResult
    {
        public string Name { get; set; }

        public bool Passed { get; set; }

        public List<XstunitFailure> Failures { get; set; }

        // Not nullable, unlike the suite-level duration: every test that appears in the
        // JSON ran, since a suite that failed to load has no test entries at all.
        public long DurationMs { get; set; }
    }

    /// <summary>
    /// One per-test failure. Deliberately partial: the CLI also emits kind, construct,
    /// assert, expected, actual, assertMessage, pou, method, bodyLine, line and
    /// callStack, which exist for non-interactive consumers and are ignored here rather
    /// than carried as fields nothing reads.
    /// </summary>
    internal sealed class XstunitFailure
    {
        /// <summary>
        /// The formatted "FAILED TEST '<c>name</c>', EXP: ..., ACT: ..." line.
        /// </summary>
        public string Message { get; set; }
    }
}
