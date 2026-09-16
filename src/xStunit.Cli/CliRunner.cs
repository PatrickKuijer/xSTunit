using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using xStunit.Interpreter;
using xStunit.Interpreter.Logging;
using xStunit.Parser;

namespace xStunit.Cli
{
    /// <summary>
    /// Testable core of the `xstunit` command line; <see cref="Program"/> is a
    /// thin wrapper so a run can be driven from a test without a subprocess.
    /// </summary>
    /// <remarks>
    /// The exit code is a contract: 0 all tests passed, 1 at least one test
    /// failed, 2 usage or discovery error (bad path, bad flag value, no suites
    /// found). A file that cannot be loaded is skipped and reported, never
    /// fatal, so a run that skipped files still exits 0 or 1 by test outcome -
    /// exit 2 means the run produced no results at all.
    /// </remarks>
    public static class CliRunner
    {
        public static int Run(string[] args, TextWriter output)
        {
            // A whole-array pre-scan rather than a branch in the parse loop
            // below: --plugins, --format, --target and --suite each consume the
            // next token unconditionally as their value, so "xstunit --plugins
            // --help" would swallow --help as a directory name. Exit 0 - an
            // explicitly requested action that succeeded, not a usage error.
            if (args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase)))
            {
                output.WriteLine(HelpText);
                return 0;
            }

            var format = "text";
            // The machine the code under test is built for, which decides how
            // wide an address is wherever SIZEOF or a byte image sees one.
            // Named rather than sniffed from the host running xstunit: the two
            // are unrelated, and a run's answers must not change with the
            // developer's laptop.
            var targetName = TargetPlatform.Default.Name;
            var paths = new List<string>();
            var suiteFilters = new List<string>();
            string pluginDirectory = null;
            // A work list, never a gate: coverage never changes the exit code.
            var withCoverage = false;
            var streaming = false;
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (string.Equals(arg, "--coverage", StringComparison.OrdinalIgnoreCase))
                {
                    withCoverage = true;
                }
                else if (string.Equals(arg, "--stream", StringComparison.OrdinalIgnoreCase))
                {
                    streaming = true;
                }
                else if (arg.StartsWith("--plugins=", StringComparison.OrdinalIgnoreCase))
                {
                    pluginDirectory = arg.Substring("--plugins=".Length);
                }
                else if (string.Equals(arg, "--plugins", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length)
                    {
                        output.WriteLine("error: --plugins requires a value (directory of plugin assemblies)");
                        return 2;
                    }
                    pluginDirectory = args[++i];
                }
                else if (arg.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                {
                    format = arg.Substring("--format=".Length);
                }
                else if (string.Equals(arg, "--format", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length)
                    {
                        output.WriteLine("error: --format requires a value (text|json)");
                        return 2;
                    }
                    format = args[++i];
                }
                else if (arg.StartsWith("--target=", StringComparison.OrdinalIgnoreCase))
                {
                    targetName = arg.Substring("--target=".Length);
                }
                else if (string.Equals(arg, "--target", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length)
                    {
                        output.WriteLine("error: --target requires a value (x86|x64)");
                        return 2;
                    }
                    targetName = args[++i];
                }
                else if (arg.StartsWith("--suite=", StringComparison.OrdinalIgnoreCase))
                {
                    suiteFilters.Add(arg.Substring("--suite=".Length));
                }
                else if (string.Equals(arg, "--suite", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length)
                    {
                        output.WriteLine("error: --suite requires a value (suite type name)");
                        return 2;
                    }
                    suiteFilters.Add(args[++i]);
                }
                else
                {
                    paths.Add(arg);
                }
            }

