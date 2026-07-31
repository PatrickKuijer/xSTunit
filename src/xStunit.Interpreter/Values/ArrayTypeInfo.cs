using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace xStunit.Interpreter
{
    // Parses declared ARRAY type text - "ARRAY[lo..hi] OF type" and the
    // multi-dim form "ARRAY[lo..hi,lo..hi,...] OF type" - into dimension
    // bounds plus element type name (TcXunit-w5x.15.6).
    internal static class ArrayTypeInfo
    {
        // TcXunit-fzm: IEC 61131-3 type names are case-insensitive ('array[..]
        // of int' is exactly as valid as 'ARRAY[..] OF INT'), so both the
        // ARRAY/OF keyword pattern and the leading-keyword check below match
        // case-insensitively.
        private static readonly Regex Pattern = new Regex(
            @"^ARRAY\s*\[(?<dims>[^\]]+)\]\s*OF\s+(?<elementType>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool IsArrayType(string typeName) =>
            typeName != null && typeName.TrimStart().StartsWith("ARRAY", StringComparison.OrdinalIgnoreCase);

        // resolveBound resolves a non-literal bound expression's text (e.g.
        // "cRemoteClientConfig.MAX_REMOTE_ITEMS") to its integer value.
        // IEC 61131-3 array bounds are constant expressions, not just bare
        // integer literals (TcXunit-654) - callers with an Engine/Frame
        // context to evaluate such expressions against (e.g. GVL-qualified
        // constants) pass a resolver; callers without one (e.g.
        // StructBoundaryBuilder, which runs before any Engine exists) pass
        // null and keep the original literal-only behavior.
        public static (IReadOnlyList<(int Lo, int Hi)> Dimensions, string ElementTypeName) Parse(
            string typeName, Func<string, int> resolveBound = null)
        {
            var match = Pattern.Match(typeName.Trim());
            if (!match.Success)
                throw new FormatException($"Not a valid ARRAY type declaration: '{typeName}'");

            var dims = match.Groups["dims"].Value
                .Split(',')
                .Select(dimText => ParseDim(dimText, resolveBound))
                .ToList();

            return (dims, match.Groups["elementType"].Value.Trim());
        }

        private static (int Lo, int Hi) ParseDim(string dimText, Func<string, int> resolveBound)
        {
            var parts = dimText.Split(new[] { ".." }, StringSplitOptions.None);
            return (ResolveBound(parts[0].Trim(), resolveBound), ResolveBound(parts[1].Trim(), resolveBound));
        }

        private static int ResolveBound(string boundText, Func<string, int> resolveBound)
        {
            if (int.TryParse(boundText, out var literal))
                return literal;

            if (resolveBound != null)
                return resolveBound(boundText);

            throw new FormatException($"The input string '{boundText}' was not in a correct format");
        }
    }
}
