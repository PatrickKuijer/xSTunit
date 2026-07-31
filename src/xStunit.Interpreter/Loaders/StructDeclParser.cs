using System.Text.RegularExpressions;

namespace xStunit.Interpreter
{
    // Parses "TYPE Name : STRUCT ... END_STRUCT END_TYPE" declaration text
    // into a StructAst. The TYPE/END_TYPE header/footer lines are inert to
    // VarBlockParser (same as a FUNCTION_BLOCK header line) since only
    // STRUCT/END_STRUCT toggle its section state - so field parsing is
    // reused as-is (TcXunit-w5x.15.6).
    public static class StructDeclParser
    {
        private static readonly Regex TypeNamePattern = new Regex(
            @"^TYPE\s+(?<name>\w+)\s*:", RegexOptions.Compiled);

        // A leading "{attribute 'pack_mode' := 'N'}" pragma line (TcXunit-eub)
        // - TwinCAT emits this before the "TYPE Name :" header, alongside any
        // other attribute pragmas or a declaration comment (same leading-line
        // shape DutEnumLoader's LeadingPragmaOrCommentLine already skips for
        // ENUM DUTs). Only scanned up to the TYPE header line below, since
        // that's the only place TwinCAT puts it.
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