            if (!string.Equals(format, "text", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
            {
                output.WriteLine($"error: unknown --format value '{format}' (expected text|json)");
                return 2;
            }

            if (!TargetPlatform.TryParse(targetName, out var target))
            {
                output.WriteLine($"error: unknown --target value '{targetName}' (expected x86|x64)");
                return 2;
            }

            var asJson = string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);
            args = paths.ToArray();

            if (args.Length == 0)
            {
                // Flag names only: the descriptions live in --help rather than
                // being duplicated on this error path.
                output.WriteLine("usage: xstunit <path-to-POUs-directory> [<path-to-POUs-directory> ...] [--format text|json] [--suite <name>] [--plugins <dir>] [--target x86|x64] [--coverage] [--stream]");
                output.WriteLine("Run 'xstunit --help' for flag descriptions and examples.");
                return 2;
            }

            var skipped = new List<SkippedFile>();

            // Declared this early only so an error can report it too: a tree
            // with no suites is precisely the tree where every POU is
            // uncovered. Stays null unless --coverage was passed.
            IReadOnlyList<PouCoverage> coverage = null;

            // The last point at which the output format matters to this file:
            // from here on the run reports what happened and the writer alone
            // decides what reaches stdout.
            var writer = RunReportWriter.Create(output, asJson, streaming);

            // Skips are taken over before the error check, not after: a load
            // that stopped on a usage error still reports the files it had
            // already dropped on its way there.
            var workspace = WorkspaceLoader.Load(args);
            skipped.AddRange(workspace.Skipped);
            if (workspace.Error != null)
                return writer.Error(workspace.Error, skipped, coverage);

            var types = workspace.PouTypes;
            var registry = workspace.Registry;
            var suiteNames = SuiteDiscovery.FindSuiteTypeNames(registry, types.Select(t => t.Name));
            var suiteFilePaths = workspace.FilePathsByTypeName;

            if (suiteFilters.Count > 0)
            {
                var discovered = new HashSet<string>(suiteNames, StringComparer.Ordinal);
                var missing = suiteFilters.Where(name => !discovered.Contains(name)).Distinct().ToList();
                if (missing.Count > 0)
                    return writer.Error($"suite not found: {string.Join(", ", missing)}", skipped, coverage);

                var requested = new HashSet<string>(suiteFilters, StringComparer.Ordinal);
                suiteNames = suiteNames.Where(name => requested.Contains(name)).ToList();
            }

            // Computed BEFORE the no-suites bail-out below: a tree with no
            // suites is the case where every POU is uncovered - the longest and
            // most useful work list there is. Uses the possibly --suite-filtered
            // list, so coverage describes what this run actually covered.
            if (withCoverage)
                coverage = SuiteCoverage.Analyze(types, suiteNames);

            if (suiteNames.Count == 0)
                return writer.Error($"no TcUnit suites found under {string.Join(", ", args)}", skipped, coverage);

            // Plugin-supplied native functions are resolved only after every
            // real POU in the tree has failed to resolve a call (see
            // Engine.CallMethod), so a plugin can never shadow real source.
            var plugins = Plugins.NativeFunctionPluginLoader.Load(
                pluginDirectory, out var pluginSkips, out var pluginsLoaded);
            skipped.AddRange(pluginSkips);
            if (pluginDirectory != null)
                writer.PluginsLoaded(pluginsLoaded);

            var engine = new Engine(registry, plugins.Functions, plugins.FunctionBlocks, target);

            writer.Discovery(suiteNames, suiteFilePaths);

            foreach (var suiteName in suiteNames)
            {
                writer.SuiteStart(suiteName);

                IReadOnlyList<xStunit.Runner.TcUnitStub.TestCaseResult> results;
                long suiteDurationMs;
                // Assigned before the call, not just by it: RunSuite writes the
                // tests that finished before a fault on its way out, and the
                // catch below can only read that if the variable was already
                // definitely assigned.
                IReadOnlyList<xStunit.Runner.TcUnitStub.TestCaseResult> completedTests =
                    Array.Empty<xStunit.Runner.TcUnitStub.TestCaseResult>();
                try
                {
                    results = engine.RunSuite(suiteName, out suiteDurationMs, out completedTests);
                }
                catch (Exception ex)
                {
                    // Catches broadly ON PURPOSE: this is an isolation
                    // boundary, so one suite that throws cannot abort the
                    // suites after it. Narrowing to an enumerated list of
                    // "expected" types is a regression - anything off the list
                    // escapes and takes the rest of the run with it.
                    //
                    // Only ex.Message reaches the FAIL line and
                    // SuiteReport.Error; the full ex.ToString() (stack trace,
                    // inner exceptions) goes to the log, so diagnosing a
                    // failure needs no re-instrumenting.
                    XstunitLog.LogException($"CliRunner.Run: suite '{suiteName}' failed to run", ex);
                    var completedReports = writer.ReportTests(completedTests);
                    suiteFilePaths.TryGetValue(suiteName, out var failFilePath);
                    writer.SuiteFailed(suiteName, failFilePath, ex, completedReports);
                    continue;
                }

                var testReports = writer.ReportTests(results);

                suiteFilePaths.TryGetValue(suiteName, out var filePath);
                writer.SuiteCompleted(suiteName, filePath, testReports, suiteDurationMs);
            }

            return writer.Summary(skipped, coverage);
        }

