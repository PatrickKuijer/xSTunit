using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using TcXunit.Parser;

namespace TcXunit.Interpreter
{
    // Shared by CliRunner and SuiteCaseRunner.BuildRegistry (TcXunit-71o):
    // loads .TcGVL files across the merged set of POU directories so global
    // variable lists (e.g. gFrameworkTemp) resolve identically for both the
    // CLI entry point and Test Explorer discovery. Mirrors DutStructLoader's
    // resilient per-file skip/report shape - a structurally unexpected
    // .TcGVL file must not abort registry build for the whole directory.
    public static class GvlLoader
    {
        public readonly struct SkippedFile
        {
            public SkippedFile(string filePath, string message)
            {
                FilePath = filePath;
                Message = message;
            }

            public string FilePath { get; }
            public string Message { get; }
        }

        public static IReadOnlyList<GvlAst> Load(
            IReadOnlyList<string> pouDirectories, out List<SkippedFile> skipped)
        {
            skipped = new List<SkippedFile>();
            var gvlsWithFiles = new List<(string FilePath, GvlAst Gvl)>();

            foreach (var file in MultiDirectoryPouLoader.FindGvlFiles(pouDirectories))
            {
                GvlAst gvl;
                try
                {
                    gvl = TcGvlParser.Parse(File.ReadAllText(file));
                }
                catch (Exception ex) when (ex is XmlException || ex is NullReferenceException)
                {
                    skipped.Add(new SkippedFile(
                        file, $"Failed to parse '{Path.GetFileName(file)}': {ex.Message}"));
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
