using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using xStunit.Parser;

namespace xStunit.Interpreter
{
    // Loads .TcGVL global variable lists across the merged set of POU
    // directories. Same resilience as DutStructLoader: a structurally
    // unexpected .TcGVL file is skipped and reported rather than aborting
    // registry build for the whole directory.
    public static class GvlLoader
    {
        public static IReadOnlyList<GvlAst> Load(
            IReadOnlyList<string> pouDirectories, out List<SkippedFile> skipped)
        {
            skipped = new List<SkippedFile>();
            var gvlsWithFiles = new List<(string FilePath, GvlAst Gvl)>();

            foreach (var file in MultiDirectoryPouLoader.FindGvlFiles(pouDirectories))
            {
                if (!StructuralParseGuard.TryParseOrSkip(
                        file, () => TcGvlParser.Parse(File.ReadAllText(file)), out var gvl, out var skip))
                {
                    skipped.Add(skip);
                    continue;
                }

                gvlsWithFiles.Add((file, gvl));
            }

            // Checked after the whole merged set is read, not per file: a
            // duplicate is only visible once every directory has contributed.
            // Fatal rather than skipped - an ambiguous GVL name must stop
            // registry construction before any suite runs.
            DuplicateNameDetector.ThrowIfDuplicate(
                gvlsWithFiles,
                x => x.Gvl.Name,
                x => x.FilePath,
                (name, filePaths) => new DuplicateGvlNameException(name, filePaths));

            return gvlsWithFiles.Select(x => x.Gvl).ToList();
        }
    }
}
