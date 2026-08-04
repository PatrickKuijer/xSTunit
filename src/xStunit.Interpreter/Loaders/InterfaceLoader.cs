using System.Collections.Generic;
using System.IO;
using System.Linq;
using xStunit.Parser;

namespace xStunit.Interpreter
{
    // Loads .TcIO interface declarations across the merged set of POU
    // directories. Same resilience as GvlLoader: a structurally unexpected
    // .TcIO file is skipped and reported rather than aborting registry build
    // for the whole directory.
    public static class InterfaceLoader
    {
        public static IReadOnlyList<InterfaceAst> Load(
            IReadOnlyList<string> pouDirectories, out List<SkippedFile> skipped)
        {
            skipped = new List<SkippedFile>();
            var interfacesWithFiles = new List<(string FilePath, InterfaceAst Interface)>();

            foreach (var file in MultiDirectoryPouLoader.FindInterfaceFiles(pouDirectories))
            {
                if (!StructuralParseGuard.TryParseOrSkip(
                        file, () => TcItfParser.Parse(File.ReadAllText(file)), out var itf, out var skip))
                {
                    skipped.Add(skip);
                    continue;
                }

                interfacesWithFiles.Add((file, itf));
            }

            // Checked after the whole merged set is read, not per file: a
            // duplicate is only visible once every directory has contributed.
            DuplicateNameDetector.ThrowIfDuplicate(
                interfacesWithFiles,
                x => x.Interface.Name,
                x => x.FilePath,
                (name, filePaths) => new DuplicateInterfaceTypeException(name, filePaths));

            return interfacesWithFiles.Select(x => x.Interface).ToList();
        }
    }
}
