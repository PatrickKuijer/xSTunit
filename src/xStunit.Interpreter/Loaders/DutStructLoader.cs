using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using xStunit.Parser;

namespace xStunit.Interpreter
{
    // Loads .TcDUT STRUCT types across the merged set of POU directories.
    //
    // ENUM/alias/union DUTs are skipped, since StructDeclParser has no model
    // for them (DutEnumLoader and DutAliasLoader pick those up on their own
    // pass over the same files). STRUCT inheritance - "TYPE X EXTENDS Base:"
    // - is skipped too: there is no model for merging in the base type's
    // fields, and StructDeclParser yields no name for that header shape, so
    // Load drops it. Neither case fails registry build, so an unsupported DUT
    // costs only itself rather than every suite in the directory.
    public static class DutStructLoader
    {
        // Line-anchored on purpose: only the "TYPE Name :" header line and the
        // next non-blank line are checked for the STRUCT keyword. A plain
        // Contains("STRUCT") over the whole declaration text would also match
        // an ENUM or alias DUT that merely mentions it in a comment ("(*
        // replaces the old STRUCT-based version *)") or inside an identifier
        // like "STRUCTURED".
        private static readonly Regex TypeHeaderPattern = new Regex(
            @"^TYPE\s+\w+(\s+EXTENDS\s+\w+)?\s*:", RegexOptions.Compiled);
        private static readonly Regex StructOnHeaderLinePattern = new Regex(
            @":\s*STRUCT\b", RegexOptions.Compiled);
        private static readonly Regex StructOnlyLinePattern = new Regex(
            @"^STRUCT\b", RegexOptions.Compiled);

        // True when the TYPE header actually declares a STRUCT ("TYPE Name :
        // STRUCT", or "TYPE Name :" with "STRUCT" on the following line), as
        // opposed to an ENUM/alias/union DUT whose text merely contains the
        // word "STRUCT" somewhere unrelated.
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

        public static IReadOnlyList<StructAst> Load(
            IReadOnlyList<string> pouDirectories, out List<SkippedFile> skipped)
        {
            skipped = new List<SkippedFile>();
            var structTypesWithFiles = new List<(string FilePath, StructAst Struct)>();

            foreach (var file in MultiDirectoryPouLoader.FindDutFiles(pouDirectories))
            {
                // A structurally unexpected .TcDUT file (malformed XML,
                // missing DUT/Declaration element) must not abort registry
                // build for the whole directory.
                if (!StructuralParseGuard.TryParseOrSkip(
                        file, () => TcDutParser.Parse(File.ReadAllText(file)), out var dut, out var dutSkip))
                {
                    skipped.Add(dutSkip);
                    continue;
                }

                if (!IsStructDeclaration(dut.DeclarationText))
                    continue;

                if (!StructuralParseGuard.TryParseOrSkip(
                        file, () => StructDeclParser.Parse(dut.DeclarationText), out var structAst, out var structSkip))
                {
                    skipped.Add(structSkip);
                    continue;
                }

                if (structAst.Name == null)
                    continue;

                structTypesWithFiles.Add((file, structAst));
            }

            // Checked after the whole merged set is read, not per file: a
            // duplicate is only visible once every directory has contributed.
            // Fatal rather than skipped, because TypeRegistry would otherwise
            // silently let whichever .TcDUT loaded last win.
            DuplicateNameDetector.ThrowIfDuplicate(
                structTypesWithFiles,
                x => x.Struct.Name,
                x => x.FilePath,
                (name, filePaths) => new DuplicateStructTypeException(name, filePaths));

            return structTypesWithFiles.Select(x => x.Struct).ToList();
        }
    }
}
