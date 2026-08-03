using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using xStunit.Parser;

namespace xStunit.Interpreter
{
    // Parses ALIAS .TcDUT definitions - e.g. "TYPE T_MaxString :
    // STRING(255); END_TYPE" - across the merged set of POU directories.
    // Same resilience as DutStructLoader/GvlLoader: a structurally
    // unexpected .TcDUT file is skipped and reported, never fatal to
    // registry build. Rescans the same .TcDUT set independently of
    // DutStructLoader, so a call site can wire in one without the other.
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

        // Extracts (name, underlyingTypeName) from an ALIAS DUT's declaration
        // text - e.g. "T_MaxString" / "STRING(255)" from "TYPE T_MaxString :
        // STRING(255); END_TYPE". Returns false for STRUCT/ENUM declarations
        // and for anything else that isn't a single underlying-type reference
        // terminated by ';', so a caller can try this without first consulting
        // DutStructLoader.IsStructDeclaration.
        public static bool TryParseAlias(string declarationText, out string name, out string underlyingTypeName)
        {
            name = null;
            underlyingTypeName = null;

            var lines = DutDeclarationPreamble.Strip(declarationText).Split('\n');
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

        // Well-known Beckhoff system-library ALIAS types. These have no
        // project-authored .TcDUT - they exist only as compiled library
        // metadata (.tmc/.xti DataType entries), which nothing here parses, so
        // they could never be discovered from source. Grown on demand as more
        // library aliases are hit. Load seeds these FIRST so a project's own
        // .TcDUT of the same name (unlikely, but not impossible) overwrites
        // them rather than the other way round.
        private static readonly IReadOnlyDictionary<string, string> WellKnownLibraryAliases =
            new Dictionary<string, string>
            {
                // Tc2_System.T_MaxString: TwinCAT PLC string of max length
                // 255 bytes + 1 byte null delimiter.
                ["T_MaxString"] = "STRING(255)",
            };

        public static IReadOnlyDictionary<string, string> Load(
            IReadOnlyList<string> pouDirectories, out List<SkippedFile> skipped)
        {
            skipped = new List<SkippedFile>();
            var aliases = new Dictionary<string, string>();
            foreach (var wellKnown in WellKnownLibraryAliases)
                aliases[wellKnown.Key] = wellKnown.Value;

            foreach (var file in MultiDirectoryPouLoader.FindDutFiles(pouDirectories))
            {
                if (!StructuralParseGuard.TryParseOrSkip(
                        file, () => TcDutParser.Parse(File.ReadAllText(file)), out var dut, out var skip))
                {
                    skipped.Add(skip);
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
