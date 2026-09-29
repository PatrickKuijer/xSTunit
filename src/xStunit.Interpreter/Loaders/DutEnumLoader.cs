using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using xStunit.Parser;

namespace xStunit.Interpreter
{
    // Parses ENUM .TcDUT definitions - e.g. "TYPE E_Color : (Red, Green,
    // Blue); END_TYPE" or "TYPE eWidgetValueKind : (A, B) DINT; END_TYPE" -
    // across the merged set of POU directories.
    //
    // Returns the same name -> underlying-type-text shape DutAliasLoader
    // does, so a caller merges enums into TypeRegistry's alias map and every
    // existing ResolveAlias call site (SIZEOF, default values, struct
    // boundaries) resolves an enum name to its underlying integer type
    // without knowing enums exist. That underlying type is INT - IEC
    // 61131-3's default enum base - unless the DUT declares one explicitly
    // after the member list's closing paren.
    //
    // No model for enum member initializers containing parenthesised
    // expressions (e.g. "Red := SomeFunc(1)") - grow-on-demand, same as
    // DutStructLoader's STRUCT EXTENDS gap.
    public static class DutEnumLoader
    {
        private const string DefaultUnderlyingType = "INT";

        private static readonly Regex EnumPattern = new Regex(
            @"^TYPE\s+(?<name>\w+)\s*:\s*\((?<body>[^)]*)\)\s*(?<base>[A-Za-z_]\w*)?\s*;",
            RegexOptions.Compiled);

        // Extracts (name, underlyingTypeName, members) from an ENUM DUT's
        // declaration text - e.g. "E_Color" / "INT" from "TYPE E_Color :
        // (Red, Green, Blue); END_TYPE", or "eWidgetValueKind" / "DINT" when
        // the DUT declares an explicit base type. members is the member-name
        // -> ordinal-value table.
        public static bool TryParseEnum(
            string declarationText, out string name, out string underlyingTypeName, out IReadOnlyDictionary<string, int> members)
        {
            name = null;
            underlyingTypeName = null;
            members = null;

            // EnumPattern is anchored at the start of the text, so the leading
            // pragmas and comments have to go first.
            var match = EnumPattern.Match(DutDeclarationPreamble.Strip(declarationText));
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

        // A "// ..." comment trailing a member entry, stripped before the body
        // is split on commas: an in-comment comma would otherwise read as a
        // member separator, and an in-comment digit run would reach the
        // initializer parser.
        private static readonly Regex TrailingLineComment = new Regex(@"//[^\n]*", RegexOptions.Compiled);

        // Splits the member-list body into member -> ordinal-value pairs,
        // following IEC 61131-3 enum numbering: an explicit ":=" initializer
        // is used verbatim, an unspecified member is one greater than the
        // previous member's value, and a first member with no initializer
        // is 0. Member names match in any case, as IEC identifiers do.
        private static IReadOnlyDictionary<string, int> ParseMembers(string body)
        {
            var members = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var nextValue = 0;

            foreach (var rawEntry in TrailingLineComment.Replace(body, "").Split(','))
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
                    value = ParseInitializerValue(entry.Substring(assignIndex + 2).Trim());
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

        // Routed through the same Lexer/Parser every other integer literal in
        // an ST body goes through, rather than a bare int.Parse, because an
        // ENUM member initializer is not guaranteed to be decimal - a based
        // literal ("16#8", "2#1010") is legal IEC 61131-3 and would otherwise
        // throw FormatException. A leading unary minus is accepted; any other
        // constant expression is out of scope.
        private static int ParseInitializerValue(string valueText)
        {
            switch (Parser.ParseExpression(valueText))
            {
                case IntLiteralExpr literal:
                    return literal.Value;
                case UnaryExpr { Op: "-", Operand: IntLiteralExpr literal }:
                    return -literal.Value;
                default:
                    throw new FormatException($"Unsupported ENUM member initializer '{valueText}' - expected an integer literal");
            }
        }

        public static IReadOnlyDictionary<string, string> Load(
            IReadOnlyList<string> pouDirectories,
            out List<SkippedFile> skipped,
            out IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> memberTables)
        {
            skipped = new List<SkippedFile>();
            var enums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var members = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

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
