using System.Collections.Generic;
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

        // Extracts (name, underlyingTypeName) from an ENUM DUT's
        // declaration text - e.g. "E_Color" / "INT" (the IEC 61131-3
        // default) from "TYPE E_Color : (Red, Green, Blue); END_TYPE", or
        // "eModuleParameterDataTypes" / "DINT" when the DUT declares an
        // explicit base type after the member list's closing paren.
        public static bool TryParseEnum(string declarationText, out string name, out string underlyingTypeName)
        {
            name = null;
            underlyingTypeName = null;

            var match = EnumPattern.Match(declarationText.Replace("\r\n", "\n").Trim());
            if (!match.Success)
                return false;

            name = match.Groups["name"].Value;
            var baseGroup = match.Groups["base"];
            underlyingTypeName = baseGroup.Success && baseGroup.Value.Length > 0
                ? baseGroup.Value
                : DefaultUnderlyingType;
            return true;
        }

        public static IReadOnlyDictionary<string, string> Load(
            IReadOnlyList<string> pouDirectories, out List<SkippedFile> skipped)
        {
            skipped = new List<SkippedFile>();
            var enums = new Dictionary<string, string>();

            foreach (var file in MultiDirectoryPouLoader.FindDutFiles(pouDirectories))
            {
                if (!StructuralParseGuard.TryParseOrSkip(
                        file, () => TcDutParser.Parse(File.ReadAllText(file)), out var dut, out var skip))
                {
                    skipped.Add(skip);
                    continue;
                }

                if (TryParseEnum(dut.DeclarationText, out var name, out var underlyingTypeName))
                    enums[name] = underlyingTypeName;
            }

            return enums;
        }
    }
}
