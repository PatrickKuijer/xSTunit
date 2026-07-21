using System.Text.RegularExpressions;

namespace TcXunit.Interpreter
{
    // Parses declared STRING type text - bare "STRING" (TwinCAT's default
    // 80-char length) or "STRING(n)" - for StructBoundaryBuilder's
    // empty/max-length boundary (TcXunit-w5x.15.10 / T7's design).
    internal static class StringTypeInfo
    {
        private const int DefaultLength = 80;

        private static readonly Regex SizedPattern = new Regex(
            @"^STRING\s*\(\s*(?<n>\d+)\s*\)$", RegexOptions.Compiled);

        public static bool IsStringType(string typeName) =>
            typeName != null && (typeName.Trim() == "STRING" || SizedPattern.IsMatch(typeName.Trim()));

        public static int ParseLength(string typeName)
        {
            var trimmed = typeName.Trim();
            if (trimmed == "STRING")
                return DefaultLength;

            var match = SizedPattern.Match(trimmed);
            return int.Parse(match.Groups["n"].Value);
        }
    }
}
