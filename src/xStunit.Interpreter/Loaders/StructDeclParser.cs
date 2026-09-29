using System.Text.RegularExpressions;

namespace xStunit.Interpreter
{
    // Parses "TYPE Name : STRUCT ... END_STRUCT END_TYPE" declaration text -
    // or the UNION spelling of the same shape - into a StructAst. Field
    // parsing is VarBlockParser's, unmodified.
    public static class StructDeclParser
    {
        public const string StructBody = "STRUCT";
        public const string UnionBody = "UNION";

        private static readonly Regex TypeNamePattern = new Regex(
            @"^TYPE\s+(?<name>\w+)\s*:", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // TwinCAT emits "{attribute 'pack_mode' := 'N'}" before the
        // "TYPE Name :" header, so it is read out of the preamble - past that
        // header there is nowhere the pragma can legally appear.
        private static readonly Regex PackModeAttributePattern = new Regex(
            @"^\{attribute\s+'pack_mode'\s*:=\s*'(?<value>\d+)'\}$", RegexOptions.Compiled);

        private static readonly Regex TypeHeaderPattern = new Regex(
            @"^TYPE\s+\w+(\s+EXTENDS\s+\w+)?\s*:", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex BodyOnHeaderLinePattern = new Regex(
            @":\s*(?<body>STRUCT|UNION)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex BodyOnItsOwnLinePattern = new Regex(
            @"^(?<body>STRUCT|UNION)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Which body the TYPE header opens - StructBody, UnionBody, or null for
        // an ENUM or alias DUT. Returned in the constants' spelling however the
        // source wrote the keyword, so callers can compare with ==.
        //
        // Line-anchored on purpose: only the "TYPE Name :" header line and the
        // next non-blank line are examined. A Contains("STRUCT") over the whole
        // declaration text would also match an ENUM or alias DUT that merely
        // mentions it in a comment ("(* replaces the old STRUCT-based version
        // *)") or inside an identifier like "STRUCTURED".
        public static string DeclaredBody(string declarationText)
        {
            var lines = DutDeclarationPreamble.Strip(declarationText).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (!TypeHeaderPattern.IsMatch(trimmed))
                    continue;

                var onHeaderLine = BodyOnHeaderLinePattern.Match(trimmed);
                if (onHeaderLine.Success)
                    return onHeaderLine.Groups["body"].Value.ToUpperInvariant();

                for (var j = i + 1; j < lines.Length; j++)
                {
                    var next = lines[j].Trim();
                    if (next.Length == 0)
                        continue;

                    var onItsOwnLine = BodyOnItsOwnLinePattern.Match(next);
                    return onItsOwnLine.Success ? onItsOwnLine.Groups["body"].Value.ToUpperInvariant() : null;
                }

                return null;
            }

            return null;
        }

        public static StructAst Parse(string declarationText)
        {
            var fields = VarBlockParser.Parse(declarationText);
            return new StructAst(
                DeclaredName(declarationText),
                fields,
                PackMode(declarationText),
                DeclaredBody(declarationText) == UnionBody);
        }

        // Taken from the header rather than from the first "TYPE Name :" text
        // anywhere in the declaration, so prose in the header comment that
        // quotes a TYPE line cannot name the type.
        private static string DeclaredName(string declarationText)
        {
            foreach (var rawLine in DutDeclarationPreamble.Strip(declarationText).Split('\n'))
            {
                var match = TypeNamePattern.Match(rawLine.Trim());
                if (match.Success)
                    return match.Groups["name"].Value;
            }

            return null;
        }

        private static int PackMode(string declarationText)
        {
            foreach (var rawLine in DutDeclarationPreamble.LeadingText(declarationText).Split('\n'))
            {
                var match = PackModeAttributePattern.Match(rawLine.Trim());
                if (match.Success)
                    return int.Parse(match.Groups["value"].Value);
            }

            return 0;
        }
    }
}
