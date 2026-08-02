using System.Text.RegularExpressions;

namespace xStunit.Interpreter
{
    // Parses "TYPE Name : STRUCT ... END_STRUCT END_TYPE" declaration text -
    // or the UNION spelling of the same shape - into a StructAst. Field
    // parsing is VarBlockParser's, unmodified: only STRUCT/UNION and their
    // END_ keywords toggle its section state, so the TYPE/END_TYPE lines pass
    // through it inert, exactly as a FUNCTION_BLOCK header line does.
    public static class StructDeclParser
    {
        public const string StructBody = "STRUCT";
        public const string UnionBody = "UNION";

        private static readonly Regex TypeNamePattern = new Regex(
            @"^TYPE\s+(?<name>\w+)\s*:", RegexOptions.Compiled);

        // TwinCAT emits "{attribute 'pack_mode' := 'N'}" before the
        // "TYPE Name :" header, so the scan below stops at that header - past
        // it there is nowhere the pragma can legally appear.
        private static readonly Regex PackModeAttributePattern = new Regex(
            @"^\{attribute\s+'pack_mode'\s*:=\s*'(?<value>\d+)'\}$", RegexOptions.Compiled);

        private static readonly Regex TypeHeaderPattern = new Regex(
            @"^TYPE\s+\w+(\s+EXTENDS\s+\w+)?\s*:", RegexOptions.Compiled);
        private static readonly Regex BodyOnHeaderLinePattern = new Regex(
            @":\s*(?<body>STRUCT|UNION)\b", RegexOptions.Compiled);
        private static readonly Regex BodyOnItsOwnLinePattern = new Regex(
            @"^(?<body>STRUCT|UNION)\b", RegexOptions.Compiled);

        // Which body the TYPE header opens - StructBody, UnionBody, or null for
        // an ENUM or alias DUT.
        //
        // Line-anchored on purpose: only the "TYPE Name :" header line and the
        // next non-blank line are examined. A Contains("STRUCT") over the whole
        // declaration text would also match an ENUM or alias DUT that merely
        // mentions it in a comment ("(* replaces the old STRUCT-based version
        // *)") or inside an identifier like "STRUCTURED".
        public static string DeclaredBody(string declarationText)
        {
            var lines = declarationText.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (!TypeHeaderPattern.IsMatch(trimmed))
                    continue;

                var onHeaderLine = BodyOnHeaderLinePattern.Match(trimmed);
                if (onHeaderLine.Success)
                    return onHeaderLine.Groups["body"].Value;

                for (var j = i + 1; j < lines.Length; j++)
                {
                    var next = lines[j].Trim();
                    if (next.Length == 0)
                        continue;

                    var onItsOwnLine = BodyOnItsOwnLinePattern.Match(next);
                    return onItsOwnLine.Success ? onItsOwnLine.Groups["body"].Value : null;
                }

                return null;
            }

            return null;
        }

        public static StructAst Parse(string declarationText)
        {
            string name = null;
            var packMode = 0;
            foreach (var rawLine in declarationText.Split('\n'))
            {
                var line = rawLine.Trim();

                var packModeMatch = PackModeAttributePattern.Match(line);
                if (packModeMatch.Success)
                {
                    packMode = int.Parse(packModeMatch.Groups["value"].Value);
                    continue;
                }

                var match = TypeNamePattern.Match(line);
                if (match.Success)
                {
                    name = match.Groups["name"].Value;
                    break;
                }
            }

            var fields = VarBlockParser.Parse(declarationText);
            return new StructAst(name, fields, packMode, DeclaredBody(declarationText) == UnionBody);
        }
    }
}
