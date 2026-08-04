using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using xStunit.Parser;

namespace xStunit.Interpreter
{
    // Loads .TcDUT STRUCT and UNION types across the merged set of POU
    // directories.
    //
    // ENUM/alias DUTs are skipped, since StructDeclParser has no model for
    // them (DutEnumLoader and DutAliasLoader pick those up on their own pass
    // over the same files). STRUCT inheritance - "TYPE X EXTENDS Base:" - is
    // skipped too: there is no model for merging in the base type's fields,
    // and StructDeclParser yields no name for that header shape, so Load drops
    // it. Neither case fails registry build, so an unsupported DUT costs only
    // itself rather than every suite in the directory.
    public static class DutStructLoader
    {
        // True for a STRUCT declaration alone, not for the UNION that Load also
        // accepts: DutAliasLoader consults this to rule a DUT out as an alias,
        // and the two bodies part company as soon as they are laid out.
        public static bool IsStructDeclaration(string declarationText) =>
            StructDeclParser.DeclaredBody(declarationText) == StructDeclParser.StructBody;

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

                if (StructDeclParser.DeclaredBody(dut.DeclarationText) == null)
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
