using System.Text.RegularExpressions;

namespace xStunit.Interpreter
{
    // Parses "TYPE Name : STRUCT ... END_STRUCT END_TYPE" declaration text
    // into a StructAst. Field parsing is VarBlockParser's, unmodified: only
    // STRUCT/END_STRUCT toggle its section state, so the TYPE/END_TYPE lines
    // pass through it inert, exactly as a FUNCTION_BLOCK header line does.
    public static class StructDeclParser
    {
        private static readonly Regex TypeNamePattern = new Regex(
            @"^TYPE\s+(?<name>\w+)\s*:", RegexOptions.Compiled);

        // TwinCAT emits "{attribute 'pack_mode' := 'N'}" before the
        // "TYPE Name :" header, so the scan below stops at that header - past
        // it there is nowhere the pragma can legally appear.
        private static readonly Regex PackModeAttributePattern = new Regex(
            @"^\{attribute\s+'pack_mode'\s*:=\s*'(?<value>\d+)'\}$", RegexOptions.Compiled);

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
            return new StructAst(name, fields, packMode);
        }
    }
}
