using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TcXunit.Interpreter
{
    // Parses declared ARRAY type text - "ARRAY[lo..hi] OF type" and the
    // multi-dim form "ARRAY[lo..hi,lo..hi,...] OF type" - into dimension
    // bounds plus element type name (TcXunit-w5x.15.6).
    internal static class ArrayTypeInfo
    {
        private static readonly Regex Pattern = new Regex(
            @"^ARRAY\s*\[(?<dims>[^\]]+)\]\s*OF\s+(?<elementType>.+)$", RegexOptions.Compiled);

        public static bool IsArrayType(string typeName) =>
            typeName != null && typeName.TrimStart().StartsWith("ARRAY", StringComparison.Ordinal);

        public static (IReadOnlyList<(int Lo, int Hi)> Dimensions, string ElementTypeName) Parse(string typeName)
        {
            var match = Pattern.Match(typeName.Trim());
            if (!match.Success)
                throw new FormatException($"Not a valid ARRAY type declaration: '{typeName}'");

            var dims = match.Groups["dims"].Value
                .Split(',')
                .Select(ParseDim)
                .ToList();

            return (dims, match.Groups["elementType"].Value.Trim());
        }

        private static (int Lo, int Hi) ParseDim(string dimText)
        {
            var parts = dimText.Split(new[] { ".." }, StringSplitOptions.None);
            return (int.Parse(parts[0].Trim()), int.Parse(parts[1].Trim()));
        }
    }
}
