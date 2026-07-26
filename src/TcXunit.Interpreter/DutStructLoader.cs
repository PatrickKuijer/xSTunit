using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        // Same line-anchored style as StructDeclParser.TypeNamePattern: finds
        // the "TYPE Name :" header line, then checks only that line and the
        // next non-blank line for the STRUCT keyword - not a raw Contains()
        // over the whole declaration text. A plain Contains("STRUCT") would
        // also match ENUM/alias DUTs whose text happens to contain that
        // substring in a comment (e.g. "(* replaces the old STRUCT-based
        // version *)") or in an identifier like "STRUCTURED" (TcXunit-bpk).
        private static readonly Regex TypeHeaderPattern = new Regex(
            @"^TYPE\s+\w+(\s+EXTENDS\s+\w+)?\s*:", RegexOptions.Compiled);
        private static readonly Regex StructOnHeaderLinePattern = new Regex(
            @":\s*STRUCT\b", RegexOptions.Compiled);
        private static readonly Regex StructOnlyLinePattern = new Regex(
            @"^STRUCT\b", RegexOptions.Compiled);

        // True when the declaration text's TYPE header actually declares a
        // STRUCT ("TYPE Name : STRUCT" or "TYPE Name :" / "STRUCT" on the
        // following line), as opposed to an ENUM/alias/union DUT whose text
        // merely contains the word "STRUCT" somewhere unrelated.
        public static bool IsStructDeclaration(string declarationText)
        {
            var lines = declarationText.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (!TypeHeaderPattern.IsMatch(trimmed))
                    continue;

                if (StructOnHeaderLinePattern.IsMatch(trimmed))
                    return true;

                for (var j = i + 1; j < lines.Length; j++)
                {
                    var next = lines[j].Trim();
                    if (next.Length == 0)
                        continue;
                    return StructOnlyLinePattern.IsMatch(next);
                }

                return false;
            }

            return false;
        }

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

                if (!IsStructDeclaration(dut.DeclarationText))
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
            DuplicateNameDetector.ThrowIfDuplicate(
                structTypesWithFiles,
                x => x.Struct.Name,
                x => x.FilePath,
                (name, filePaths) => new DuplicateStructTypeException(name, filePaths));

            return structTypesWithFiles.Select(x => x.Struct).ToList();
        }
    }
}
