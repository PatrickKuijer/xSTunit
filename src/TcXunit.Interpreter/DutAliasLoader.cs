using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using TcXunit.Parser;

namespace TcXunit.Interpreter
{
    // Parses ALIAS .TcDUT definitions - e.g. "TYPE T_MaxString :
    // STRING(255); END_TYPE" - across the merged set of POU directories,
    // mirroring DutStructLoader/GvlLoader's shape (TcXunit-6hg): a
    // structurally unexpected .TcDUT file is skipped/reported, not fatal to
    // registry build, and the same file set is scanned independently of
    // DutStructLoader so this can be wired in (or left out) at each call
    // site without disturbing STRUCT loading.
    //
    // An ALIAS DUT's declaration text has no STRUCT/ENUM body - just a
    // "TYPE Name : <underlying type>;" header, optionally split across two
    // lines the same way DutStructLoader.IsStructDeclaration checks the
    // line after a bare "TYPE Name :" header for the STRUCT keyword.
    public static class DutAliasLoader
    {
        private static readonly Regex TypeHeaderPattern = new Regex(
            @"^TYPE\s+(?<name>\w+)\s*:\s*(?<rest>.*)$", RegexOptions.Compiled);

        // Text that starts a STRUCT or ENUM body, not an alias's
        // underlying-type text.
        private static bool LooksLikeStructOrEnumBody(string text) =>
            text.Length == 0
            || text.StartsWith("STRUCT", StringComparison.Ordinal)
            || text.StartsWith("(", StringComparison.Ordinal);

        // Extracts (name, underlyingTypeName) from an ALIAS DUT's
        // declaration text - e.g. "T_MaxString" / "STRING(255)" from
        // "TYPE T_MaxString : STRING(255); END_TYPE". Returns false for
        // STRUCT/ENUM declarations (recognised by their body shape) and for
        // anything else that doesn't look like a single underlying-type
        // reference terminated by ';', so this can be tried independently
        // of DutStructLoader.IsStructDeclaration.
        public static bool TryParseAlias(string declarationText, out string name, out string underlyingTypeName)
        {
            name = null;
            underlyingTypeName = null;

            var lines = declarationText.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                var match = TypeHeaderPattern.Match(trimmed);
                if (!match.Success)
                    continue;

                var headerName = match.Groups["name"].Value;
                var rest = match.Groups["rest"].Value.Trim();

                if (rest.Length == 0)
                {
                    // Underlying-type text is on the next non-blank line
                    // (same "TYPE Name :" header shape STRUCT/ENUM DUTs use).
                    for (var j = i + 1; j < lines.Length; j++)
                    {
                        var next = lines[j].Trim();
                        if (next.Length == 0)
                            continue;
                        rest = next;
                        break;
                    }
                }

                if (LooksLikeStructOrEnumBody(rest) || !rest.EndsWith(";", StringComparison.Ordinal))
                    return false;

                name = headerName;
                underlyingTypeName = rest.Substring(0, rest.Length - 1).Trim();
                return true;
            }

            return false;
        }

        // A .TcDUT file that couldn't be parsed at all. Mirrors
        // DutStructLoader.SkippedFile's shape so callers can fold these in
        // directly alongside other skipped-DUT reporting.
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

        public static IReadOnlyDictionary<string, string> Load(
            IReadOnlyList<string> pouDirectories, out List<SkippedFile> skipped)
        {
            skipped = new List<SkippedFile>();
            var aliases = new Dictionary<string, string>();

            foreach (var file in MultiDirectoryPouLoader.FindDutFiles(pouDirectories))
            {
                DutAst dut;
                try
                {
                    dut = TcDutParser.Parse(File.ReadAllText(file));
                }
                catch (Exception ex) when (ex is XmlException || ex is NullReferenceException)
                {
                    skipped.Add(new SkippedFile(
                        file, $"Failed to parse '{Path.GetFileName(file)}': {ex.Message}"));
                    continue;
                }

                if (DutStructLoader.IsStructDeclaration(dut.DeclarationText))
                    continue;

                if (TryParseAlias(dut.DeclarationText, out var name, out var underlyingTypeName))
                    aliases[name] = underlyingTypeName;
            }

            return aliases;
        }
    }
}
