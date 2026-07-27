using System.Text.RegularExpressions;

namespace TcXunit.Interpreter
{
    // Parses declared STRING/WSTRING type text - bare "STRING"/"WSTRING"
    // (TwinCAT's default 80-char length) or "STRING(n)"/"WSTRING(n)" - for
    // StructBoundaryBuilder's empty/max-length boundary (TcXunit-w5x.15.10 /
    // T7's design) and Engine.Defaults's "" default. WSTRING (TcXunit-gd2.4)
    // mirrors STRING here: character width isn't enforced (C# strings are
    // already UTF-16), so the only difference from STRING is the type-name
    // keyword itself.
    internal static class StringTypeInfo
    {
        private const int DefaultLength = 80;

        private static readonly Regex SizedPattern = new Regex(
            @"^(STRING|WSTRING)\s*\(\s*(?<n>\d+)\s*\)$", RegexOptions.Compiled);

        public static bool IsStringType(string typeName)
        {
            if (typeName == null)
                return false;

            var trimmed = typeName.Trim();
            return trimmed == "STRING" || trimmed == "WSTRING" || SizedPattern.IsMatch(trimmed);
        }

        public static int ParseLength(string typeName)
        {
            var trimmed = typeName.Trim();
            if (trimmed == "STRING" || trimmed == "WSTRING")
                return DefaultLength;

            var match = SizedPattern.Match(trimmed);
            return int.Parse(match.Groups["n"].Value);
        }
    }
}
