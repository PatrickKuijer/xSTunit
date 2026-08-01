using System;
using System.Text.RegularExpressions;

namespace xStunit.Interpreter
{
    // Parses declared STRING/WSTRING type text - bare "STRING"/"WSTRING", or
    // the sized "STRING(n)"/"WSTRING(n)" - for StructBoundaryBuilder's
    // empty/max-length boundary and Engine.Defaults's "" default. An
    // unsized declaration is taken as length 80.
    //
    // The declared length is a CHARACTER count for both keywords: in the value
    // model WSTRING collapses onto STRING, since a C# string is already
    // UTF-16 and holds either. The BYTE model does not collapse - a WSTRING
    // character is two bytes on the wire - so callers that size or lay out
    // bytes scale the length by CharWidth.
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
            @"^(?<keyword>STRING|WSTRING)\s*\(\s*(?<n>[^()]+?)\s*\)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool IsStringType(string typeName)
        {
            if (typeName == null)
                return false;

            var trimmed = typeName.Trim();
            return string.Equals(trimmed, "STRING", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "WSTRING", System.StringComparison.OrdinalIgnoreCase)
                || SizedPattern.IsMatch(trimmed);
        }

        public static bool IsWideStringType(string typeName)
        {
            if (typeName == null)
                return false;

            var trimmed = typeName.Trim();
            if (string.Equals(trimmed, "WSTRING", System.StringComparison.OrdinalIgnoreCase))
                return true;

            var match = SizedPattern.Match(trimmed);
            return match.Success
                && string.Equals(match.Groups["keyword"].Value, "WSTRING", System.StringComparison.OrdinalIgnoreCase);
        }

        // Bytes per character on the wire, which doubles as the type's
        // alignment and as the width of its terminator: a narrow STRING ends
        // at a zero byte, a WSTRING at a zero WORD.
        public static int CharWidth(string typeName) => IsWideStringType(typeName) ? 2 : 1;

        // Digit-literal sizes only; throws NotSupportedException for a
        // constant-expression size, which callers that may see one resolve
        // through the resolveExpr overload instead.
        public static int ParseLength(string typeName) =>
            ParseLength(typeName, exprText => throw new NotSupportedException(
                $"STRING/WSTRING size '{exprText}' is not an integer literal; " +
                "use the ParseLength(typeName, resolveExpr) overload to resolve constant expressions."));

        // The declared character capacity, for a caller with no expression
        // evaluator to hand. False for a type that is not a STRING/WSTRING and
        // for a constant-expression size (e.g. STRING(cConstants.MAX)), which
        // only ParseLength's resolveExpr overload can settle.
        public static bool TryParseLength(string typeName, out int length)
        {
            length = 0;
            if (typeName == null)
                return false;

            var trimmed = typeName.Trim();
            if (string.Equals(trimmed, "STRING", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "WSTRING", System.StringComparison.OrdinalIgnoreCase))
            {
                length = DefaultLength;
                return true;
            }

            var match = SizedPattern.Match(trimmed);
            return match.Success && int.TryParse(match.Groups["n"].Value.Trim(), out length);
        }

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

