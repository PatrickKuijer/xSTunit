using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TcXunit.Interpreter;
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
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
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
                output.WriteLine("usage: tcxunit run <path-to-POUs-directory> [<path-to-POUs-directory> ...] [--format text|json]");
                return 2;
            }

            int WriteError(string message)
            {
                if (asJson)
                    output.WriteLine(JsonSerializer.Serialize(new ErrorReport(message), JsonOptions));
                else
                    output.WriteLine($"error: {message}");
                return 2;
            }

            foreach (var path in args)
            {
                if (!Directory.Exists(path))
                    return WriteError($"path does not exist: {path}");
            }

            IReadOnlyList<LoadedPou> loaded;
            try
            {
                loaded = MultiDirectoryPouLoader.Load(args);
            }
            catch (DuplicatePouTypeException ex)
            {
                return WriteError(ex.Message);
            }
            catch (TcPouRejectedException ex)
            {
                return WriteError(ex.Message);
            }

            var types = loaded.Select(l => l.Pou).ToList();

            // .TcDUT STRUCT types (TcXunit-9li): shared with SuiteCaseRunner
            // via DutStructLoader so `tcxunit run` resolves STRUCT-typed DUTs
            // the same way Test Explorer discovery does, instead of silently
            // failing to resolve any suite/FB that depends on one. Per-file
            // parse failures are isolated the same resilient way
            // DutStructLoader isolates them for SuiteCaseRunner (unsupported
            // DUT kinds are skipped, not fatal); a duplicate STRUCT name
            // across files is a hard error, same as a duplicate POU type
            // name above.
            IReadOnlyList<StructAst> structTypes;
            try
            {
                structTypes = DutStructLoader.Load(args, out _);
            }
            catch (DuplicateStructTypeException ex)
            {
                return WriteError(ex.Message);
            }

            // ALIAS .TcDUT definitions (TcXunit-6hg, e.g. T_MaxString ->
            // STRING(255)): shared with SuiteCaseRunner via DutAliasLoader
            // for the same "both entry points resolve DUTs identically"
            // reason as DutStructLoader/GvlLoader above.
            var aliases = DutAliasLoader.Load(args, out _).ToDictionary(kv => kv.Key, kv => kv.Value);

            // ENUM .TcDUT definitions (TcXunit-fyu, e.g. E_Color -> INT):
            // registered into the same alias map so SIZEOF() and every other
            // ResolveAlias call site resolves an enum type name to its
            // underlying integer type without a separate lookup path.
            var enumAliases = DutEnumLoader.Load(args, out _, out var enumMembers);
            foreach (var enumAlias in enumAliases)
                aliases[enumAlias.Key] = enumAlias.Value;

            // .TcGVL global variable lists (TcXunit-71o): shared with
            // SuiteCaseRunner via GvlLoader, same resilient/fail-fast shape
            // as DutStructLoader above.
            IReadOnlyList<GvlAst> gvls;
            try
            {
                gvls = GvlLoader.Load(args, out _);
            }
            catch (DuplicateGvlNameException ex)
            {
                return WriteError(ex.Message);
            }

            var registry = new TypeRegistry(types, structTypes, gvls, aliases, enumMembers);
            var suiteNames = SuiteDiscovery.FindSuiteTypeNames(registry, types.Select(t => t.Name));
            var suiteFilePaths = loaded.ToDictionary(l => l.Pou.Name, l => l.FilePath);

            if (suiteNames.Count == 0)
                return WriteError($"no TcUnit suites found under {string.Join(", ", args)}");

            var engine = new Engine(registry);
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
                    if (!asJson)
                        output.WriteLine($"{suiteName}: FAIL ({ex.Message})");
                    suiteFilePaths.TryGetValue(suiteName, out var failFilePath);
                    // No suite ran to completion here (load/instantiation/default-value
                    // failure), so there's no elapsed time to report - null, not a
                    // fabricated zero (TcXunit-6fb.2).
                    suiteReports.Add(new SuiteReport(suiteName, failFilePath, ex.Message, Array.Empty<TestReport>(), null));
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
                suiteReports.Add(new SuiteReport(suiteName, filePath, null, testReports, suiteDurationMs));
            }

            var exitCode = anyFailed ? 1 : 0;

            if (asJson)
            {
                output.WriteLine(JsonSerializer.Serialize(new RunReport(suiteReports, passCount, failCount, exitCode), JsonOptions));
            }
            else
            {
                output.WriteLine($"{passCount} passed, {failCount} failed");
            }

            return exitCode;
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private sealed class ErrorReport
        {
            public ErrorReport(string error)
            {
                Error = error;
            }

            public string Error { get; }
        }

        // Shapes for `--format json` (TcXunit prototype spike: structured output
        // for non-console consumers such as a VSIX tool window). Deliberately
        // separate from TestCaseResult/AssertionFailure so the wire format is
        // stable even if the interpreter's internal model changes.
        private sealed class RunReport
        {
            public RunReport(IReadOnlyList<SuiteReport> suites, int passed, int failed, int exitCode)
            {
                Suites = suites;
                Passed = passed;
                Failed = failed;
                ExitCode = exitCode;
            }

            public IReadOnlyList<SuiteReport> Suites { get; }
            public int Passed { get; }
            public int Failed { get; }
            public int ExitCode { get; }
        }

        private sealed class SuiteReport
        {
            public SuiteReport(string name, string filePath, string error, IReadOnlyList<TestReport> tests, long? durationMs)
            {
                Name = name;
                FilePath = filePath;
                Error = error;
                Tests = tests;
                DurationMs = durationMs;
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
