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
            pouDirectories
                .SelectMany(dir => Directory.GetFiles(dir, "*.TcPOU", SearchOption.AllDirectories))
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
