using System.Text.RegularExpressions;

namespace TcXunit.Interpreter
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

        public static StructAst Parse(string declarationText)
        {
            string name = null;
            foreach (var rawLine in declarationText.Split('\n'))
            {
                var match = TypeNamePattern.Match(rawLine.Trim());
                if (match.Success)
                {
                    name = match.Groups["name"].Value;
                    break;
                }
            }

            var fields = VarBlockParser.Parse(declarationText);
            return new StructAst(name, fields);
        }
    }
}
