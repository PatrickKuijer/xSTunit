using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using xStunit.Interpreter;
using xStunit.Interpreter.Logging;
using xStunit.Parser;
using xStunit.Runner;

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
            // below: --plugins, --format and --suite each consume the next
            // token unconditionally as their value, so "xstunit --plugins
            // --help" would swallow --help as a directory name. Exit 0 - an
            // explicitly requested action that succeeded, not a usage error.
            if (args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase)))
            {
                output.WriteLine(HelpText);
                return 0;
            }

            var format = "text";
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

            var asJson = string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);
            args = paths.ToArray();

            if (args.Length == 0)
            {
                // Flag names only: the descriptions live in --help rather than
                // being duplicated on this error path.
                output.WriteLine("usage: xstunit <path-to-POUs-directory> [<path-to-POUs-directory> ...] [--format text|json] [--suite <name>] [--plugins <dir>] [--coverage] [--stream]");
                output.WriteLine("Run 'xstunit --help' for flag descriptions and examples.");
                return 2;
            }

            var skipped = new List<SkippedFile>();

            // Declared this early only so WriteError can report it too: a tree
            // with no suites is precisely the tree where every POU is
            // uncovered. Stays null unless --coverage was passed.
            IReadOnlyList<PouCoverage> coverage = null;

            // Both WithGuidance overloads are handed over: the report module
            // owns the shape of what goes on the wire, this file owns the
            // wording that goes inside it.
            var reportBuilder = new RunReportBuilder(WithGuidance, WithGuidance);

            int WriteError(string message)
            {
                if (streaming)
                {
                    // Stands alone: this can fire before any discovery or suite
                    // event has been emitted (a bad path, "no suites found").
                    output.WriteLine(RunReportJson.Line(reportBuilder.Error(message, skipped, coverage, "error")));
                }
                else if (asJson)
                {
                    output.WriteLine(RunReportJson.Blob(reportBuilder.Error(message, skipped, coverage)));
                }
                else
                {
                    WriteSkipLines(output, skipped);
                    output.WriteLine($"error: {message}");
                    WriteCoverageLines(output, coverage);
                }
                return 2;
            }

            // Skips are taken over before the error check, not after: a load
            // that stopped on a usage error still reports the files it had
            // already dropped on its way there.
            var workspace = WorkspaceLoader.Load(args);
            skipped.AddRange(workspace.Skipped);
            if (workspace.Error != null)
                return WriteError(workspace.Error);

            var types = workspace.PouTypes;
            var registry = workspace.Registry;
            var suiteNames = SuiteDiscovery.FindSuiteTypeNames(registry, types.Select(t => t.Name));
            var suiteFilePaths = workspace.FilePathsByTypeName;

            if (suiteFilters.Count > 0)
            {
                var discovered = new HashSet<string>(suiteNames, StringComparer.Ordinal);
                var missing = suiteFilters.Where(name => !discovered.Contains(name)).Distinct().ToList();
                if (missing.Count > 0)
                    return WriteError($"suite not found: {string.Join(", ", missing)}");

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
                return WriteError($"no TcUnit suites found under {string.Join(", ", args)}");

            // Plugin-supplied native functions are resolved only after every
            // real POU in the tree has failed to resolve a call (see
            // Engine.CallMethod), so a plugin can never shadow real source.
            var nativeFunctions = Plugins.NativeFunctionPluginLoader.Load(
                pluginDirectory, out var pluginSkips, out var pluginsLoaded);
            skipped.AddRange(pluginSkips);
            // Under --stream, stdout must stay one JSON object per line: any
            // plain-text line interleaved with the NDJSON breaks that contract,
            // which is why every text write below is gated the same way.
            if (!asJson && !streaming && pluginDirectory != null)
            {
                foreach (var plugin in pluginsLoaded)
                    output.WriteLine($"plugin: {plugin}");
            }

            var engine = new Engine(registry, nativeFunctions);
            var anyFailed = false;
            var passCount = 0;
            var failCount = 0;
            var suiteReports = new List<SuiteReport>();

            // Shared by the two paths that have test results to report: a suite
            // that ran to completion, and one that faulted after some of its
            // tests had already finished. Counting them in one place is what
            // keeps `passed`/`failed` describing the same tests the report
            // lists.
            List<TestReport> ReportTests(IReadOnlyList<xStunit.Runner.TcUnitStub.TestCaseResult> results)
            {
                var reports = new List<TestReport>();
                foreach (var result in results)
                {
                    if (!asJson && !streaming)
                    {
                        output.WriteLine(result.ToString());

                        foreach (var failure in result.Failures)
                        {
                            if (failure.CallStack == null)
                                continue;
                            foreach (var frame in failure.CallStack)
                                output.WriteLine($"    at {frame.LocationWithLine}");
                        }
                    }
                    reports.Add(reportBuilder.Test(result));
                    if (result.Passed)
                        passCount++;
                    else
                    {
                        failCount++;
                        anyFailed = true;
                    }
                }
                return reports;
            }

            if (streaming)
                output.WriteLine(RunReportJson.Line(RunReportBuilder.Discovery(suiteNames, suiteFilePaths)));

            foreach (var suiteName in suiteNames)
            {
                if (streaming)
                    output.WriteLine(RunReportJson.Line(RunReportBuilder.SuiteStart(suiteName)));

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
                    // Non-null only when an interpreted ST body actually
                    // faulted; a load-level failure (an unresolvable type in
                    // default-value construction, say) has no PLC location, and
                    // every location-derived field below stays null for it.
                    var located = ex as PlcSourceLocationException;
                    // Reported before the FAIL line below, in the order they
                    // happened: these tests ran and finished, and the fault came
                    // after them.
                    var completedReports = ReportTests(completedTests);
                    if (!asJson && !streaming)
                    {
                        // located.Message rather than a locally composed
                        // location + inner message: the exception owns the one
                        // rendering of "where", shared with the JSON error
                        // string below, so there are never two formatters to
                        // keep in step.
                        var detail = located != null ? $"in {located.Message}" : ex.Message;
                        output.WriteLine($"{suiteName}: FAIL ({detail})");

                        // Frames print beneath the FAIL line, never instead of
                        // it: a text consumer that reads only the FAIL line
                        // must keep working.
                        if (located != null)
                        {
                            foreach (var frame in located.CallStack)
                                output.WriteLine($"    at {frame.LocationWithLine}");
                        }
                    }
                    suiteFilePaths.TryGetValue(suiteName, out var failFilePath);
                    // Classified once, here, from the exception itself: the
                    // message is prose for a human and is never what a consumer
                    // switches on.
                    //
                    // FailureKind.Assertion is a legitimate answer here, not
                    // only on the per-test path: an assertion that escapes the
                    // TEST()/TEST_FINISHED() bracket has no test to charge and
                    // lands as a suite-level error. Re-homing it as
                    // FailureKind.PlcFault would claim the PLC faulted, which is
                    // false, and would trade the assertion guidance for advice
                    // to go fix code under test that is not what broke.
                    var errorKind = FailureClassifier.Classify(ex, out var errorConstruct);
                    // A parse error's body line comes from the front end's own
                    // structured field, never from re-parsing ex.Message.
                    var errorBodyLine = errorKind == FailureKind.ParseError
                        ? FailureClassifier.UnwrapParseException(ex)?.BodyLine ?? PlcSourceLocationException.UnknownLine
                        : PlcSourceLocationException.UnknownLine;
                    // Guidance is appended so the JSON object is self-contained:
                    // a consumer never has to have read this repo to know
                    // whether to edit the POU or stop and escalate.
                    var errorText = WithGuidance(ex.Message, errorKind, isVerbatim: false, errorBodyLine);
                    suiteReports.Add(reportBuilder.SuiteError(
                        suiteName, failFilePath, errorText, errorKind, errorConstruct, completedReports, located));
                    // The suite-level fault is a failure in its own right, on
                    // top of whatever the tests above reported: a run cannot
                    // pass just because everything that got to run passed.
                    failCount++;
                    anyFailed = true;
                    if (streaming)
                    {
                        // A suite that never ran to completion still emits
                        // exactly one suite-result line, so a --stream
                        // consumer's "waiting" list always empties out.
                        output.WriteLine(RunReportJson.Line(reportBuilder.SuiteError(
                            suiteName, failFilePath, errorText, errorKind, errorConstruct, completedReports, located,
                            "suite-result", "fail")));
                    }
                    continue;
                }

                var testReports = ReportTests(results);

                suiteFilePaths.TryGetValue(suiteName, out var filePath);
                suiteReports.Add(reportBuilder.Suite(suiteName, filePath, testReports, suiteDurationMs));
                if (streaming)
                {
                    var suiteOutcome = testReports.Any(t => !t.Passed) ? "fail" : "pass";
                    output.WriteLine(RunReportJson.Line(
                        reportBuilder.Suite(suiteName, filePath, testReports, suiteDurationMs, "suite-result", suiteOutcome)));
                }
            }

            // Skipped files must never change the exit code: a run that skipped
            // unsupported POUs but ran everything else is still a completed run
            // (0 or 1 by test outcome). Exit 2 stays reserved for usage and
            // discovery errors that produced no results at all; the skip list
            // is what tells the caller coverage was reduced.
            var exitCode = anyFailed ? 1 : 0;

            if (streaming)
            {
                output.WriteLine(RunReportJson.Line(
                    reportBuilder.Summary(suiteReports, passCount, failCount, exitCode, skipped, coverage, "summary")));
            }
            else if (asJson)
            {
                output.WriteLine(RunReportJson.Blob(
                    reportBuilder.Summary(suiteReports, passCount, failCount, exitCode, skipped, coverage)));
            }
            else
            {
                WriteSkipLines(output, skipped);
                output.WriteLine(skipped.Count > 0
                    ? $"{passCount} passed, {failCount} failed, {skipped.Count} skipped"
                    : $"{passCount} passed, {failCount} failed");
                WriteCoverageLines(output, coverage);
            }

            return exitCode;
        }

        // The message a consumer reads, followed by what to DO about a failure
        // of that kind: the consumer is usually a model choosing its next edit
        // from one JSON object, and the two possible responses - fix the ST, or
        // stop and escalate - are opposites that the factual half never
        // distinguishes.
        //
        // The exception is a formatted TcUnit assert line ("FAILED TEST 'X',
        // EXP: 99, ACT: 3, MSG: ..."), reproduced byte for byte from upstream
        // TcUnit's FB_AdsAssertMessageFormatter: it has a verbatim contract, so
        // no guidance may be appended to it. `Expected != null` is what
        // identifies one, because FB_TestSuite.Fail() is both the only path
        // that formats that string and the only path that populates
        // Assert/Expected/Actual/AssertMessage. Keying on the KIND instead
        // would exempt every assertion-kind failure, including ones that never
        // went near the formatter.
        private static string WithGuidance(xStunit.Runner.TcUnitStub.AssertionFailure failure) =>
            WithGuidance(failure.Message, failure.Kind, isVerbatim: failure.Expected != null, failure.Site.BodyLine);

        // Overload for the call sites that have no failure object at all: a
        // suite-level error and the run-level ErrorReport. isVerbatim has no
        // default on purpose - the verbatim exemption must never be something a
        // caller gets by omission.
        private static string WithGuidance(
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

        private static void WriteCoverageLines(TextWriter output, IReadOnlyList<PouCoverage> coverage)
        {
            if (coverage == null)
                return;

            foreach (var entry in coverage)
                output.WriteLine($"{entry.PouTypeName}  suites: {(entry.IsCovered ? string.Join(", ", entry.SuiteTypeNames) : "(none)")}");
        }

        private static void WriteSkipLines(TextWriter output, IReadOnlyList<SkippedFile> skipped)
        {
            foreach (var skip in skipped)
                output.WriteLine($"skipped: {skip.FileKey} ({skip.Message})");
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

  xstunit ./Plc/POUs --coverage
      List every non-suite POU with the suites exercising it; ""(none)""
      marks a POU with no test coverage yet.

  xstunit ./Plc/POUs --stream
      Emit one NDJSON event per line as suites run, for a live progress UI.";
    }
}
