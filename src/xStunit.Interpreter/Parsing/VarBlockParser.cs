using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace xStunit.Interpreter
{
    // Turns the raw VAR/VAR_INPUT/VAR_OUTPUT/VAR_IN_OUT/VAR_TEMP declaration
    // text a FUNCTION_BLOCK or METHOD carries in its Declaration CDATA into
    // typed VarDecl entries. Also doubles as StructDeclParser's field-list
    // parser: STRUCT/UNION and their END_ keywords toggle the same section
    // state as VAR/END_VAR.
    // Scoped to the fixtures' grammar - one name per line, no comma lists.
    public static class VarBlockParser
    {
        // The W?STRING(...) size may be any IEC 61131-3 constant expression
        // (int literal, GVL-qualified constant, +-*/), not just a bare digit
        // literal, so the type group takes [^()]+ rather than \d+ and passes
        // that expression text through verbatim. StringTypeInfo/
        // StructBoundaryBuilder resolve it later, the same way ARRAY bounds
        // are resolved.
        //
        // An ARRAY element type reuses that same sized-string alternative. A
        // line this pattern fails to match is skipped silently, so an element
        // type it cannot spell costs a variable rather than a parse error:
        // every later use of the name reports "Unknown variable" instead.
        private const string SizedStringPattern = @"W?STRING\s*\(\s*[^()]+\s*\)";

        // IgnoreCase because IEC 61131-3 type names are case-insensitive and
        // every consumer of the type text this produces already treats them
        // that way - AddressTypeInfo, ArrayTypeInfo, StringTypeInfo,
        // IecNumericType, IecElementaryDefault and TypeRegistry. Matching
        // POINTER TO/REFERENCE TO/ARRAY..OF/W?STRING exactly would make the
        // parser the one layer that disagrees, and it disagrees silently: the
        // whole line fails to match and the variable never exists. The matched
        // text is still passed through verbatim, since the declared spelling is
        // what FbInstance and Cell record.
        private static readonly Regex VarLinePattern = new Regex(
            @"^(?<name>\w+)\s*:\s*(?<type>POINTER TO \w+|REFERENCE TO \w+|ARRAY\s*\[[^\]]+\]\s*OF\s*(?:"
            + SizedStringPattern + @"|\w+)|" + SizedStringPattern + @"|\w+)\s*(:=\s*(?<default>.+?))?;$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static IReadOnlyList<VarDecl> Parse(string declarationText)
        {
            var result = new List<VarDecl>();
            var currentSection = (VarSection?)null;

            foreach (var rawLine in declarationText.Split('\n'))
            {
                var line = StripTrailingComment(rawLine.Trim()).Trim();
                if (line.Length == 0)
                    continue;

                // Section keywords are read case-sensitively, here and in
                // TryReadSectionHeader, unlike the type names VarLinePattern
                // accepts in any case. STRUCT/UNION are also read
                // case-sensitively upstream by StructDeclParser.DeclaredBody,
                // which is what decides whether a DUT reaches DutStructLoader
                // or DutAliasLoader in the first place. Widening only this copy
                // would open a body here for a declaration those two already
                // routed elsewhere, so the accepted spelling is a pipeline-wide
                // decision rather than this parser's to make alone.
                switch (line)
                {
                    case "END_VAR":
                        currentSection = null;
                        continue;
                    case "STRUCT":
                    case "UNION":
                        currentSection = VarSection.Local;
                        continue;
                    case "END_STRUCT":
                    case "END_UNION":
                        currentSection = null;
                        continue;
                }

                if (TryReadSectionHeader(line, out var openedSection))
                {
                    currentSection = openedSection;
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

        // Reads a VAR/VAR_INPUT/VAR_OUTPUT/VAR_IN_OUT/VAR_TEMP/VAR_GLOBAL
        // header and the section it opens, ignoring any CONSTANT/RETAIN/
        // PERSISTENT modifiers trailing it. The modifiers change nothing but
        // the section a field lands in - no write-protection, retain or
        // persistence semantics are modelled - but the header they sit on has
        // to be recognized anyway. Unrecognized, it leaves no section open, and
        // every declaration under it is dropped without a word: each later use
        // reports "Unknown variable", pointing at the use rather than at the
        // block that never opened.
        private static bool TryReadSectionHeader(string line, out VarSection section)
        {
            var space = line.IndexOfAny(new[] { ' ', '\t' });
            var keyword = space < 0 ? line : line.Substring(0, space);

            switch (keyword)
            {
                case "VAR":
                    section = VarSection.Local;
                    return true;
                case "VAR_INPUT":
                    section = VarSection.Input;
                    return true;
                case "VAR_OUTPUT":
                    section = VarSection.Output;
                    return true;
                case "VAR_IN_OUT":
                    section = VarSection.InOut;
                    return true;
                // A top-level VAR_TEMP gets its own section rather than
                // aliasing to Local: it has to live in instance.Fields for
                // dot-access and methods to see it, so there is no per-call
                // Frame to give it the reset-every-invocation semantics
                // VAR_TEMP requires. See VarSection.Temp.
                case "VAR_TEMP":
                    section = VarSection.Temp;
                    return true;
                case "VAR_GLOBAL":
                    section = VarSection.Global;
                    return true;
                default:
                    section = default;
                    return false;
            }
        }

        // Strips a trailing "// ..." or "(* ... *)" comment, ignoring those
        // sequences when they appear inside a single- or double-quoted string
        // literal - a STRING default value is allowed to contain them.
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
