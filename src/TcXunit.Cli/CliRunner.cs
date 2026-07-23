using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            if (args.Length == 0)
            {
                output.WriteLine("usage: tcxunit run <path-to-POUs-directory> [<path-to-POUs-directory> ...]");
                return 2;
            }

            foreach (var path in args)
            {
                if (!Directory.Exists(path))
                {
                    output.WriteLine($"error: path does not exist: {path}");
                    return 2;
                }
            }

            IReadOnlyList<LoadedPou> loaded;
            try
            {
                loaded = MultiDirectoryPouLoader.Load(args);
            }
            catch (DuplicatePouTypeException ex)
            {
                output.WriteLine($"error: {ex.Message}");
                return 2;
            }
            catch (TcPouRejectedException ex)
            {
                output.WriteLine($"error: {ex.Message}");
                return 2;
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
                output.WriteLine($"error: {ex.Message}");
                return 2;
            }

            var registry = new TypeRegistry(types, structTypes);
            var suiteNames = SuiteDiscovery.FindSuiteTypeNames(registry, types.Select(t => t.Name));

            if (suiteNames.Count == 0)
            {
                output.WriteLine($"error: no TcUnit suites found under {string.Join(", ", args)}");
                return 2;
            }

            var engine = new Engine(registry);
            var anyFailed = false;
            var passCount = 0;
            var failCount = 0;

            foreach (var suiteName in suiteNames)
            {
                IReadOnlyList<TcXunit.Runner.TcUnitStub.TestCaseResult> results;
                try
                {
                    results = engine.RunSuite(suiteName);
                }
                catch (Exception ex)
                {
                    output.WriteLine($"{suiteName}: FAIL ({ex.Message})");
                    failCount++;
                    anyFailed = true;
                    continue;
                }

                foreach (var result in results)
                {
                    output.WriteLine(result.ToString());
                    if (result.Passed)
                        passCount++;
                    else
                    {
                        failCount++;
                        anyFailed = true;
                    }
                }
            }

            output.WriteLine($"{passCount} passed, {failCount} failed");
            return anyFailed ? 1 : 0;
        }
    }
}
