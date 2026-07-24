using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TcXunit.Parser
{
    // A parsed POU tagged with the file it came from (TcXunit-98e.1), so
    // duplicate-type errors and per-file skip/report paths can name the
    // originating file.
    public readonly struct LoadedPou
    {
        public LoadedPou(PouAst pou, string filePath)
        {
            Pou = pou;
            FilePath = filePath;
        }

        public PouAst Pou { get; }
        public string FilePath { get; }
    }

    // Unions POUs across one-or-more source directories (TcXunit-98e: a Tests
    // PLC project and a separately-referenced Source PLC project) instead of
    // scanning a single directory. Order of the input directories does not
    // matter -- paths are unioned, not layered/precedenced. A type name that
    // appears in more than one file across the merged set is a fail-fast,
    // hard error (DuplicatePouTypeException) raised before any suite runs.
    public static class MultiDirectoryPouLoader
    {
        public static IReadOnlyList<string> FindPouFiles(IReadOnlyList<string> pouDirectories) =>
            DeduplicatePaths(
                pouDirectories
                    .SelectMany(dir => Directory.GetFiles(dir, "*.TcPOU", SearchOption.AllDirectories)));

        // .TcDUT files declare STRUCT/ENUM/alias types (TcXunit-w5x.15.6's
        // struct-DUT gap) - globbed separately from *.TcPOU since they use a
        // different root XML element (<DUT> vs <POU>).
        public static IReadOnlyList<string> FindDutFiles(IReadOnlyList<string> pouDirectories) =>
            DeduplicatePaths(
                pouDirectories
                    .SelectMany(dir => Directory.GetFiles(dir, "*.TcDUT", SearchOption.AllDirectories)));

        // .TcGVL files declare GVL (global variable list) types (TcXunit-71o)
        // - globbed separately from *.TcPOU/*.TcDUT since they use their own
        // root XML element (<GVL> vs <POU>/<DUT>).
        public static IReadOnlyList<string> FindGvlFiles(IReadOnlyList<string> pouDirectories) =>
            DeduplicatePaths(
                pouDirectories
                    .SelectMany(dir => Directory.GetFiles(dir, "*.TcGVL", SearchOption.AllDirectories)));

        // Normalizes each path (Path.GetFullPath) and de-duplicates
        // case-insensitively so overlapping input directories (same
        // directory passed twice, one nested inside another, or differing
        // only by casing/trailing slash) behave as a true union rather than
        // producing the same on-disk file more than once.
        private static IReadOnlyList<string> DeduplicatePaths(IEnumerable<string> files) =>
            files
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        // Globs and parses every *.TcPOU file across all given directories.
        // Parse failures (TcPouRejectedException) propagate to the caller
        // uncaught; callers that need to skip-and-report unparseable POUs
        // (e.g. SuiteCaseRunner) should parse files themselves and call
        // CheckForDuplicates on the successfully-parsed subset instead.
        public static IReadOnlyList<LoadedPou> Load(IReadOnlyList<string> pouDirectories)
        {
            var loaded = FindPouFiles(pouDirectories)
                .Select(file => new LoadedPou(TcPouParser.Parse(File.ReadAllText(file)), file))
                .ToList();

            CheckForDuplicates(loaded);
            return loaded;
        }

        public static void CheckForDuplicates(IReadOnlyList<LoadedPou> loaded)
        {
            var duplicate = loaded
                .GroupBy(l => l.Pou.Name)
                .FirstOrDefault(g => g.Count() > 1);

            if (duplicate != null)
                throw new DuplicatePouTypeException(duplicate.Key, duplicate.Select(l => l.FilePath).ToList());
        }
    }
}
