using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace xStunit.Parser
{
    /// <summary>
    /// A parsed POU tagged with the file it came from, so duplicate-type
    /// errors and per-file skip/report paths can name the originating file.
    /// </summary>
    public readonly struct LoadedPou
    {
        /// <summary>Constructs a loaded-POU record from its parsed AST and originating file path.</summary>
        public LoadedPou(PouAst pou, string filePath)
        {
            Pou = pou;
            FilePath = filePath;
        }

        /// <summary>The parsed POU.</summary>
        public PouAst Pou { get; }

        /// <summary>The .TcPOU file the POU was parsed from.</summary>
        public string FilePath { get; }
    }

    /// <summary>
    /// Unions POUs across one or more source directories (e.g. a Tests PLC
    /// project and a separately-referenced Source PLC project) instead of
    /// scanning a single directory.
    /// </summary>
    /// <remarks>
    /// Order of the input directories does not matter - paths are unioned,
    /// not layered/precedenced. A type name that appears in more than one
    /// file across the merged set is a fail-fast, hard error
    /// (<see cref="DuplicatePouTypeException"/>) raised before any suite runs.
    /// </remarks>
    public static class MultiDirectoryPouLoader
    {
        /// <summary>Every distinct *.TcPOU file path under the given directories.</summary>
        public static IReadOnlyList<string> FindPouFiles(IReadOnlyList<string> pouDirectories) =>
            DeduplicatePaths(
                pouDirectories
                    .SelectMany(dir => Directory.GetFiles(dir, "*.TcPOU", SearchOption.AllDirectories)));

        /// <summary>
        /// Every distinct *.TcDUT file path under the given directories.
        /// </summary>
        /// <remarks>
        /// Globbed separately from *.TcPOU since .TcDUT files (which declare
        /// STRUCT/ENUM/alias types) use a different root XML element
        /// (&lt;DUT&gt; vs &lt;POU&gt;).
        /// </remarks>
        public static IReadOnlyList<string> FindDutFiles(IReadOnlyList<string> pouDirectories) =>
            DeduplicatePaths(
                pouDirectories
                    .SelectMany(dir => Directory.GetFiles(dir, "*.TcDUT", SearchOption.AllDirectories)));

        /// <summary>
        /// Every distinct *.TcGVL file path under the given directories.
        /// </summary>
        /// <remarks>
        /// Globbed separately from *.TcPOU/*.TcDUT since .TcGVL files
        /// (global variable lists) use their own root XML element
        /// (&lt;GVL&gt; vs &lt;POU&gt;/&lt;DUT&gt;).
        /// </remarks>
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

        /// <summary>
        /// Globs and parses every *.TcPOU file across all given directories,
        /// then checks the successfully-parsed set for duplicate type names.
        /// </summary>
        /// <exception cref="TcPouRejectedException">
        /// A .TcPOU's implementation text uses a construct outside the
        /// parser's supported subset. Propagates uncaught; callers that need
        /// to skip-and-report unparseable POUs instead (e.g. the CLI's suite
        /// discovery) should parse files themselves and call
        /// <see cref="CheckForDuplicates"/> on the successfully-parsed subset.
        /// </exception>
        /// <exception cref="DuplicatePouTypeException">
        /// The same POU type name is defined in more than one file.
        /// </exception>
        public static IReadOnlyList<LoadedPou> Load(IReadOnlyList<string> pouDirectories)
        {
            var loaded = FindPouFiles(pouDirectories)
                .Select(file => new LoadedPou(TcPouParser.Parse(File.ReadAllText(file)), file))
                .ToList();

            CheckForDuplicates(loaded);
            return loaded;
        }

        /// <summary>
        /// Throws <see cref="DuplicatePouTypeException"/> if any two entries
        /// in <paramref name="loaded"/> share a POU type name; does nothing
        /// otherwise.
        /// </summary>
        public static void CheckForDuplicates(IReadOnlyList<LoadedPou> loaded) =>
            DuplicateNameDetector.ThrowIfDuplicate(
                loaded,
                l => l.Pou.Name,
                l => l.FilePath,
                (name, filePaths) => new DuplicatePouTypeException(name, filePaths));
    }
}
