using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace xStunit.Parser
{
    /// <summary>
    /// A parsed POU tagged with the file it came from, so duplicate-type
    /// errors and skip/report paths can name the originating file.
    /// </summary>
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

    /// <summary>
    /// Unions POUs across one or more source directories (e.g. a Tests PLC
    /// project plus a separately-referenced Source PLC project).
    /// </summary>
    /// <remarks>
    /// Directory order does not matter - paths are unioned, not
    /// layered/precedenced, so no directory can shadow another's type.
    /// </remarks>
    public static class MultiDirectoryPouLoader
    {
        public static IReadOnlyList<string> FindPouFiles(IReadOnlyList<string> pouDirectories) =>
            DeduplicatePaths(
                pouDirectories
                    .SelectMany(dir => Directory.GetFiles(dir, "*.TcPOU", SearchOption.AllDirectories)));

        // .TcDUT, .TcGVL and .TcIO are globbed separately from .TcPOU rather
        // than in one pass because each uses its own root XML element (<DUT>,
        // <GVL>, <Itf>, <POU>) and so needs a different parser.
        public static IReadOnlyList<string> FindDutFiles(IReadOnlyList<string> pouDirectories) =>
            DeduplicatePaths(
                pouDirectories
                    .SelectMany(dir => Directory.GetFiles(dir, "*.TcDUT", SearchOption.AllDirectories)));

        public static IReadOnlyList<string> FindGvlFiles(IReadOnlyList<string> pouDirectories) =>
            DeduplicatePaths(
                pouDirectories
                    .SelectMany(dir => Directory.GetFiles(dir, "*.TcGVL", SearchOption.AllDirectories)));

        public static IReadOnlyList<string> FindInterfaceFiles(IReadOnlyList<string> pouDirectories) =>
            DeduplicatePaths(
                pouDirectories
                    .SelectMany(dir => Directory.GetFiles(dir, "*.TcIO", SearchOption.AllDirectories)));

        // Normalizes and de-duplicates case-insensitively so overlapping input
        // directories (same directory passed twice, one nested inside another,
        // or differing only by casing/trailing slash) behave as a true union
        // rather than yielding the same on-disk file more than once.
        private static IReadOnlyList<string> DeduplicatePaths(IEnumerable<string> files) =>
            files
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// Globs and parses every .TcPOU across all given directories, then
        /// checks the result for duplicate type names.
        /// </summary>
        /// <exception cref="TcPouRejectedException">
        /// A POU uses a construct outside the supported subset. Propagates
        /// UNCAUGHT and abandons the whole load - callers that need to
        /// skip-and-report instead must parse files themselves and call
        /// <see cref="CheckForDuplicates"/> on what survived.
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

        /// <exception cref="DuplicatePouTypeException">
        /// Two or more entries share a POU type name.
        /// </exception>
        public static void CheckForDuplicates(IReadOnlyList<LoadedPou> loaded) =>
            DuplicateNameDetector.ThrowIfDuplicate(
                loaded,
                l => l.Pou.Name,
                l => l.FilePath,
                (name, filePaths) => new DuplicatePouTypeException(name, filePaths));
    }
}
