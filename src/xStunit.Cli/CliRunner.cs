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
    // Testable core of `tcxunit run <path>` (TcXunit-w5x.7 item 5). Program.Main
    // is a thin wrapper so this can be driven from xUnit without a subprocess.
    public static class CliRunner
    {
        public static int Run(string[] args, TextWriter output)
        {
            // --help/-h (TcXunit-3cu): a standalone pre-scan rather than an
            // else-if branch in the loop below, deliberately - --plugins,
            // --format, and --suite each consume the next token unconditionally
            // as their value, so "tcxunit --plugins --help" would swallow
            // "--help" as a directory name instead of recognizing it if this
            // lived inside that loop. Scanning the whole array up front means
            // --help wins regardless of position. Exit 0: this is a successful,
            // explicitly requested action, not a usage error.
            if (args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase)))
            {
                output.WriteLine(HelpText);
                return 0;
            }

            // --format json|text (TcXunit prototype spike: structured output for
            // non-console consumers, e.g. a VSIX tool window shelling out to the
            // CLI instead of parsing plain-text lines). Accepted anywhere in args,
            // both "--format json" and "--format=json"; everything else is a path.
            var format = "text";
            var paths = new List<string>();
            // --suite <name> (TcXunit-6fb.3): repeatable, restricts the run to
            // the named suite(s) instead of everything SuiteDiscovery finds -
            // e.g. a VSIX "rerun failed" action re-running only the suites it
            // cares about. Parsed the same way as --format (both "--suite x"
            // and "--suite=x"), interleaved freely with paths/--format.
            var suiteFilters = new List<string>();
            // --plugins <dir> (TcXunit-6k2): directory of assemblies supplying
            // ITcXunitNativeFunction stand-ins for compiled-only TwinCAT
            // library functions (Tc2_Utilities' F_CheckSum16 and friends),
            // which have no .TcPOU source anywhere to parse. Parsed like
            // --format/--suite; omitted means no plugins, i.e. exactly the
            // pre-existing behavior.
            string pluginDirectory = null;
            // --coverage (TcXunit-3tx.4): additionally report which loaded POUs
            // any suite exercises, and - the useful half - which none does. A
            // next-task list for an agent rather than a CI gate, so it is a
            // report only: it never changes the exit code. Opt-in because a
            // large tree's list is long and most runs don't want it.
            var withCoverage = false;
            // --stream (TcXunit-ce1): opt-in NDJSON progress mode for large
            // suite counts - one JSON object per line instead of one blob at
            // the end: an initial "discovery" event listing every suite
            // about to run (so a consumer can render a "waiting" list up
            // front), a "suite-start"/"suite-result" pair per suite as it
            // executes, and a final "summary" event carrying the same
            // aggregate data --format json already reports in one shot.
            // Independent of --format. When NOT passed, output is exactly
            // what it always was - this is the CLI's most-consumed contract
            // (the VSIX shells out to it without ever passing --stream), so
            // every new code path below is gated behind this flag rather
            // than changed in place.
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
                // TcXunit-3cu: kept short (path args + flag names only, no
                // descriptions) - full detail lives in --help so this error
                // path doesn't duplicate it. "run" was never a real
                // subcommand (Program.Main passes args straight to Run), so
                // it's dropped here rather than carried forward as a token
                // that would itself fail with "path does not exist: run".
                output.WriteLine("usage: tcxunit <path-to-POUs-directory> [<path-to-POUs-directory> ...] [--format text|json] [--suite <name>] [--plugins <dir>] [--coverage] [--stream]");
                output.WriteLine("Run 'tcxunit --help' for flag descriptions and examples.");
                return 2;
            }

            // Files that couldn't be loaded but must not abort the run
            // (TcXunit-iyd.7) - reported individually at the end of the run
            // instead of collapsing the whole invocation into a discovery
            // error.
            var skipped = new List<SkippedFile>();

            // TcXunit-3tx.4: null until the POU set and suite list are both
            // known (see below), and stays null unless --coverage was passed.
            // Declared up here only so WriteError can report it too - a tree
            // with no suites is precisely the tree where everything is
            // uncovered.
            IReadOnlyList<PouCoverage> coverage = null;

            int WriteError(string message)
            {
                if (streaming)
                {
                    // TcXunit-ce1: a single NDJSON "error" line, same data as
                    // the --format json ErrorReport plus the event tag - this
                    // can fire before any suite/discovery event is emitted
                    // (a bad path, "no suites found"), so it stands alone
                    // rather than assuming a discovery line already went out.
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

            // Parse each *.TcPOU individually rather than via
            // MultiDirectoryPouLoader.Load, which propagates the first
            // TcPouRejectedException and takes the entire run down with it
            // (TcXunit-iyd.7): a real production tree always contains POUs
            // outside the v1 parse subset (Tc2_System, __NEW, ...), and one of
            // them must not make every *other* suite in the tree unrunnable.
            // Same skip-and-report shape used throughout this loader
            // (PLC-b62/TcXunit-swk), keyed by full file path for the same
            // collision-avoidance reason (TcXunit-pvp). A suite that actually
            // depends on a skipped POU still fails clearly at run time with
            // an unresolved-type error.
            var loaded = new List<LoadedPou>();
            foreach (var file in MultiDirectoryPouLoader.FindPouFiles(args))
            {
                try
                {
                    // Structurally unexpected POUs (malformed XML, missing
                    // Declaration/Implementation/ST, a GVL/DUT file caught by
                    // the *.TcPOU glob) are skipped the same way.
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

            // Duplicate type names across the merged directory set stay a hard,
            // fail-fast error here: it means the caller pointed the CLI at an
            // inconsistent set of directories, which is a usage/discovery
            // error rather than an unsupported-file skip.
            try
            {
                MultiDirectoryPouLoader.CheckForDuplicates(loaded);
            }
            catch (DuplicatePouTypeException ex)
            {
                return WriteError(ex.Message);
            }

            var types = loaded.Select(l => l.Pou).ToList();

            // .TcDUT STRUCT types (TcXunit-9li): loaded via DutStructLoader so
            // `tcxunit run` resolves STRUCT-typed DUTs instead of silently
            // failing to resolve any suite/FB that depends on one. Per-file
            // parse failures are isolated the same resilient way
            // DutStructLoader isolates them (unsupported DUT kinds are
            // skipped, not fatal); a duplicate STRUCT name across files is a
            // hard error, same as a duplicate POU type name above.
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

            // ALIAS .TcDUT definitions (TcXunit-6hg, e.g. T_MaxString ->
            // STRING(255)): loaded via DutAliasLoader, same resolution shape
            // as DutStructLoader/GvlLoader above.
            var aliases = DutAliasLoader.Load(args, out var aliasSkipped).ToDictionary(kv => kv.Key, kv => kv.Value);
            skipped.AddRange(aliasSkipped);

            // ENUM .TcDUT definitions (TcXunit-fyu, e.g. E_Color -> INT):
            // registered into the same alias map so SIZEOF() and every other
            // ResolveAlias call site resolves an enum type name to its
            // underlying integer type without a separate lookup path.
            var enumAliases = DutEnumLoader.Load(args, out var enumSkipped, out var enumMembers);
            skipped.AddRange(enumSkipped);
            foreach (var enumAlias in enumAliases)
                aliases[enumAlias.Key] = enumAlias.Value;

            // .TcGVL global variable lists (TcXunit-71o): loaded via
            // GvlLoader, same resilient/fail-fast shape as DutStructLoader
            // above.
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

            // TcXunit-3tx.4: computed from the same loaded types and the same
            // discovered suite names the run uses - the coverage list describes
            // this run's tree, not a separate scan of it. (suiteNames may have
            // been narrowed by --suite just above; that is deliberate, since a
            // filtered run's coverage should describe what that run covered.)
            //
            // Computed BEFORE the no-suites bail-out below: a tree with no
            // suites at all is the case where every POU is uncovered, i.e. the
            // longest and most useful work list there is, and reporting nothing
            // for it would be exactly backwards.
            if (withCoverage)
                coverage = SuiteCoverage.Analyze(types, suiteNames);

            if (suiteNames.Count == 0)
                return WriteError($"no TcUnit suites found under {string.Join(", ", args)}");

            // TcXunit-6k2: plugin-supplied native functions, resolved only
            // after every real POU in the tree has failed to resolve a call
            // (see Engine.CallMethod), so a plugin can never shadow real
            // source. Load failures join the same skip list as unloadable
            // POUs, for the same reason: reduced coverage is reported, not
            // fatal.
            var nativeFunctions = Plugins.NativeFunctionPluginLoader.Load(
                pluginDirectory, out var pluginSkips, out var pluginsLoaded);
            skipped.AddRange(pluginSkips);
            // Streaming suppresses this plain-text line same as the PASS/FAIL
            // lines below (TcXunit-ce1): stdout must stay one-JSON-object-
            // per-line for a --stream consumer, and a bare "plugin: ..." line
            // interleaved with NDJSON would break that.
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
                // TcXunit-ce1: the "discovery" event - every suite about to
                // run, before any of them have. SuiteDiscovery.FindSuiteTypeNames
                // (via suiteNames above) already produces the full suite list
                // independent of execution, so this is just that same list
                // paired with each suite's file path, emitted as the first
                // NDJSON line - a consumer can render every suite as
                // "waiting" immediately instead of only learning suite count
                // from the final summary.
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
                    // Same rationale as SuiteCaseRunner's catch sites (TcXunit-2v8): the
                    // "FAIL (...)" line/SuiteReport.Error only carries ex.Message, the full
                    // ex.ToString() (stack trace + inner exceptions) goes to the TcXunit log
                    // file so this doesn't need re-instrumenting to diagnose.
                    TcXunitLog.LogException($"CliRunner.Run: suite '{suiteName}' failed to run", ex);
                    // TcXunit-p3t.1: name the PLC POU + method that was
                    // executing when it threw, not just the suite. Engine
                    // only produces a PlcSourceLocationException when an
                    // interpreted ST body actually faulted; a load-level
                    // failure (unresolvable type in default-value
                    // construction, say) has no location and keeps the
                    // original single-line shape.
                    var located = ex as PlcSourceLocationException;
                    if (!asJson && !streaming)
                    {
                        // located.Message rather than Location + inner message
                        // (TcXunit-p3t.4/gfs): the exception owns the one rendering
                        // of "where", so the body-relative line appears here and in
                        // the JSON error string below without two formatters to keep
                        // in step - and degrades to the bare "FB_Y.MethodZ: ..."
                        // shape by itself when no line is known.
                        var detail = located != null ? $"in {located.Message}" : ex.Message;
                        output.WriteLine($"{suiteName}: FAIL ({detail})");

                        // TcXunit-7s6: the full interpreted call chain, one frame
                        // per indented line beneath the FAIL line, innermost
                        // first - printed in addition to (never instead of) the
                        // single-line Error above, so existing text-output
                        // consumers that only look at the FAIL line are
                        // unaffected.
                        if (located != null)
                        {
                            foreach (var frame in located.CallStack)
                                output.WriteLine($"    at {frame.LocationWithLine}");
                        }
                    }
                    suiteFilePaths.TryGetValue(suiteName, out var failFilePath);
                    // TcXunit-gfs: the raw .TcPOU XML line, carried as a separate
                    // structured field for a non-interactive consumer (e.g. an AI
                    // agent) that opens the fixture/POU file directly by path
                    // rather than through XAE - null when the failure never
                    // entered an interpreted ST body, or its line is unknown.
                    var fileLine = located != null ? NullableLine(located.Line) : null;
                    // TcXunit-7s6: the ordered call-stack, additive alongside the
                    // pre-existing single-frame Error/FileLine fields - null (not
                    // an empty array) when the failure never entered an
                    // interpreted ST body, so a load-level failure keeps the same
                    // JSON shape it always had.
                    var callStack = located?.CallStack.Select(f => ToCallStackFrameReport(f.Site)).ToArray();
                    // TcXunit-3tx.1: classify once, here, from the exception
                    // itself - the message string is prose for a human and is
                    // never the thing a consumer switches on.
                    var errorKind = FailureClassifier.Classify(ex, out var errorConstruct);
                    // TcXunit-229.15: the one-line error an agent reads gets
                    // that kind's guidance appended, so the JSON object is
                    // self-contained - a consumer never has to have read this
                    // repo's README to know whether to edit the POU or stop.
                    var errorText = WithGuidance(ex.Message, errorKind, isVerbatim: false);
                    // No suite ran to completion here (load/instantiation/default-value
                    // failure), so there's no elapsed time to report - null, not a
                    // fabricated zero (TcXunit-6fb.2). ex.Message already carries the
                    // "FB_Y.MethodZ(5): ..." prefix for an interpreted fault
                    // (TcXunit-p3t.1/gfs), so the error string gets richer without the
                    // JSON wire format changing shape - suites[].error stays a
                    // plain string.
                    suiteReports.Add(new SuiteReport(
                        suiteName, failFilePath, errorText, errorKind, errorConstruct,
                        Array.Empty<TestReport>(), null, fileLine, callStack));
                    failCount++;
                    anyFailed = true;
                    if (streaming)
                    {
                        // TcXunit-ce1: a suite that never ran to completion
                        // (unresolved type, unresolvable method call, ...)
                        // still gets exactly one suite-result line, so a
                        // --stream consumer's "waiting" list always empties
                        // out - it never has to distinguish "suite finished"
                        // from "suite is still stuck". Outcome is "fail" (not
                        // a third "skip" state): it already counts toward
                        // failCount/exitCode above the same as a suite that
                        // ran with a failing test, and `kind` is what tells a
                        // consumer *why* if it wants to render that differently.
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

                        // TcXunit-3tx.3: a fault charged to one test prints its
                        // call chain beneath that test's FAIL line, exactly as
                        // a suite-level error does beneath its own
                        // (TcXunit-7s6) - the fault moved, its diagnostics
                        // didn't.
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
                    // TcXunit-ce1: outcome mirrors what already drives
                    // anyFailed/exitCode above - "fail" if any TEST() in this
                    // suite failed, "pass" otherwise.
                    var suiteOutcome = testReports.Any(t => !t.Passed) ? "fail" : "pass";
                    output.WriteLine(JsonSerializer.Serialize(
                        new SuiteReport(suiteName, filePath, null, null, null, testReports, suiteDurationMs, null, null, "suite-result", suiteOutcome),
                        StreamJsonOptions));
                }
            }

            // Skipped files do not change the exit code (TcXunit-iyd.7): a run
            // that skipped unsupported POUs but ran everything else is still a
            // completed run (0/1 by test outcome), deliberately distinct from
            // the exit 2 reserved for usage/discovery errors that produced no
            // results at all. The skip list + count is what tells the caller
            // coverage was reduced.
            var exitCode = anyFailed ? 1 : 0;

            if (streaming)
            {
                // TcXunit-ce1: the final "summary" NDJSON line - same
                // aggregate data (suites/passed/failed/exitCode/skipped/
                // coverage) as the non-streaming --format json blob below,
                // just tagged with the event so a --stream consumer doesn't
                // need a second code path to read the end-of-run totals.
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

        private static FailureReport ToFailureReport(xStunit.Runner.TcUnitStub.AssertionFailure failure) =>
            new FailureReport(
                WithGuidance(failure),
                failure.Kind,
                failure.Construct ?? ParseErrorConstruct(failure),
                failure.Assert,
                failure.Expected,
                failure.Actual,
                failure.AssertMessage,
                failure.Site.PouTypeName,
                failure.Site.MethodName,
                NullableLine(failure.Site.BodyLine) ?? ParseErrorBodyLine(failure),
                NullableLine(failure.Site.Line),
                failure.CallStack?.Select(ToCallStackFrameReport).ToArray());

        // TcXunit-229.15: a fault charged to a test is classified inside the
        // engine (Engine.Diagnostics.ToTestFailure), which hands this boundary
        // only a kind and a message - so a parse-error's token and line are
        // recovered here, from that message, exactly as the suite-level path
        // recovers them from the exception. Same two fields either way
        // (`construct`, `bodyLine`), never a parse-error-only field.
        private static string ParseErrorConstruct(xStunit.Runner.TcUnitStub.AssertionFailure failure) =>
            failure.Kind == FailureKind.ParseError ? FailureClassifier.OffendingToken(failure.Message) : null;

        // Only ever fills a bodyLine that is otherwise UNKNOWN, and only for a
        // parse-error: a parse failure happens before any statement runs, so
        // the frame it is attributed to has no Stmt.Line to stamp. Leaving it
        // null while this kind's own message says "at line N" would be
        // incoherent - `bodyLine` is the field an agent opens the source with.
        // `line` (the raw .TcPOU XML line) stays null: deriving it needs the
        // body's BodyStartLine, which never reaches this boundary, and a
        // guessed file line is worse than an absent one.
        private static int? ParseErrorBodyLine(xStunit.Runner.TcUnitStub.AssertionFailure failure) =>
            failure.Kind == FailureKind.ParseError
                ? NullableLine(FailureClassifier.ParseErrorBodyLine(failure.Message))
                : null;

        // TcXunit-229.15: the message a consumer reads, followed by what to DO
        // about a failure of that kind (FailureKind.Guidance). TcXunit's
        // consumer is usually a model choosing its next edit from one JSON
        // object; "Unexpected character '@' at position 33" states a fact and
        // answers nothing, and the two possible responses - fix the ST, or stop
        // and escalate - are opposites.
        //
        // The one exception is a formatted TcUnit assert line ("FAILED TEST
        // 'X', EXP: 99, ACT: 3, MSG: ..."), which is reproduced byte for byte
        // from upstream TcUnit's FB_AdsAssertMessageFormatter and is what text
        // output prints and the VSIX results tree renders. That string has a
        // verbatim contract, so guidance is not appended to it - and it is the
        // one message whose factual half already names the change to make.
        //
        // TcXunit-4iop: what identifies that line is the failure's STRUCTURED
        // fields, not its kind. FB_TestSuite.Fail() is the only path that
        // formats that string, and the only path that populates
        // Assert/Expected/Actual/AssertMessage; Engine.Diagnostics.ToTestFailure
        // leaves them null by design ("a contained fault has no expected/actual
        // pair"). So `Expected != null` IS "this Message has a verbatim
        // contract" - no prefix matching, and no second field saying what
        // Expected/Actual already say. Keying on kind instead exempted every
        // assertion-kind failure, including a convergence failure that never
        // went near the formatter, and left Guidance(Assertion) unreachable.
        private static string WithGuidance(xStunit.Runner.TcUnitStub.AssertionFailure failure) =>
            WithGuidance(failure.Message, failure.Kind, isVerbatim: failure.Expected != null);

        // Overload for the two call sites that have no failure object at all -
        // a suite-level error and the run-level ErrorReport. Neither can be a
        // formatter line (no AssertionFailure, so no Expected/Actual), hence
        // isVerbatim spelled out at the call site rather than defaulted here:
        // the exemption should never be something a caller gets by omission.
        private static string WithGuidance(string message, string kind, bool isVerbatim)
        {
            if (isVerbatim)
                return message;

            var guidance = FailureKind.Guidance(kind);
            if (string.IsNullOrEmpty(guidance))
                return message;

            if (kind == FailureKind.ParseError)
            {
                // The form TcXunit-229.9 settled on: say plainly that the body
                // could not be READ, and that the cause is one of two things
                // TcXunit genuinely cannot tell apart - never assert which.
                var bodyLine = FailureClassifier.ParseErrorBodyLine(message);
                var at = bodyLine != PlcSourceLocationException.UnknownLine
                    ? $" at line {bodyLine}"
                    : string.Empty;
                guidance = $"TcXunit could not read this body{at} - " +
                    "either it uses ST beyond TcXunit's subset, or it is invalid ST. " + guidance;
            }

            // " -- " rather than a space: the factual half often ends in ST
            // punctuation (";", ")") or, for a parse-error, in the raw body
            // text itself, so a bare space runs the two halves into one
            // sentence. The delimiter is where "what happened" stops and "what
            // to do" starts.
            return string.IsNullOrEmpty(message) ? guidance : message + " -- " + guidance;
        }

        // Takes an AssertSite, which is what both sources of a frame carry:
        // PlcCallStackFrame.Site for a suite-level error, and the failure's own
        // CallStack entries for a fault contained into a test (TcXunit-3tx.3).
        private static CallStackFrameReport ToCallStackFrameReport(xStunit.Runner.TcUnitStub.AssertSite site) =>
            new CallStackFrameReport(site.PouTypeName, site.MethodName, NullableLine(site.Line), NullableLine(site.BodyLine));

        // TcXunit-gfs/7s6: PlcSourceLocationException.UnknownLine (0) means
        // "no line" the same way for a suite's single FileLine as for every
        // per-frame Line/BodyLine - one place for that sentinel-to-null
        // translation instead of a ternary at each call site.
        private static int? NullableLine(int line) =>
            line != PlcSourceLocationException.UnknownLine ? (int?)line : null;

        // TcXunit-3tx.4: one line per POU, "(none)" spelling out the entries
        // that are the actual work list. No-ops when --coverage wasn't passed.
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

        // TcXunit-3cu: --help/-h text. One place, printed verbatim, kept in
        // sync with the actual flags parsed above rather than duplicating
        // them in a second string - the no-args usage line above stays a
        // short pointer to this instead of repeating the descriptions.
        private static readonly string HelpText =
@"tcxunit - xUnit-style test runner for TwinCAT/IEC 61131-3 PLC code (no TwinCAT runtime required)

Usage:
  tcxunit <path-to-POUs-directory> [<path-to-POUs-directory> ...] [options]

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
                        ITcXunitNativeFunction, for compiled-only TwinCAT
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
  tcxunit ./Plc/POUs
      Run every suite found under ./Plc/POUs, plain text output.

  tcxunit ./src ./tests --format json
      Union two directories and print one JSON result - for a script or an
      agent to parse instead of scraping console text.

  tcxunit ./Plc/POUs --suite FB_CounterTests --suite FB_ClampedCounterTests
      Run only the named suites (e.g. re-running just the ones that failed).

  tcxunit ./Plc/POUs --plugins ./plugins/bin/Release/netstandard2.0
      Resolve compiled-only library calls via native-function plugins.

  tcxunit ./Plc/POUs --coverage
      List every non-suite POU with the suites exercising it; ""(none)""
      marks a POU with no test coverage yet.

  tcxunit ./Plc/POUs --stream
      Emit one NDJSON event per line as suites run, for a live progress UI.";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // TcXunit-ce1: --stream's wire format - one compact JSON object per
        // line (WriteIndented: false, unlike JsonOptions above), since a
        // multi-line indented object would break NDJSON's one-line-per-event
        // contract.
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
                // TcXunit-229.15: always load-error here (see Kind below), so
                // the guidance is appended once, in the constructor, rather
                // than at each of WriteError's call sites. Text output keeps
                // the bare "error: <message>" line - it is read by a human at
                // a console, who has the README; this string is the one an
                // agent reads with nothing else to go on.
                Error = WithGuidance(error, FailureKind.LoadError, isVerbatim: false);
                Skipped = skipped;
                Coverage = coverage;
            }

            // TcXunit-ce1: set ("error") only when WriteError serializes this
            // for --stream; null (and so omitted, JsonIgnoreCondition.
            // WhenWritingNull) for the pre-existing --format json ErrorReport
            // shape, which every non-streaming caller still gets unchanged.
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string Event { get; }

            public string Error { get; }

            // TcXunit-3tx.1: every error reported through this shape is a
            // usage/discovery failure - a bad path, an inconsistent directory
            // set, a tree with no suites - so it is always load-error. Emitted
            // as a constant rather than omitted so a consumer reads `kind` the
            // same way whether the run died before any suite ran or one suite
            // failed inside it.
            public string Kind => FailureKind.LoadError;

            // Skips collected before the error surfaced (TcXunit-iyd.7) - e.g.
            // "no TcUnit suites found" in a tree where every candidate POU was
            // outside the v1 parse subset: without this the caller sees only
            // "nothing found" and no reason why.
            public IReadOnlyList<SkipReport> Skipped { get; }

            // TcXunit-3tx.4: --coverage still reports here. "No suites found"
            // is a usage error for a run, but for a work list it is the most
            // informative answer there is - every POU in the tree is uncovered.
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public IReadOnlyList<CoverageReport> Coverage { get; }
        }

        // One unloadable file (TcXunit-iyd.7): reported rather than aborting
        // the run. FilePath is the full on-disk path, so the offending file is
        // unambiguous when several merged directories contain same-named POUs.
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

        // Shapes for `--format json` (TcXunit prototype spike: structured output
        // for non-console consumers such as a VSIX tool window). Deliberately
        // separate from TestCaseResult/AssertionFailure so the wire format is
        // stable even if the interpreter's internal model changes.
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

            // TcXunit-ce1: set ("summary") only for --stream's final NDJSON
            // line; null (and so omitted) for the pre-existing --format json
            // blob, which keeps its exact prior shape.
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string Event { get; }

            public IReadOnlyList<SuiteReport> Suites { get; }
            public int Passed { get; }
            public int Failed { get; }
            public int ExitCode { get; }

            // Files that couldn't be loaded (TcXunit-iyd.7). Always present
            // (empty array when nothing was skipped) so consumers can read it
            // unconditionally.
            public IReadOnlyList<SkipReport> Skipped { get; }

            // TcXunit-3tx.4: one entry per non-suite POU with the suites
            // exercising it. Emitted only under --coverage - null (and so
            // omitted below) otherwise, since an absent key and an empty list
            // would otherwise be indistinguishable from "everything is
            // uncovered".
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public IReadOnlyList<CoverageReport> Coverage { get; }
        }

        // One POU's coverage (TcXunit-3tx.4). An entry whose `suites` is empty
        // is the interesting one: it is a directly usable next task ("write a
        // suite for F_ComputeChecksum").
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

        // TcXunit-ce1: --stream's first NDJSON line - every suite about to
        // run, so a consumer can render a "waiting" list before any of them
        // start. One entry per suite name SuiteDiscovery.FindSuiteTypeNames
        // found (after --suite filtering), paired with the same file path
        // suites[].filePath uses elsewhere.
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

        // TcXunit-ce1: one of these per suite, immediately before
        // engine.RunSuite is called for it - the "running" half of
        // waiting/running/pass/fail/skip, paired with the suite-result line
        // (a SuiteReport with Event="suite-result") emitted once that suite
        // finishes.
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

            // TcXunit-ce1: set ("suite-result") only when this SuiteReport is
            // serialized standalone as one --stream NDJSON line; null (and so
            // omitted) both in the final summary's suites[] array and in the
            // pre-existing --format json blob, so neither shape gains a field.
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string Event { get; }

            // TcXunit-ce1: "pass"/"fail" for the same --stream suite-result
            // line, null everywhere else (same rationale as Event above). A
            // suite that never ran to completion (Error != null) still
            // reports "fail" here rather than a third "skip" state - it
            // already counts toward failCount/exitCode the same as a suite
            // that ran with a failing TEST(), and `kind` is what tells a
            // consumer *why* if it wants to render that case differently.
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string Outcome { get; }

            public string Name { get; }
            public string FilePath { get; }
            public string Error { get; }

            // TcXunit-3tx.1: the machine-readable counterpart to Error - one of
            // the FailureKind constants, null for a suite that didn't fail.
            // Deliberately a SIBLING field rather than turning Error into an
            // object: the VSIX results tree reads suites[].error as a string,
            // and an additive field costs it nothing.
            //
            // The distinction that matters to an agent consuming this is
            // unsupported-construct vs. everything else: it means the ST is
            // correct and TcXunit is behind, so the POU must not be edited.
            //
            // TcXunit-229.15 (BREAKING): serialized as `kind`, not the
            // `errorKind` this used to emit. `kind`/`construct` is now the one
            // vocabulary at every level of the JSON - the top-level error, this
            // suite-level error, and each per-test failure - so a consumer
            // reads the same two keys wherever a failure surfaces instead of
            // learning that suites happen to spell them differently.
            public string Kind { get; }

            // The specific construct behind the error: the unimplemented ST
            // construct for an unsupported-construct (e.g. "SEL"), or the
            // offending token for a parse-error, so escalation names it without
            // parsing Error. Null for every other kind, and for a throw site
            // that knew only that something was unsupported.
            //
            // TcXunit-229.15 (BREAKING): serialized as `construct`, not
            // `errorConstruct` - same rationale as Kind above.
            public string Construct { get; }
            public IReadOnlyList<TestReport> Tests { get; }

            // TcXunit-6fb.2: suite-level wall-clock time from Engine.RunSuite's
            // stopwatch, alongside each test's existing durationMs. Null (not a
            // fabricated 0) when the suite never ran to completion - see the
            // suite-load-failure catch above.
            public long? DurationMs { get; }

            // TcXunit-gfs: the raw .TcPOU XML line for a suite failure whose
            // location is known (a PlcSourceLocationException with a known
            // line) - Error's "FB_Y.MethodZ(N): ..." prefix carries the
            // XAE-body-relative line instead (TcXunit-gfs), so this is the one
            // place the raw file line still surfaces for a consumer opening
            // the .TcPOU file directly. Null for a passing suite, a suite-load
            // failure with no PLC location, or a fault whose line is unknown.
            public int? FileLine { get; }

            // TcXunit-7s6: the full interpreted call chain behind Error,
            // innermost frame first (CallStack[0] describes the same fault as
            // Error/FileLine above), suite entry point last. Additive - Error
            // keeps its pre-existing single-line shape - and null (not an
            // empty array) for a passing suite or a load-level failure that
            // never entered an interpreted ST body.
            public IReadOnlyList<CallStackFrameReport> CallStack { get; }
        }

        // One PLC-level frame of a call-stack JSON entry (TcXunit-7s6),
        // mirroring PlcCallStackFrame's own fields. A separate DTO rather than
        // serializing PlcCallStackFrame directly, same rationale as the other
        // *Report types: the wire format stays stable even if the
        // interpreter's internal model changes.
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

            // Null for a frame with no method to name (a suite body, a bare-
            // invoked FB body, or a StepCycles cycle) - same convention as
            // PlcSourceLocationException.MethodName.
            public string MethodName { get; }

            // The raw .TcPOU XML line, or null when unknown - same convention
            // as SuiteReport.FileLine, applied per frame.
            public int? Line { get; }

            // The XAE-implementation-editor-relative line, or null when
            // unknown.
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

        // One per-test failure (TcXunit-3tx.1/.2). Was a bare string; the
        // formatted line lives on in Message, so nothing readable was lost,
        // but everything a consumer previously had to regex back out of that
        // string is now a field of its own.
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

            // The formatted line text output prints. For an assert failure this
            // is verbatim what text output prints; for a fault charged to this
            // test it is the located message plus that kind's guidance
            // (TcXunit-229.15, see WithGuidance).
            public string Message { get; }

            // One of the FailureKind constants - the same vocabulary, under the
            // same key, that the top-level error and suites[].kind use
            // (TcXunit-229.15).
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

            // TcXunit-3tx.3: for a fault charged to this test, the full
            // interpreted call chain behind it, innermost frame first - the
            // same array (and the same contract) suites[].callStack carries for
            // a suite-level error, since a contained fault is the same fault.
            // Null for an assertion failure, whose Pou/Method/BodyLine above
            // already say where it is written.
            public IReadOnlyList<CallStackFrameReport> CallStack { get; }
        }
    }
}
