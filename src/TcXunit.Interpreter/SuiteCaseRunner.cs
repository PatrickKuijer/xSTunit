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
        public static IReadOnlyList<SuiteCase> DiscoverCases(string pouDirectory)
        {
            var registry = BuildRegistry(pouDirectory, out var typeNames);
            var engine = new Engine(registry);
            var suiteNames = SuiteDiscovery.FindSuiteTypeNames(registry, typeNames);

            var cases = new List<SuiteCase>();
            foreach (var suiteName in suiteNames)
                foreach (var result in engine.RunSuite(suiteName))
                    cases.Add(new SuiteCase(suiteName, result.Name));

            return cases;
        }

        public static TestCaseResult RunCase(string pouDirectory, string suiteName, string caseName)
        {
            var registry = BuildRegistry(pouDirectory, out _);
            var engine = new Engine(registry);
            var results = engine.RunSuite(suiteName);

            var match = results.FirstOrDefault(r => r.Name == caseName);
            if (match == null)
                throw new InvalidOperationException($"Case '{caseName}' not found in suite '{suiteName}'");

            return match;
        }

        private static TypeRegistry BuildRegistry(string pouDirectory, out List<string> typeNames)
        {
            var pouFiles = Directory.GetFiles(pouDirectory, "*.TcPOU", SearchOption.AllDirectories);
            var types = pouFiles.Select(f => TcPouParser.Parse(File.ReadAllText(f))).ToList();
            typeNames = types.Select(t => t.Name).ToList();
            return new TypeRegistry(types);
        }
    }
}
