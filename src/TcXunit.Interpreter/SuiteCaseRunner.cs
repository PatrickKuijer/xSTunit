using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TcXunit.Parser;
using TcXunit.Runner.TcUnitStub;

namespace TcXunit.Interpreter
{
    public readonly struct SuiteCase
    {
        public string SuiteName { get; }
        public string CaseName { get; }

        public SuiteCase(string suiteName, string caseName)
        {
            SuiteName = suiteName;
            CaseName = caseName;
        }
    }

    // Discovery/execution surface for VS Test Explorer integration
    // (TcXunit-w5x.14): lists every TcUnit case under a PLC POUs directory so
    // each can be surfaced as its own xUnit test, and re-runs a single suite
    // to fetch one case's result on demand.
    public static class SuiteCaseRunner
    {
        // Synthetic case name used for POUs that couldn't be parsed at all
        // (PLC-cta): surfaced as a failing case named after the POU file
        // rather than being silently dropped from discovery.
        private const string ParseErrorCaseName = "(parse error)";

        public static IReadOnlyList<SuiteCase> DiscoverCases(string pouDirectory)
        {
            var registry = BuildRegistry(pouDirectory, out var typeNames, out var skipped);
            var engine = new Engine(registry);
            var suiteNames = SuiteDiscovery.FindSuiteTypeNames(registry, typeNames);

            var cases = new List<SuiteCase>();
            foreach (var suiteName in suiteNames)
                foreach (var result in engine.RunSuite(suiteName))
                    cases.Add(new SuiteCase(suiteName, result.Name));

            foreach (var skip in skipped)
                cases.Add(new SuiteCase(skip.FileKey, ParseErrorCaseName));

            return cases;
        }

        public static TestCaseResult RunCase(string pouDirectory, string suiteName, string caseName)
        {
            var registry = BuildRegistry(pouDirectory, out _, out var skipped);

            if (caseName == ParseErrorCaseName)
            {
                var skip = skipped.FirstOrDefault(s => s.FileKey == suiteName);
                if (skip.FileKey != null)
                    return new TestCaseResult(caseName, new[] { new AssertionFailure(skip.Message) });
            }

            var engine = new Engine(registry);
            var results = engine.RunSuite(suiteName);

            var match = results.FirstOrDefault(r => r.Name == caseName);
            if (match == null)
                throw new InvalidOperationException($"Case '{caseName}' not found in suite '{suiteName}'");

            return match;
        }

        private readonly struct SkippedPou
        {
            public SkippedPou(string fileKey, string message)
            {
                FileKey = fileKey;
                Message = message;
            }

            public string FileKey { get; }
            public string Message { get; }
        }

        private static TypeRegistry BuildRegistry(
            string pouDirectory, out List<string> typeNames, out List<SkippedPou> skipped)
        {
            var pouFiles = Directory.GetFiles(pouDirectory, "*.TcPOU", SearchOption.AllDirectories);
            var types = new List<PouAst>();
            skipped = new List<SkippedPou>();

            foreach (var file in pouFiles)
            {
                try
                {
                    types.Add(TcPouParser.Parse(File.ReadAllText(file)));
                }
                catch (TcPouRejectedException ex)
                {
                    // Skip POUs outside TcXunit's v1 parse subset (e.g. production
                    // code using Tc2_System) instead of failing the entire scan
                    // (PLC-b62), but surface each as its own failing case (PLC-cta)
                    // instead of silently vanishing from discovery. A suite that
                    // actually depends on a skipped POU will still fail clearly at
                    // run time with an unresolved-type error; suites that don't
                    // need it can run unaffected.
                    skipped.Add(new SkippedPou(Path.GetFileNameWithoutExtension(file), ex.Message));
                }
            }

            typeNames = types.Select(t => t.Name).ToList();
            return new TypeRegistry(types);
        }
    }
}
