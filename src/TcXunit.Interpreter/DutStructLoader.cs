using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using TcXunit.Parser;

namespace TcXunit.Interpreter
{
    // Shared by CliRunner and SuiteCaseRunner.BuildRegistry (TcXunit-9li): loads
    // .TcDUT STRUCT types across the merged set of POU directories so both the
    // CLI entry point (`tcxunit run`) and the Test Explorer discovery path
    // (SuiteCaseRunner) resolve STRUCT-typed DUTs identically, and future
    // changes to DUT-loading can't silently apply to only one of them again.
    //
    // ENUM/alias/union DUTs are skipped since StructDeclParser has no model
    // for them yet, as are STRUCT DUTs using "TYPE X EXTENDS Base:" (struct
    // inheritance - own fields only, no model for merging in the base type's
    // fields yet); both fall back to their prior (unsupported) behavior
    // rather than failing registry build for every suite.
    public static class DutStructLoader
    {
        // A .TcDUT file that couldn't be parsed at all, or whose declaration
        // isn't a STRUCT this parser understands. Mirrors SuiteCaseRunner's
        // SkippedPou shape so callers that already track skipped POUs can
        // fold these in directly.
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

        public static IReadOnlyList<StructAst> Load(
            IReadOnlyList<string> pouDirectories, out List<SkippedFile> skipped)
        {
            skipped = new List<SkippedFile>();
            var structTypesWithFiles = new List<(string FilePath, StructAst Struct)>();

            foreach (var file in MultiDirectoryPouLoader.FindDutFiles(pouDirectories))
            {
                DutAst dut;
                try
                {
                    dut = TcDutParser.Parse(File.ReadAllText(file));
                }
                catch (Exception ex) when (ex is XmlException || ex is NullReferenceException)
                {
                    // A structurally unexpected .TcDUT file (malformed XML,
                    // missing DUT/Declaration element) must not abort
                    // registry build for the whole directory (TcXunit-022).
                    skipped.Add(new SkippedFile(
                        file, $"Failed to parse '{Path.GetFileName(file)}': {ex.Message}"));
                    continue;
                }

                if (!dut.DeclarationText.Contains("STRUCT"))
                    continue;

                StructAst structAst;
                try
                {
                    structAst = StructDeclParser.Parse(dut.DeclarationText);
                }
                catch (Exception ex) when (ex is XmlException || ex is NullReferenceException)
                {
                    skipped.Add(new SkippedFile(
                        file, $"Failed to parse '{Path.GetFileName(file)}': {ex.Message}"));
                    continue;
                }

                if (structAst.Name == null)
                    continue;

                structTypesWithFiles.Add((file, structAst));
            }

            // Fail fast and loud on duplicate STRUCT type names across the
            // merged set (TcXunit-dvd): two .TcDUT files declaring the same
            // STRUCT name must not silently let the later-loaded one win in
            // TypeRegistry.
            var duplicateStruct = structTypesWithFiles
                .GroupBy(x => x.Struct.Name)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicateStruct != null)
                throw new DuplicateStructTypeException(
                    duplicateStruct.Key, duplicateStruct.Select(x => x.FilePath).ToList());

            return structTypesWithFiles.Select(x => x.Struct).ToList();
        }
    }
}
