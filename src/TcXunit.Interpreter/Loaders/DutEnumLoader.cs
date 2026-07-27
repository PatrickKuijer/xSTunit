using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using TcXunit.Parser;

namespace TcXunit.Interpreter
{
    // Parses ENUM .TcDUT definitions - e.g. "TYPE E_Color : (Red, Green,
    // Blue); END_TYPE" or "TYPE eModuleParameterDataTypes : (A, B) DINT;
    // END_TYPE" - across the merged set of POU directories, mirroring
    // DutAliasLoader's shape (TcXunit-fyu): registered into the same
    // name -> underlying-type-text map TypeRegistry's alias mechanism
    // already uses, so SIZEOF()/default-value/struct-boundary code paths
    // that already call ResolveAlias resolve an enum type name to its
    // underlying integer type (INT, IEC 61131-3's default enum base type,
    // unless the DUT declares an explicit base type after the closing
    // paren) without any of those call sites needing to know enums exist.
    //
    // No model for enum member initializers containing parenthesised
    // expressions (e.g. "Red := SomeFunc(1)") - not seen in any fixture yet
    // (grow-on-demand, same rationale as DutStructLoader's STRUCT EXTENDS
    // gap).
    public static class DutEnumLoader
    {
        private const string DefaultUnderlyingType = "INT";

        private static readonly Regex EnumPattern = new Regex(
            @"^TYPE\s+(?<name>\w+)\s*:\s*\((?<body>[^)]*)\)\s*(?<base>[A-Za-z_]\w*)?\s*;",
            RegexOptions.Compiled);

        // Matches a single leading "{attribute '...'}"-style pragma line
        // (e.g. "{attribute 'qualified_only'}") or a leading "// ..." line
        // comment, which TwinCAT emits before the "TYPE Name :" header for
        // DUTs with pragma attributes and/or a declaration comment. Neither
        // is part of the enum's declaration shape, so they're stripped
        // before EnumPattern is tried (mirrors real .TcDUT declaration text
        // - see PLC repo's eModuleParameterDataTypes.TcDUT/
        // eRemoteRegistrationOpcode.TcDUT).
        private static readonly Regex LeadingPragmaOrCommentLine = new Regex(
            @"\A\s*(\{[^\n\}]*\}|//[^\n]*)\s*", RegexOptions.Compiled);

        // Extracts (name, underlyingTypeName, members) from an ENUM DUT's
        // declaration text - e.g. "E_Color" / "INT" (the IEC 61131-3
        // default) from "TYPE E_Color : (Red, Green, Blue); END_TYPE", or
        // "eModuleParameterDataTypes" / "DINT" when the DUT declares an
        // explicit base type after the member list's closing paren. members
        // is the member-name -> ordinal-value table (TcXunit-rk3), parsed
        // from the same body capture group used above - never a second scan
        // of the DUT file.
        public static bool TryParseEnum(
            string declarationText, out string name, out string underlyingTypeName, out IReadOnlyDictionary<string, int> members)
        {
            name = null;
            underlyingTypeName = null;
            members = null;

            var text = declarationText.Replace("\r\n", "\n").Trim();
            for (var lead = LeadingPragmaOrCommentLine.Match(text); lead.Success; lead = LeadingPragmaOrCommentLine.Match(text))
                text = text.Substring(lead.Length);

            var match = EnumPattern.Match(text);
            if (!match.Success)
                return false;

            name = match.Groups["name"].Value;
            var baseGroup = match.Groups["base"];
            underlyingTypeName = baseGroup.Success && baseGroup.Value.Length > 0
                ? baseGroup.Value
                : DefaultUnderlyingType;
            members = ParseMembers(match.Groups["body"].Value);
            return true;
        }

        // Splits the member-list body (e.g. "Red,\n\tGreen,\n\tBlue" or
        // "Ok := 0,\n\tError := 1") into member -> ordinal-value pairs,
        // following standard IEC 61131-3 enum numbering: an explicit
        // ":=" initializer is used verbatim, an unspecified member is one
        // greater than the previous member's value, and the first
        // unspecified member (no preceding member at all) defaults to 0.
        private static IReadOnlyDictionary<string, int> ParseMembers(string body)
        {
            var members = new Dictionary<string, int>();
            var nextValue = 0;

            foreach (var rawEntry in body.Split(','))
            {
                var entry = rawEntry.Trim();
                if (entry.Length == 0)
                    continue;

                var assignIndex = entry.IndexOf(":=", StringComparison.Ordinal);
                string memberName;
                int value;
                if (assignIndex >= 0)
                {
                    memberName = entry.Substring(0, assignIndex).Trim();
                    value = int.Parse(entry.Substring(assignIndex + 2).Trim(), CultureInfo.InvariantCulture);
                }
                else
                {
                    memberName = entry;
                    value = nextValue;
                }

                members[memberName] = value;
                nextValue = value + 1;
            }

            return members;
        }

        public static IReadOnlyDictionary<string, string> Load(
            IReadOnlyList<string> pouDirectories,
            out List<SkippedFile> skipped,
            out IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> memberTables)
        {
            skipped = new List<SkippedFile>();
            var enums = new Dictionary<string, string>();
            var members = new Dictionary<string, IReadOnlyDictionary<string, int>>();

            foreach (var file in MultiDirectoryPouLoader.FindDutFiles(pouDirectories))
            {
                if (!StructuralParseGuard.TryParseOrSkip(
                        file, () => TcDutParser.Parse(File.ReadAllText(file)), out var dut, out var skip))
                {
                    skipped.Add(skip);
                    continue;
                }

                if (TryParseEnum(dut.DeclarationText, out var name, out var underlyingTypeName, out var enumMembers))
                {
                    enums[name] = underlyingTypeName;
                    members[name] = enumMembers;
                }
            }

            memberTables = members;
            return enums;
        }
    }
}
