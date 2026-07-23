using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TcXunit.Interpreter
{
    // Parses the raw VAR/VAR_INPUT/VAR_OUTPUT/VAR_IN_OUT declaration text a
    // FUNCTION_BLOCK or METHOD carries in its Declaration CDATA into typed
    // VarDecl entries. Also doubles as the STRUCT field-list parser for
    // StructDeclParser (STRUCT/END_STRUCT toggle the same section state as
    // VAR/END_VAR, mapped to VarSection.Local - TcXunit-w5x.15.6). Scoped to
    // the fixture's grammar (TcXunit-w5x.8): single name per line, no comma
    // lists.
    public static class VarBlockParser
    {
        private static readonly Regex VarLinePattern = new Regex(
            @"^(?<name>\w+)\s*:\s*(?<type>POINTER TO \w+|REFERENCE TO \w+|ARRAY\s*\[[^\]]+\]\s*OF\s*\w+|STRING\s*\(\s*\d+\s*\)|\w+)\s*(:=\s*(?<default>.+?))?;$",
            RegexOptions.Compiled);

        public static IReadOnlyList<VarDecl> Parse(string declarationText)
        {
            var result = new List<VarDecl>();
            var currentSection = (VarSection?)null;

            foreach (var rawLine in declarationText.Split('\n'))
            {
                var line = StripTrailingComment(rawLine.Trim()).Trim();
                if (line.Length == 0)
                    continue;

                switch (line)
                {
                    case "VAR":
                        currentSection = VarSection.Local;
                        continue;
                    case "VAR_INPUT":
                        currentSection = VarSection.Input;
                        continue;
                    case "VAR_OUTPUT":
                        currentSection = VarSection.Output;
                        continue;
                    case "VAR_IN_OUT":
                        currentSection = VarSection.InOut;
                        continue;
                    case "END_VAR":
                        currentSection = null;
                        continue;
                    case "STRUCT":
                        currentSection = VarSection.Local;
                        continue;
                    case "END_STRUCT":
                        currentSection = null;
                        continue;
                }

                if (currentSection == null)
                    continue;

                var match = VarLinePattern.Match(line);
                if (!match.Success)
                    continue;

                var defaultGroup = match.Groups["default"];
                result.Add(new VarDecl(
                    match.Groups["name"].Value,
                    match.Groups["type"].Value,
                    defaultGroup.Success ? defaultGroup.Value : null,
                    currentSection.Value));
            }

            return result;
        }

        // Strips a trailing "// ..." line comment or "(* ... *)" block
        // comment, ignoring "//", "(*" and "*)" that appear inside a
        // single- or double-quoted string literal (e.g. a STRING default
        // value containing those sequences). TcXunit-dem, TcXunit-n3w.
        private static string StripTrailingComment(string line)
        {
            char? quoteChar = null;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (quoteChar != null)
                {
                    if (c == quoteChar)
                        quoteChar = null;
                    continue;
                }

                if (c == '\'' || c == '"')
                {
                    quoteChar = c;
                    continue;
                }

                if (i < line.Length - 1 && c == '/' && line[i + 1] == '/')
                    return line.Substring(0, i);

                if (i < line.Length - 1 && c == '(' && line[i + 1] == '*'
                    && line.IndexOf("*)", i + 2, System.StringComparison.Ordinal) >= 0)
                    return line.Substring(0, i);
            }

            return line;
        }
    }
}
