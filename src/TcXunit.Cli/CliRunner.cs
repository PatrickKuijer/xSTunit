using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TcXunit.Interpreter;
using TcXunit.Interpreter.Logging;
using TcXunit.Parser;

namespace TcXunit.Cli
{
    // Testable core of `tcxunit run <path>` (TcXunit-w5x.7 item 5). Program.Main
    // is a thin wrapper so this can be driven from xUnit without a subprocess.
    public static class CliRunner
    {
        public static int Run(string[] args, TextWriter output)
        {
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
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.StartsWith("--plugins=", StringComparison.OrdinalIgnoreCase))
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
                output.WriteLine("usage: tcxunit run <path-to-POUs-directory> [<path-to-POUs-directory> ...] [--format text|json] [--plugins <dir>]");
                return 2;
            }

            // Files that couldn't be loaded but must not abort the run
            // (TcXunit-iyd.7) - reported individually at the end of the run
            // instead of collapsing the whole invocation into a discovery
            // error.
            var skipped = new List<SkippedFile>();

            int WriteError(string message)
            {
                if (asJson)
                {
                    output.WriteLine(JsonSerializer.Serialize(new ErrorReport(message, ToSkipReports(skipped)), JsonOptions));
                }
                else
                {
                    WriteSkipLines(output, skipped);
                    output.WriteLine($"error: {message}");
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
            if (!asJson && pluginDirectory != null)
            {
                foreach (var plugin in pluginsLoaded)
                    output.WriteLine($"plugin: {plugin}");
            }

            var engine = new Engine(registry, nativeFunctions);
            var anyFailed = false;
            var passCount = 0;
            var failCount = 0;
            var suiteReports = new List<SuiteReport>();

            foreach (var suiteName in suiteNames)
            {
                IReadOnlyList<TcXunit.Runner.TcUnitStub.TestCaseResult> results;
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
                    if (!asJson)
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
                    var callStack = located?.CallStack.Select(ToCallStackFrameReport).ToArray();
                    // No suite ran to completion here (load/instantiation/default-value
                    // failure), so there's no elapsed time to report - null, not a
                    // fabricated zero (TcXunit-6fb.2). ex.Message already carries the
                    // "FB_Y.MethodZ(5): ..." prefix for an interpreted fault
                    // (TcXunit-p3t.1/gfs), so the error string gets richer without the
                    // JSON wire format changing shape - suites[].error stays a
                    // plain string.
                    suiteReports.Add(new SuiteReport(suiteName, failFilePath, ex.Message, Array.Empty<TestReport>(), null, fileLine, callStack));
                    failCount++;
                    anyFailed = true;
                    continue;
                }

                var testReports = new List<TestReport>();
                foreach (var result in results)
                {
                    if (!asJson)
                        output.WriteLine(result.ToString());
                    testReports.Add(new TestReport(result.Name, result.Passed, result.Failures.Select(f => f.Message).ToArray(), result.ElapsedMilliseconds));
                    if (result.Passed)
                        passCount++;
                    else
                    {
                        failCount++;
                        anyFailed = true;
                    }
                }

                suiteFilePaths.TryGetValue(suiteName, out var filePath);
                suiteReports.Add(new SuiteReport(suiteName, filePath, null, testReports, suiteDurationMs, null, null));
            }

            // Skipped files do not change the exit code (TcXunit-iyd.7): a run
            // that skipped unsupported POUs but ran everything else is still a
            // completed run (0/1 by test outcome), deliberately distinct from
            // the exit 2 reserved for usage/discovery errors that produced no
            // results at all. The skip list + count is what tells the caller
            // coverage was reduced.
            var exitCode = anyFailed ? 1 : 0;

            if (asJson)
            {
                output.WriteLine(JsonSerializer.Serialize(
                    new RunReport(suiteReports, passCount, failCount, exitCode, ToSkipReports(skipped)), JsonOptions));
            }
            else
            {
                WriteSkipLines(output, skipped);
                output.WriteLine(skipped.Count > 0
                    ? $"{passCount} passed, {failCount} failed, {skipped.Count} skipped"
                    : $"{passCount} passed, {failCount} failed");
            }

            return exitCode;
        }

        private static CallStackFrameReport ToCallStackFrameReport(PlcCallStackFrame frame) =>
            new CallStackFrameReport(frame.PouTypeName, frame.MethodName, NullableLine(frame.Line), NullableLine(frame.BodyLine));

        // TcXunit-gfs/7s6: PlcSourceLocationException.UnknownLine (0) means
        // "no line" the same way for a suite's single FileLine as for every
        // per-frame Line/BodyLine - one place for that sentinel-to-null
        // translation instead of a ternary at each call site.
        private static int? NullableLine(int line) =>
            line != PlcSourceLocationException.UnknownLine ? (int?)line : null;

        private static void WriteSkipLines(TextWriter output, IReadOnlyList<SkippedFile> skipped)
        {
            foreach (var skip in skipped)
                output.WriteLine($"skipped: {skip.FileKey} ({skip.Message})");
        }

        private static IReadOnlyList<SkipReport> ToSkipReports(IReadOnlyList<SkippedFile> skipped) =>
            skipped.Select(s => new SkipReport(s.FileKey, s.Message)).ToList();

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private sealed class ErrorReport
        {
            public ErrorReport(string error, IReadOnlyList<SkipReport> skipped)
            {
                Error = error;
                Skipped = skipped;
            }

            public string Error { get; }

            // Skips collected before the error surfaced (TcXunit-iyd.7) - e.g.
            // "no TcUnit suites found" in a tree where every candidate POU was
            // outside the v1 parse subset: without this the caller sees only
            // "nothing found" and no reason why.
            public IReadOnlyList<SkipReport> Skipped { get; }
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
                IReadOnlyList<SuiteReport> suites, int passed, int failed, int exitCode, IReadOnlyList<SkipReport> skipped)
            {
                Suites = suites;
                Passed = passed;
                Failed = failed;
                ExitCode = exitCode;
                Skipped = skipped;
            }

            public IReadOnlyList<SuiteReport> Suites { get; }
            public int Passed { get; }
            public int Failed { get; }
            public int ExitCode { get; }

            // Files that couldn't be loaded (TcXunit-iyd.7). Always present
            // (empty array when nothing was skipped) so consumers can read it
            // unconditionally.
            public IReadOnlyList<SkipReport> Skipped { get; }
        }

        private sealed class SuiteReport
        {
            public SuiteReport(
                string name,
                string filePath,
                string error,
                IReadOnlyList<TestReport> tests,
                long? durationMs,
                int? fileLine,
                IReadOnlyList<CallStackFrameReport> callStack)
            {
                Name = name;
                FilePath = filePath;
                Error = error;
                Tests = tests;
                DurationMs = durationMs;
                FileLine = fileLine;
                CallStack = callStack;
            }

            public string Name { get; }
            public string FilePath { get; }
            public string Error { get; }
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
            public TestReport(string name, bool passed, IReadOnlyList<string> failures, long durationMs)
            {
                Name = name;
                Passed = passed;
                Failures = failures;
                DurationMs = durationMs;
            }

            public string Name { get; }
            public bool Passed { get; }
            public IReadOnlyList<string> Failures { get; }
            public long DurationMs { get; }
        }
    }
}
