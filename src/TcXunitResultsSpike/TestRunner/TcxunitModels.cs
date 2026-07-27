using System.Collections.Generic;

namespace TcXunitResultsSpike.TestRunner
{
    /// <summary>
    /// THROWAWAY SPIKE. Mirrors the camelCase JSON shape emitted by
    /// `tcxunit run --format json` (see TcXunit.Cli.CliRunner):
    /// { suites: [{ name, error, tests: [{ name, passed, failures }] }], passed, failed, exitCode }
    /// and its early-exit error shape: { error }.
    /// </summary>
    internal sealed class TcxunitRunResult
    {
        public List<TcxunitSuiteResult> Suites { get; set; }

        public int Passed { get; set; }

        public int Failed { get; set; }

        public int ExitCode { get; set; }

        public string Error { get; set; }
    }

    internal sealed class TcxunitSuiteResult
    {
        public string Name { get; set; }

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
