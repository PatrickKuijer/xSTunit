using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
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

            int WriteError(string message)
            {
                if (streaming)
                {
                    // Stands alone: this can fire before any discovery or suite
                    // event has been emitted (a bad path, "no suites found").
                    output.WriteLine(JsonSerializer.Serialize(
                        new ErrorReport(message, ToSkipReports(skipped), ToCoverageReports(coverage), "error"), StreamJsonOptions));
                }
                else if (asJson)
                {
                    output.WriteLine(JsonSerializer.Serialize(
                        new ErrorReport(message, ToSkipReports(skipped), ToCoverageReports(coverage)), JsonOptions));
                }
                else
                {
                    WriteSkipLines(output, skipped);
                    output.WriteLine($"error: {message}");
                    WriteCoverageLines(output, coverage);
                }
                return 2;
            }

            foreach (var path in args)
            {
                if (!Directory.Exists(path))
                    return WriteError($"path does not exist: {path}");
            }

            // Parsed file by file rather than through
            // MultiDirectoryPouLoader.Load, which propagates the first
            // TcPouRejectedException and takes the whole run down with it: a
            // real tree always contains POUs outside the parse subset
            // (Tc2_System, __NEW, ...), and one of them must not make every
            // other suite in the tree unrunnable. A suite that genuinely
            // depends on a skipped POU still fails clearly at run time with an
            // unresolved-type error.
            var loaded = new List<LoadedPou>();
            foreach (var file in MultiDirectoryPouLoader.FindPouFiles(args))
            {
                try
                {
                    if (StructuralParseGuard.TryParseOrSkip(
                            file, () => TcPouParser.Parse(File.ReadAllText(file)), out var pou, out var skip))
                        loaded.Add(new LoadedPou(pou, file));
                    else
                        skipped.Add(skip);
                }
                catch (TcPouRejectedException ex)
                {
                    skipped.Add(new SkippedFile(file, ex.Message));
                }
            }

            // A duplicate type name across the merged directory set is a hard
            // error, not a skip: it means the caller pointed the CLI at an
            // inconsistent set of directories, which is usage, not an
            // unsupported file. Every duplicate-name check below follows suit.
            try
            {
                MultiDirectoryPouLoader.CheckForDuplicates(loaded);
            }
            catch (DuplicatePouTypeException ex)
            {
                return WriteError(ex.Message);
            }

            var types = loaded.Select(l => l.Pou).ToList();

            IReadOnlyList<StructAst> structTypes;
            try
            {
                structTypes = DutStructLoader.Load(args, out var dutSkipped);
                skipped.AddRange(dutSkipped);
            }
            catch (DuplicateStructTypeException ex)
            {
                return WriteError(ex.Message);
            }

            var aliases = DutAliasLoader.Load(args, out var aliasSkipped).ToDictionary(kv => kv.Key, kv => kv.Value);
            skipped.AddRange(aliasSkipped);

            // Enum names are merged into the alias map so SIZEOF() and every
            // other ResolveAlias call site resolves an enum to its underlying
            // integer type without a second lookup path.
            var enumAliases = DutEnumLoader.Load(args, out var enumSkipped, out var enumMembers);
            skipped.AddRange(enumSkipped);
            foreach (var enumAlias in enumAliases)
                aliases[enumAlias.Key] = enumAlias.Value;

            IReadOnlyList<GvlAst> gvls;
            try
            {
                gvls = GvlLoader.Load(args, out var gvlSkipped);
                skipped.AddRange(gvlSkipped);
            }
            catch (DuplicateGvlNameException ex)
            {
                return WriteError(ex.Message);
            }

            var registry = new TypeRegistry(types, structTypes, gvls, aliases, enumMembers);
            var suiteNames = SuiteDiscovery.FindSuiteTypeNames(registry, types.Select(t => t.Name));
            var suiteFilePaths = loaded.ToDictionary(l => l.Pou.Name, l => l.FilePath);

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

            if (streaming)
            {
                var discoverySuites = suiteNames.Select(name =>
                {
                    suiteFilePaths.TryGetValue(name, out var discoveryFilePath);
                    return new SuiteDiscoveryEntry(name, discoveryFilePath);
                }).ToList();
                output.WriteLine(JsonSerializer.Serialize(new DiscoveryEvent(discoverySuites), StreamJsonOptions));
            }

            foreach (var suiteName in suiteNames)
            {
                if (streaming)
                    output.WriteLine(JsonSerializer.Serialize(new SuiteStartEvent(suiteName), StreamJsonOptions));

                IReadOnlyList<xStunit.Runner.TcUnitStub.TestCaseResult> results;
                long suiteDurationMs;
                try
                {
                    results = engine.RunSuite(suiteName, out suiteDurationMs);
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
                    var fileLine = located != null ? NullableLine(located.Line) : null;
                    var callStack = located?.CallStack.Select(f => ToCallStackFrameReport(f.Site)).ToArray();
                    // Classified once, here, from the exception itself: the
                    // message is prose for a human and is never what a consumer
                    // switches on.
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
                    suiteReports.Add(new SuiteReport(
                        suiteName, failFilePath, errorText, errorKind, errorConstruct,
                        Array.Empty<TestReport>(), null, fileLine, callStack));
                    failCount++;
                    anyFailed = true;
                    if (streaming)
                    {
                        // A suite that never ran to completion still emits
                        // exactly one suite-result line, so a --stream
                        // consumer's "waiting" list always empties out.
                        output.WriteLine(JsonSerializer.Serialize(
                            new SuiteReport(
                                suiteName, failFilePath, errorText, errorKind, errorConstruct,
                                Array.Empty<TestReport>(), null, fileLine, callStack, "suite-result", "fail"),
                            StreamJsonOptions));
                    }
                    continue;
                }

                var testReports = new List<TestReport>();
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
                    testReports.Add(new TestReport(
                        result.Name, result.Passed, result.Failures.Select(ToFailureReport).ToArray(), result.ElapsedMilliseconds));
                    if (result.Passed)
                        passCount++;
                    else
                    {
                        failCount++;
                        anyFailed = true;
                    }
                }

                suiteFilePaths.TryGetValue(suiteName, out var filePath);
                suiteReports.Add(new SuiteReport(suiteName, filePath, null, null, null, testReports, suiteDurationMs, null, null));
                if (streaming)
                {
                    var suiteOutcome = testReports.Any(t => !t.Passed) ? "fail" : "pass";
                    output.WriteLine(JsonSerializer.Serialize(
                        new SuiteReport(suiteName, filePath, null, null, null, testReports, suiteDurationMs, null, null, "suite-result", suiteOutcome),
                        StreamJsonOptions));
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
                output.WriteLine(JsonSerializer.Serialize(
                    new RunReport(suiteReports, passCount, failCount, exitCode, ToSkipReports(skipped), ToCoverageReports(coverage), "summary"),
                    StreamJsonOptions));
            }
            else if (asJson)
            {
                output.WriteLine(JsonSerializer.Serialize(
                    new RunReport(suiteReports, passCount, failCount, exitCode, ToSkipReports(skipped), ToCoverageReports(coverage)),
                    JsonOptions));
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

        // Reads Construct and Site.BodyLine straight through: the engine
        // populates both from the ParseException's own structured fields, so
        // nothing here recovers them by re-parsing a message.
        private static FailureReport ToFailureReport(xStunit.Runner.TcUnitStub.AssertionFailure failure) =>
            new FailureReport(
                WithGuidance(failure),
                failure.Kind,
                failure.Construct,
                failure.Assert,
                failure.Expected,
                failure.Actual,
                failure.AssertMessage,
                failure.Site.PouTypeName,
                failure.Site.MethodName,
                NullableLine(failure.Site.BodyLine),
                NullableLine(failure.Site.Line),
                failure.CallStack?.Select(ToCallStackFrameReport).ToArray());

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

        private static CallStackFrameReport ToCallStackFrameReport(xStunit.Runner.TcUnitStub.AssertSite site) =>
            new CallStackFrameReport(site.PouTypeName, site.MethodName, NullableLine(site.Line), NullableLine(site.BodyLine));

        // The one place the UnknownLine sentinel becomes a JSON null, so every
        // line field on the wire uses null - never 0 - for "not known".
        private static int? NullableLine(int line) =>
            line != PlcSourceLocationException.UnknownLine ? (int?)line : null;

        private static void WriteCoverageLines(TextWriter output, IReadOnlyList<PouCoverage> coverage)
        {
            if (coverage == null)
                return;

            foreach (var entry in coverage)
                output.WriteLine($"{entry.PouTypeName}  suites: {(entry.IsCovered ? string.Join(", ", entry.SuiteTypeNames) : "(none)")}");
        }

        private static IReadOnlyList<CoverageReport> ToCoverageReports(IReadOnlyList<PouCoverage> coverage) =>
            coverage?.Select(c => new CoverageReport(c.PouTypeName, c.SuiteTypeNames)).ToList();

        private static void WriteSkipLines(TextWriter output, IReadOnlyList<SkippedFile> skipped)
        {
            foreach (var skip in skipped)
                output.WriteLine($"skipped: {skip.FileKey} ({skip.Message})");
        }

        private static IReadOnlyList<SkipReport> ToSkipReports(IReadOnlyList<SkippedFile> skipped) =>
            skipped.Select(s => new SkipReport(s.FileKey, s.Message)).ToList();

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

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // --stream's wire format. Compact, unlike JsonOptions above: an
        // indented object spans lines and would break NDJSON's
        // one-line-per-event contract.
        private static readonly JsonSerializerOptions StreamJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private sealed class ErrorReport
        {
            public ErrorReport(
                string error, IReadOnlyList<SkipReport> skipped, IReadOnlyList<CoverageReport> coverage, string streamEvent = null)
            {
                Event = streamEvent;
                // Guidance is appended here, once, rather than at each of
                // WriteError's call sites. Text output keeps the bare
                // "error: <message>" line: that one is read by a human at a
                // console, this one by a consumer with nothing else to go on.
                Error = WithGuidance(error, FailureKind.LoadError, isVerbatim: false);
                Skipped = skipped;
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

            // Reported even on a failed run: "no suites found" is a usage error
            // for a run, but for a work list it is the most informative answer
            // there is - every POU in the tree is uncovered.
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public IReadOnlyList<CoverageReport> Coverage { get; }
        }

        // FilePath is the full on-disk path, so the offending file stays
        // unambiguous when several merged directories hold same-named POUs.
        private sealed class SkipReport
        {
            public SkipReport(string filePath, string reason)
            {
                FilePath = filePath;
                Reason = reason;
            }

            public string FilePath { get; }
            public string Reason { get; }
        }

        // Root of the `--format json` output, and with it the *Report family
        // below. Kept separate from TestCaseResult/AssertionFailure on purpose:
        // the wire format has to stay stable even when the interpreter's
        // internal model changes.
        private sealed class RunReport
        {
            public RunReport(
                IReadOnlyList<SuiteReport> suites,
                int passed,
                int failed,
                int exitCode,
                IReadOnlyList<SkipReport> skipped,
                IReadOnlyList<CoverageReport> coverage,
                string streamEvent = null)
            {
                Event = streamEvent;
                Suites = suites;
                Passed = passed;
                Failed = failed;
                ExitCode = exitCode;
                Skipped = skipped;
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

            // One entry per non-suite POU. Omitted entirely without --coverage,
            // because an empty list already means something else: that every
            // POU in the tree is uncovered.
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public IReadOnlyList<CoverageReport> Coverage { get; }
        }

        // An entry whose `suites` is empty is the interesting one: it is a
        // directly usable next task ("write a suite for F_ComputeChecksum").
        private sealed class CoverageReport
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
        private sealed class SuiteDiscoveryEntry
        {
            public SuiteDiscoveryEntry(string name, string filePath)
            {
                Name = name;
                FilePath = filePath;
            }

            public string Name { get; }
            public string FilePath { get; }
        }

        private sealed class DiscoveryEvent
        {
            public DiscoveryEvent(IReadOnlyList<SuiteDiscoveryEntry> suites)
            {
                Suites = suites;
            }

            public string Event => "discovery";
            public IReadOnlyList<SuiteDiscoveryEntry> Suites { get; }
        }

        // Emitted immediately before a suite runs, and paired with the
        // suite-result line (a SuiteReport with Event="suite-result") emitted
        // once it finishes.
        private sealed class SuiteStartEvent
        {
            public SuiteStartEvent(string suite)
            {
                Suite = suite;
            }

            public string Event => "suite-start";
            public string Suite { get; }
        }

        private sealed class SuiteReport
        {
            public SuiteReport(
                string name,
                string filePath,
                string error,
                string kind,
                string construct,
                IReadOnlyList<TestReport> tests,
                long? durationMs,
                int? fileLine,
                IReadOnlyList<CallStackFrameReport> callStack,
                string streamEvent = null,
                string outcome = null)
            {
                Event = streamEvent;
                Outcome = outcome;
                Name = name;
                FilePath = filePath;
                Error = error;
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

            // "pass"/"fail" on that same standalone line, null everywhere else.
            // A suite that never ran to completion reports "fail" rather than a
            // third "skip" state: it already counts toward the exit code like
            // any failing TEST(), and `kind` is what says why.
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string Outcome { get; }

            public string Name { get; }
            public string FilePath { get; }
            public string Error { get; }

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

        private sealed class CallStackFrameReport
        {
            public CallStackFrameReport(string pouTypeName, string methodName, int? line, int? bodyLine)
            {
                PouTypeName = pouTypeName;
                MethodName = methodName;
                Line = line;
                BodyLine = bodyLine;
            }

            public string PouTypeName { get; }

            // Null for a frame with no method to name: a suite body, a
            // bare-invoked FB body, or a StepCycles cycle.
            public string MethodName { get; }

            // The raw .TcPOU XML line, null when unknown.
            public int? Line { get; }

            // The XAE-implementation-editor-relative line, null when unknown.
            public int? BodyLine { get; }
        }

        private sealed class TestReport
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
        private sealed class FailureReport
        {
            public FailureReport(
                string message,
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

            // For an assert failure, verbatim what text output prints; for a
            // fault charged to this test, the located message plus that kind's
            // guidance (see WithGuidance).
            public string Message { get; }

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
}