        // The single description of every flag, and so the one that must be
        // kept in step with the parsing in Run. The no-args usage line stays a
        // pointer here rather than a second copy.
        private static readonly string HelpText =
@"xstunit - xUnit-style test runner for TwinCAT/IEC 61131-3 PLC code (no TwinCAT runtime required)

Usage:
  xstunit <path-to-POUs-directory> [<path-to-POUs-directory> ...] [options]

Arguments:
  <path-to-POUs-directory>  One or more directories, scanned recursively for
                            *.TcPOU files. Repeatable; the POU sets are
                            unioned, and a type name that collides across
                            paths is a usage error.

Options:
  --format text|json    Output format. text (default) is for a human reading
                        the console; json emits one structured result blob
                        (suites, tests, failures, skips, coverage, exit
                        code) for a script or agent to parse.
  --suite <name>        Restrict the run to one suite (repeatable). Only
                        the named suite type(s) run.
  --plugins <dir>       Directory of assemblies implementing
                        IXstunitNativeFunction, for compiled-only TwinCAT
                        library functions with no .TcPOU source (e.g.
                        Tc2_Utilities.F_CheckSum16).
  --target x86|x64      Machine the code under test is compiled for, which
                        is what makes a POINTER TO / REFERENCE TO 4 bytes or
                        8 wherever SIZEOF or a byte image sees one. x64 is
                        the default; nothing else in the layout rules differs
                        between the two.
  --coverage            Additionally report which non-suite POUs are
                        exercised by a suite, and which have none. A work
                        list, not a gate - never affects the exit code.
  --stream              Emit NDJSON progress events (discovery,
                        suite-start, suite-result, summary), one per line,
                        instead of one blob at the end.
  --help, -h            Show this help and exit.

Exit codes:
  0   all tests passed
  1   at least one test failed
  2   usage or discovery error (bad path, no suites found, ...)

Examples:
  xstunit ./Plc/POUs
      Run every suite found under ./Plc/POUs, plain text output.

  xstunit ./src ./tests --format json
      Union two directories and print one JSON result - for a script or an
      agent to parse instead of scraping console text.

  xstunit ./Plc/POUs --suite FB_CounterTests --suite FB_ClampedCounterTests
      Run only the named suites (e.g. re-running just the ones that failed).

  xstunit ./Plc/POUs --plugins ./plugins/bin/Release/netstandard2.0
      Resolve compiled-only library calls via native-function plugins.

  xstunit ./Plc/POUs --target x64
      Size addresses as the 8 bytes a 64-bit runtime uses, for suites whose
      results depend on a pointer's width.

  xstunit ./Plc/POUs --coverage
      List every non-suite POU with the suites exercising it; ""(none)""
      marks a POU with no test coverage yet.

  xstunit ./Plc/POUs --stream
      Emit one NDJSON event per line as suites run, for a live progress UI.";
    }
}
