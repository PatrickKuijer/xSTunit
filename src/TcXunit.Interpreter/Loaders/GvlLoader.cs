using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TcXunit.Parser;

namespace TcXunit.Interpreter
{
    // Used by CliRunner (TcXunit-71o) to load .TcGVL files across the merged
    // set of POU directories, resolving global variable lists (e.g.
    // gScratchGlobals). Mirrors DutStructLoader's resilient per-file
    // skip/report shape - a structurally unexpected .TcGVL file must not
    // abort registry build for the whole directory.
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

            // Fail fast and loud on duplicate GVL names across the merged
            // set, same rationale as DuplicateStructTypeException/
            // DuplicatePouTypeException: a duplicate name is ambiguous and
            // must stop registry construction before any suite runs.
            DuplicateNameDetector.ThrowIfDuplicate(
                gvlsWithFiles,
                x => x.Gvl.Name,
                x => x.FilePath,
                (name, filePaths) => new DuplicateGvlNameException(name, filePaths));

            return gvlsWithFiles.Select(x => x.Gvl).ToList();
        }
    }
}
