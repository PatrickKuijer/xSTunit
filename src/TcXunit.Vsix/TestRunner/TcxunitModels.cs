using System.Collections.Generic;

namespace TcXunit.Vsix.TestRunner
{
    /// <summary>
    /// Mirrors the camelCase JSON shape emitted by
    /// `tcxunit run --format json` (see TcXunit.Cli.CliRunner):
    /// { suites: [{ name, filePath, error, tests: [{ name, passed, failures }] }], passed, failed, exitCode }
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
    }

    internal sealed class TcxunitTestResult
    {
        public string Name { get; set; }

        public bool Passed { get; set; }

        public List<string> Failures { get; set; }
    }
}
