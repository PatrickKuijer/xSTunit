using System.Collections.Generic;

namespace xStunit.Vsix.TestRunner
{
    /// <summary>
    /// Mirrors the camelCase JSON shape emitted by
    /// `tcxunit run --format json` (see xStunit.Cli.CliRunner):
    /// { suites: [{ name, filePath, error, kind, construct, durationMs, callStack, tests: [{ name, passed, failures, durationMs }] }], passed, failed, exitCode }
    /// and its early-exit error shape: { error, kind }.
    /// </summary>
    /// <remarks>
    /// This extension has NO ProjectReference to xStunit.Cli - it targets
    /// net472, shells out to the tcxunit executable and deserializes its stdout
    /// - so a rename on the CLI's side cannot produce a compile error here. It
    /// produces a silently-null property instead. Any change to the CLI's wire
    /// shape has to be mirrored into this file by hand, in the same change;
    /// TcxunitModelsDeserializationTests is where that gets pinned.
    /// </remarks>
    internal sealed class TcxunitRunResult
    {
        public List<TcxunitSuiteResult> Suites { get; set; }

        public int Passed { get; set; }

        public int Failed { get; set; }

        public int ExitCode { get; set; }

        public string Error { get; set; }

        // TcXunit-3tx.1/229.15: the kind of the run-level Error above. Always
        // "load-error" - every error reported through this shape is a
        // usage/discovery failure that produced no results at all - and
        // modelled only so the same two keys (kind/construct) can be read at
        // every level of the JSON without a special case for the top one.
        public string Kind { get; set; }

        // Not part of the CLI's wire shape -- TcxunitProcessRunner stashes the
        // exact stdout text here after deserializing it, so
        // ResultsToolWindowControl can forward that same camelCase JSON straight
        // into the WebView2 page (see BuildRenderResultScript) instead of
        // re-serializing this object and risking a shape drift between what the
        // CLI actually emitted and what the tree renders from.
        public string RawJson { get; set; }
    }

    internal sealed class TcxunitSuiteResult
    {
        public string Name { get; set; }

        // Added for TcXunit-1tt.2: the results tree's .node-src slot (suite rows
        // only) renders this, per docs/design-system.html section 5's node-anatomy
        // table. The CLI has emitted "filePath" on SuiteReport since TcXunit-8gj;
        // this model simply hadn't picked the field up yet.
        public string FilePath { get; set; }

        public string Error { get; set; }

        // TcXunit-229.15: the machine-readable counterpart to Error - one of
        // the FailureKind strings ("assertion", "plc-fault",
        // "unsupported-construct", "load-error", "parse-error"), null for a
        // suite that didn't fail.
        //
        // The CLI emitted this as "errorKind" from TcXunit-3tx.1 until
        // TcXunit-229.15 renamed it to "kind", so that the top-level error,
        // this suite-level error and every per-test failure all use one pair of
        // keys. This model never picked the old name up, so nothing here read
        // it under the old spelling and nothing broke on the rename - it is
        // modelled now so the next consumer that wants to render "the
        // interpreter is behind" differently from "the PLC code is wrong"
        // doesn't have to rediscover that the field exists.
        public string Kind { get; set; }

        // The construct behind Kind: the unimplemented ST construct for an
        // unsupported-construct (e.g. "SEL"), or the offending token for a
        // parse-error. Null for every other kind, and whenever the throw site
        // knew only that something was unsupported. Renamed from
        // "errorConstruct" alongside Kind above (TcXunit-229.15).
        public string Construct { get; set; }

        public List<TcxunitTestResult> Tests { get; set; }

        // Added for TcXunit-1tt.6: the results tree's .node-dur slot (suite
        // rows) renders this. The CLI has emitted "durationMs" on SuiteReport
        // since TcXunit-6fb.2 as a nullable long -- null when the suite failed
        // to load (never ran, so there's nothing to time), matching this
        // model's nullable long?.
        public long? DurationMs { get; set; }

        // Added for TcXunit-9fs: the CLI has emitted "callStack" on SuiteReport
        // since TcXunit-7s6 -- the full interpreted call chain behind Error,
        // innermost frame first. Null (not an empty list) for a passing suite
        // or a load-level failure that never entered an interpreted ST body,
        // matching the CLI's own null-vs-empty-array convention.
        public List<TcxunitCallStackFrame> CallStack { get; set; }
    }

    // One frame of TcxunitSuiteResult.CallStack (TcXunit-9fs), mirroring the
    // CLI's CallStackFrameReport shape.
    internal sealed class TcxunitCallStackFrame
    {
        public string PouTypeName { get; set; }

        // Null for a frame with no method to name (a suite body, a bare-
        // invoked FB body, or a StepCycles cycle).
        public string MethodName { get; set; }

        // The raw .TcPOU XML line, or null when unknown.
        public int? Line { get; set; }

        // The XAE-implementation-editor-relative line, or null when unknown.
        public int? BodyLine { get; set; }
    }

    internal sealed class TcxunitTestResult
    {
        public string Name { get; set; }

        public bool Passed { get; set; }

        // TcXunit-3tx.2: each failure became an object (message + kind +
        // expected/actual + assert name + location) instead of a bare string.
        // `message` carries the identical formatted line the string used to be,
        // so nothing this extension already renders changed meaning.
        public List<TcxunitFailure> Failures { get; set; }

        // Added for TcXunit-1tt.6: the results tree's .node-dur slot (leaf
        // test rows). The CLI has emitted "durationMs" on TestReport since
        // TcXunit-6fb.1 as a non-negative long -- every test that appears in
        // the JSON ran (a suite that failed to load has no test entries at
        // all), so this is a plain long, not nullable.
        public long DurationMs { get; set; }
    }

    /// <summary>
    /// One per-test failure (TcXunit-3tx.2). Only the field this extension
    /// actually renders is modelled -- the CLI also emits kind, construct,
    /// assert, expected, actual, assertMessage, pou, method, bodyLine, line and
    /// callStack, which exist for non-interactive consumers and are ignored
    /// here rather than carried as fields nothing reads.
    /// </summary>
    internal sealed class TcxunitFailure
    {
        /// <summary>
        /// The formatted "FAILED TEST '<c>name</c>', EXP: ..., ACT: ..." line --
        /// byte for byte what failures[] used to hold as a bare string.
        /// </summary>
        public string Message { get; set; }
    }
}
