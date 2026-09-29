using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace xStunit.Interpreter
{
    // Turns the VAR_* sections a FUNCTION_BLOCK or METHOD carries in its
    // Declaration CDATA into typed VarDecl entries. Also doubles as
    // StructDeclParser's field-list parser: STRUCT/UNION and their END_
    // keywords toggle the same section state as VAR/END_VAR.
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

        // A type name may carry a library qualifier - Lib.FB_Name - which the
        // registries already strip to reach the bare name. Segments repeat
        // because a qualifier may itself be nested.
        private const string QualifiedNamePattern = @"\w+(?:\.\w+)*";

        // ARRAY[lo..hi,...] OF <elementType>, where the element type is a
        // sized string or a (qualified) name - the same alternatives a bare
        // declaration accepts. Bounds pass through verbatim, literal or
        // GVL-qualified constant expression alike; ArrayTypeInfo/
        // Engine.Defaults resolve them later.
        private const string ArrayPattern = @"ARRAY\s*\[[^\]]+\]\s*OF\s*(?:" + SizedStringPattern + "|" + QualifiedNamePattern + ")";

        // What POINTER TO / REFERENCE TO may address: a plain or qualified
        // type name, a sized string, or a whole array. A POINTER TO/
        // REFERENCE TO an array or a sized string is a real, common shape
        // (step-timer FBs holding a POINTER TO ARRAY OF ..., FUNCTIONs
        // taking a REFERENCE TO ARRAY OF .. to avoid a by-value copy) - not
        // widening this cost every such declaration its variable, silently,
        // the same way an unmatched element type does.
        private const string AddressTargetPattern = @"(?:" + ArrayPattern + "|" + SizedStringPattern + "|" + QualifiedNamePattern + ")";

        // IgnoreCase because IEC 61131-3 type names are case-insensitive and
        // every consumer of the type text this produces already treats them
        // that way (IecIdentifier for type names, the *TypeInfo readers for
        // POINTER TO/ARRAY/STRING text). Matching
        // POINTER TO/REFERENCE TO/ARRAY..OF/W?STRING exactly would make the
        // parser the one layer that disagrees, and it disagrees silently: the
        // whole line fails to match and the variable never exists. The matched
        // text is still passed through verbatim, since the declared spelling is
        // what FbInstance and Cell record.
        private static readonly Regex VarLinePattern = new Regex(
            @"^(?<name>\w+)\s*:\s*(?<type>POINTER TO " + AddressTargetPattern
            + @"|REFERENCE TO " + AddressTargetPattern + @"|" + ArrayPattern + @"|"
            + SizedStringPattern + @"|" + QualifiedNamePattern + @")\s*(:=\s*(?<default>.+?))?;$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Reports the lines inside an open section that the pattern could not
        // match, which are the variables this parse lost. Pragmas, comment text
        // and anything outside a section are not reported: they were never
        // declarations, they outnumber real losses several times over in a real
        // tree, and a report they drown out is one nobody reads.
        public static IReadOnlyList<VarDecl> Parse(string declarationText, out IReadOnlyList<string> unreadableLines)
        {
            var unreadable = new List<string>();
            var result = Parse(declarationText, unreadable);
            unreadableLines = unreadable;
            return result;
        }

        public static IReadOnlyList<VarDecl> Parse(string declarationText)
        {
            return Parse(declarationText, null);
        }

        private static IReadOnlyList<VarDecl> Parse(string declarationText, List<string> unreadable)
        {
            var result = new List<VarDecl>();
            var currentSection = (VarSection?)null;
            var inBlockComment = false;

            foreach (var rawLine in declarationText.Split('\n'))
            {
                var line = StripComments(rawLine.Trim(), ref inBlockComment).Trim();
                if (line.Length == 0)
                    continue;

                // A pragma carries no declaration, so it costs no variable and
                // is not a loss to report. It also may not close the section it
                // sits in, which is why it is skipped rather than falling
                // through to the header read below.
                if (line[0] == '{')
                    continue;

                // Section keywords match in any case, as IEC 61131-3 keywords
                // do. StructDeclParser.DeclaredBody reads STRUCT/UNION the same
                // way when it routes a DUT here rather than to DutAliasLoader,
                // and the two have to agree: a spelling only one side accepts
                // opens a body here for a declaration the other routed away.
                switch (line.ToUpperInvariant())
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
                {
                    unreadable?.Add(line);
                    continue;
                }

                var defaultGroup = match.Groups["default"];
                result.Add(new VarDecl(
                    match.Groups["name"].Value,
                    match.Groups["type"].Value,
                    defaultGroup.Success ? defaultGroup.Value : null,
                    currentSection.Value));
            }

            return result;
        }

        // Reads a VAR_* section header and the section it opens, ignoring any
        // CONSTANT/RETAIN/PERSISTENT modifiers trailing it. The modifiers
        // change nothing but the section a field lands in - no write-
        // protection, retain or persistence semantics are modelled - but the
        // header they sit on has to be recognized anyway. Unrecognized, it leaves no section open, and
        // every declaration under it is dropped without a word: each later use
        // reports "Unknown variable", pointing at the use rather than at the
        // block that never opened.
        private static bool TryReadSectionHeader(string line, out VarSection section)
        {
            var space = line.IndexOfAny(new[] { ' ', '\t' });
            var keyword = space < 0 ? line : line.Substring(0, space);

            switch (keyword.ToUpperInvariant())
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
                case "VAR_INST":
                    section = VarSection.MethodInstance;
                    return true;
                case "VAR_GLOBAL":
                    section = VarSection.Global;
                    return true;
                default:
                    section = default;
                    return false;
            }
        }

        // Removes "// ..." and "(* ... *)" comment text, ignoring those
        // sequences when they appear inside a single- or double-quoted string
        // literal - a STRING default value is allowed to contain them.
        //
        // inBlockComment carries across lines because a "(* ... *)" may span
        // several, and each line is offered to the caller separately. Without
        // that state the body and closing line of a multi-line comment arrive
        // as declarations: harmless while an unmatched line was skipped in
        // silence, but once losses are reported they are the bulk of what gets
        // reported and nothing real is visible behind them.
        //
        // Code either side of a comment on the same line is kept, so a
        // declaration that follows a comment's close is still read.
        private static string StripComments(string line, ref bool inBlockComment)
        {
            var code = new StringBuilder(line.Length);
            char? quoteChar = null;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (inBlockComment)
                {
                    if (c == '*' && i < line.Length - 1 && line[i + 1] == ')')
                    {
                        inBlockComment = false;
                        i++;
                    }

                    continue;
                }

                if (quoteChar != null)
                {
                    code.Append(c);
                    if (c == quoteChar)
                        quoteChar = null;
                    continue;
                }

                if (c == '\'' || c == '"')
                {
                    quoteChar = c;
                    code.Append(c);
                    continue;
                }

                if (c == '/' && i < line.Length - 1 && line[i + 1] == '/')
                    break;

                if (c == '(' && i < line.Length - 1 && line[i + 1] == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }

                code.Append(c);
            }

            return code.ToString();
        }
    }
}
