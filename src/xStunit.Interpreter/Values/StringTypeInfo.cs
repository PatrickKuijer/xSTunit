using System;
using System.Text.RegularExpressions;

namespace xStunit.Interpreter
{
    // Parses declared STRING/WSTRING type text - bare "STRING"/"WSTRING", or
    // the sized "STRING(n)"/"WSTRING(n)" - for StructBoundaryBuilder's
    // empty/max-length boundary and Engine.Defaults's "" default. An
    // unsized declaration is taken as length 80. WSTRING is handled
    // identically to STRING: the length is neither scaled nor reinterpreted
    // for the wider element, since C# strings are already UTF-16, so the
    // keyword is the only difference.
    internal static class StringTypeInfo
    {
        private const int DefaultLength = 80;

        // The size need not be a digit literal - a GVL-qualified constant
        // expression (e.g. cScratchConstants.MAX_STRING_SIZE) is equally legal
        // IEC 61131-3 - so the group captures raw expression text for
        // ParseLength to resolve. IgnoreCase because IEC type names are
        // case-insensitive ('WString' is as valid as 'WSTRING'), as are the
        // bare-keyword comparisons below.
        private static readonly Regex SizedPattern = new Regex(
            @"^(STRING|WSTRING)\s*\(\s*(?<n>[^()]+?)\s*\)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool IsStringType(string typeName)
        {
            if (typeName == null)
                return false;

            var trimmed = typeName.Trim();
            return string.Equals(trimmed, "STRING", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "WSTRING", System.StringComparison.OrdinalIgnoreCase)
                || SizedPattern.IsMatch(trimmed);
        }

        // Digit-literal sizes only; throws NotSupportedException for a
        // constant-expression size, which callers that may see one resolve
        // through the resolveExpr overload instead.
        public static int ParseLength(string typeName) =>
            ParseLength(typeName, exprText => throw new NotSupportedException(
                $"STRING/WSTRING size '{exprText}' is not an integer literal; " +
                "use the ParseLength(typeName, resolveExpr) overload to resolve constant expressions."));

        public static int ParseLength(string typeName, Func<string, int> resolveExpr)
        {
            var trimmed = typeName.Trim();
            if (string.Equals(trimmed, "STRING", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "WSTRING", System.StringComparison.OrdinalIgnoreCase))
                return DefaultLength;

            var match = SizedPattern.Match(trimmed);
            var sizeText = match.Groups["n"].Value.Trim();
            return int.TryParse(sizeText, out var literal) ? literal : resolveExpr(sizeText);
        }
    }
}

