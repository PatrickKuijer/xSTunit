using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
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

        public static IReadOnlyList<SuiteCase> DiscoverCases(string pouDirectory) =>
            DiscoverCases(new[] { pouDirectory });

        public static IReadOnlyList<SuiteCase> DiscoverCases(IReadOnlyList<string> pouDirectories)
        {
            var registry = BuildRegistry(pouDirectories, out var typeNames, out var skipped);
            var engine = new Engine(registry);
            var suiteNames = SuiteDiscovery.FindSuiteTypeNames(registry, typeNames);

            var cases = new List<SuiteCase>();
            foreach (var suiteName in suiteNames)
            {
                IReadOnlyList<TestCaseResult> results;
                try
                {
                    results = engine.RunSuite(suiteName);
                }
                catch (Exception)
                {
                    // A suite whose transitive default-value building hits an
                    // interpreter gap (e.g. TcXunit-654's unresolved
                    // constant-expression array bound) must not abort
                    // discovery for every *other* suite in the solution-wide
                    // scan (PLC-b62-style fix, extended to runtime failures
                    // rather than just parse failures). Surfaced as its own
                    // failing synthetic case instead.
                    cases.Add(new SuiteCase(suiteName, ParseErrorCaseName));
                    continue;
                }

                foreach (var result in results)
                    cases.Add(new SuiteCase(suiteName, result.Name));
            }

            foreach (var skip in skipped)
                cases.Add(new SuiteCase(skip.FileKey, ParseErrorCaseName));

            return cases;
        }

        public static TestCaseResult RunCase(string pouDirectory, string suiteName, string caseName) =>
            RunCase(new[] { pouDirectory }, suiteName, caseName);

        public static TestCaseResult RunCase(IReadOnlyList<string> pouDirectories, string suiteName, string caseName)
        {
            var registry = BuildRegistry(pouDirectories, out _, out var skipped);

            if (caseName == ParseErrorCaseName)
            {
                var skip = skipped.FirstOrDefault(s => s.FileKey == suiteName);
                if (skip.FileKey != null)
                    return new TestCaseResult(caseName, new[] { new AssertionFailure(skip.Message) });

                // Not a parse-level skip and suiteName resolves to a real
                // type: re-attempt the suite run so a runtime failure (e.g.
                // TcXunit-654) surfaces the same synthetic failing case
                // DiscoverCases reported, rather than falling through to
                // "case not found" for what is actually a known-bad suite.
                //
                // If suiteName isn't in the registry at all (a stale suite/
                // case pair from an earlier discovery, or the skip set
                // changed between DiscoverCases and RunCase calls), it never
                // parsed successfully, so RunSuite would dereference a null
                // type definition (NullReferenceException) - checked here
                // instead of relying on the catch below, since that generic
                // catch would otherwise swallow the NRE into a failing
                // TestCaseResult rather than the clear not-found error
                // (TcXunit-t0u).
                if (registry.Get(suiteName) == null)
                    throw new InvalidOperationException($"Case '{caseName}' not found in suite '{suiteName}'");

                try
                {
                    new Engine(registry).RunSuite(suiteName);
                }
                catch (Exception ex)
                {
                    return new TestCaseResult(caseName, new[] { new AssertionFailure(ex.Message) });
                }

                throw new InvalidOperationException($"Case '{caseName}' not found in suite '{suiteName}'");
            }

            var engine = new Engine(registry);
            var results = engine.RunSuite(suiteName);

            var match = results.FirstOrDefault(r => r.Name == caseName);
            if (match == null)
                throw new InvalidOperationException($"Case '{caseName}' not found in suite '{suiteName}'");

            return match;
        }

        private static TypeRegistry BuildRegistry(
            IReadOnlyList<string> pouDirectories, out List<string> typeNames, out List<SkippedFile> skipped)
        {
            var pouFiles = MultiDirectoryPouLoader.FindPouFiles(pouDirectories);
            var loaded = new List<LoadedPou>();
            skipped = new List<SkippedFile>();

            foreach (var file in pouFiles)
            {
                try
                {
                    loaded.Add(new LoadedPou(TcPouParser.Parse(File.ReadAllText(file)), file));
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
                    //
                    // Keyed by the full file path (TcXunit-pvp), not the bare
                    // filename: two merged directories (e.g. a Source PLC
                    // project and a Tests PLC project) can each contain a
                    // same-named rejected POU, and a bare-filename key would
                    // collide, producing duplicate SuiteCase entries and
                    // letting RunCase's lookup silently resolve to whichever
                    // file happened to be parsed first. The full path is also
                    // surfaced as the synthetic case's SuiteName, so the
                    // failing file is unambiguous to the user.
                    skipped.Add(new SkippedFile(file, ex.Message));
                }
                catch (Exception ex) when (ex is XmlException || ex is NullReferenceException)
                {
                    // Same rationale as above (PLC-b62/TcXunit-swk): a structurally
                    // unexpected POU (malformed XML, missing Declaration/Implementation/ST,
                    // an interface-only POU, or a GVL/DUT file caught by the *.TcPOU glob)
                    // must not abort discovery for the whole directory either. Keyed
                    // by full file path for the same collision-avoidance reason as
                    // above (TcXunit-pvp).
                    skipped.Add(new SkippedFile(
                        file,
                        $"Failed to parse '{Path.GetFileName(file)}': {ex.Message}"));
                }
            }

            // Detect duplicate type names across the merged set (TcXunit-98e.1)
            // before any suite runs, distinct from the per-file skip/report
            // path above. Unlike CliRunner.Run (which can afford to abort the
            // entire process on a hard error), BuildRegistry backs Test
            // Explorer discovery across a whole solution-wide scan: letting
            // DuplicatePouTypeException/DuplicateStructTypeException/
            // DuplicateGvlNameException propagate here would crash discovery
            // of every *other* suite too (TcXunit-qxp.1). Each duplicate is
            // instead reported as its own synthetic failing case (keyed by
            // the conflicting type/STRUCT/GVL name, since - same as the
            // comments below already noted - it can't be attributed to a
            // single file), and the offending names are dropped from the
            // merged set so unrelated suites still resolve normally.
            while (true)
            {
                try
                {
                    MultiDirectoryPouLoader.CheckForDuplicates(loaded);
                    break;
                }
                catch (TcXunit.Parser.DuplicatePouTypeException ex)
                {
                    skipped.Add(new SkippedFile(ex.TypeName, ex.Message));
                    loaded.RemoveAll(l => l.Pou.Name == ex.TypeName);
                }
            }

            var types = loaded.Select(l => l.Pou).ToList();
            typeNames = types.Select(t => t.Name).ToList();

            // .TcDUT STRUCT types (e.g. HMI-mirror structs referenced by
            // production FBs), shared with CliRunner via DutStructLoader
            // (TcXunit-9li) so both entry points resolve STRUCT-typed DUTs
            // identically. Keyed by full file path for the same
            // collision-avoidance reason as the POU skip path (TcXunit-pvp).
            IReadOnlyList<StructAst> structTypes;
            try
            {
                structTypes = DutStructLoader.Load(pouDirectories, out var dutSkipped);
                skipped.AddRange(dutSkipped);
            }
            catch (DuplicateStructTypeException ex)
            {
                // Same rationale as the POU duplicate handling above
                // (TcXunit-qxp.1): DutStructLoader has no API to re-load
                // "everything except this name" (it re-scans directories from
                // scratch), so on a duplicate the whole STRUCT category is
                // dropped for this registry build; any suite that actually
                // depends on a STRUCT-typed DUT still fails clearly at run
                // time via the existing unresolved-type path, and the
                // duplicate itself is surfaced as its own synthetic case.
                structTypes = Array.Empty<StructAst>();
                skipped.Add(new SkippedFile(ex.TypeName, ex.Message));
            }

            // ALIAS .TcDUT definitions (TcXunit-6hg, e.g. T_MaxString ->
            // STRING(255)), shared with CliRunner via DutAliasLoader for the
            // same reason DUT struct types/GVLs are shared above.
            var aliases = DutAliasLoader.Load(pouDirectories, out var aliasSkipped);
            skipped.AddRange(aliasSkipped);

            // .TcGVL global variable lists (TcXunit-71o), shared with
            // CliRunner via GvlLoader for the same reason DUT struct types
            // are shared above - both entry points must resolve GVLs
            // identically. A duplicate GVL name can't be attributed to a
            // single suite (mirrors MultiDirectoryPouLoader.CheckForDuplicates
            // above), but per TcXunit-qxp.1 must still not crash discovery of
            // every other suite - handled the same way as the STRUCT
            // duplicate case above.
            IReadOnlyList<GvlAst> gvls;
            try
            {
                gvls = GvlLoader.Load(pouDirectories, out var gvlSkipped);
                skipped.AddRange(gvlSkipped);
            }
            catch (DuplicateGvlNameException ex)
            {
                gvls = Array.Empty<GvlAst>();
                skipped.Add(new SkippedFile(ex.GvlName, ex.Message));
            }

            return new TypeRegistry(types, structTypes, gvls, aliases);
        }
    }
}
