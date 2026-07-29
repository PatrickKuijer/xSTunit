using System;
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

        // The sized form's size text need not be a bare digit literal - a
        // GVL-qualified constant expression (e.g. cFramework.MAX_STRING_SIZE)
        // is equally legal IEC 61131-3 (TcXunit-988), so the group here
        // captures the raw expression text; ParseLength resolves it (either
        // the digit fast path, or via the caller's constant-expression
        // resolver).
        // TcXunit-fzm: IEC 61131-3 type names are case-insensitive ('string'/
        // 'WString' are exactly as valid as 'STRING'/'WSTRING'), so both the
        // bare-keyword comparisons below and this pattern match
        // case-insensitively.
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

        // Digit-literal fast path only; throws for a non-literal size
        // expression (e.g. a GVL constant) - callers that may see one should
        // use the ParseLength(typeName, resolveExpr) overload instead
        // (mirroring Engine.Defaults.ResolveArrayBound /
        // StructBoundaryBuilder.EvaluateConstExpr for ARRAY bounds).
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

