using System.Collections.Generic;

namespace TcXunit.Vsix.TestRunner
{
    /// <summary>
    /// Mirrors the camelCase JSON shape emitted by
    /// `tcxunit run --format json` (see TcXunit.Cli.CliRunner):
    /// { suites: [{ name, filePath, error, durationMs, tests: [{ name, passed, failures, durationMs }] }], passed, failed, exitCode }
    /// and its early-exit error shape: { error }.
    /// </summary>
    internal sealed class TcxunitRunResult
    {
        public List<TcxunitSuiteResult> Suites { get; set; }

        public int Passed { get; set; }

        public int Failed { get; set; }

        public int ExitCode { get; set; }

        public string Error { get; set; }

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

        public List<TcxunitTestResult> Tests { get; set; }

        // Added for TcXunit-1tt.6: the results tree's .node-dur slot (suite
        // rows) renders this. The CLI has emitted "durationMs" on SuiteReport
        // since TcXunit-6fb.2 as a nullable long -- null when the suite failed
        // to load (never ran, so there's nothing to time), matching this
        // model's nullable long?.
        public long? DurationMs { get; set; }
    }

    internal sealed class TcxunitTestResult
    {
        public string Name { get; set; }

        public bool Passed { get; set; }

        public List<string> Failures { get; set; }

        // Added for TcXunit-1tt.6: the results tree's .node-dur slot (leaf
        // test rows). The CLI has emitted "durationMs" on TestReport since
        // TcXunit-6fb.1 as a non-negative long -- every test that appears in
        // the JSON ran (a suite that failed to load has no test entries at
        // all), so this is a plain long, not nullable.
        public long DurationMs { get; set; }
    }
}
