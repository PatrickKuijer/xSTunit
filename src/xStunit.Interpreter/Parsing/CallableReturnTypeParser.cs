using System.Text.RegularExpressions;

namespace xStunit.Interpreter
{
    // Parses the declared return type off a METHOD/FUNCTION/PROPERTY header
    // line, e.g. the "LREAL" in "METHOD PRIVATE M_Read : LREAL".
    //
    // VarBlockParser cannot do this: it only looks at lines *inside* a
    // VAR...END_VAR section, and the return type is declared on the header
    // line above the first VAR. Without it Engine has to infer the return
    // cell's type from whatever value is assigned into it first.
    //
    // Alias resolution is deliberately not done here; callers run the result
    // through TypeRegistry.ResolveAlias themselves, as every other type-name
    // consumer does.
    internal static class CallableReturnTypeParser
    {
        // Anchored to the start of the comment-stripped declaration text and
        // never multiline, so only the header line can match - a
        // "nIndex : UINT;" line in the VAR block below it must not.
        //
        // (?!_BLOCK) keeps FUNCTION_BLOCK out, the same guard Engine's
        // GlobalFunctionDeclarationPattern uses. Each access/inheritance
        // modifier must be followed by whitespace, so a method named FINALIZE
        // isn't read as FINAL + "IZE".
        //
        // PROPERTY belongs in the keyword alternation because a Get/Set
        // accessor's Local named after the property has the same
        // lazily-created-by-first-assignment shape as a return value.
        //
        // The return-type alternation mirrors VarBlockParser.VarLinePattern's
        // with one deliberate difference: the STRING length excludes \n here,
        // because that pattern matches a whole ";"-terminated VAR line
        // whereas this one has only the header line and must not run on into
        // the VAR block. Keep the two in step if either grows a type shape.
        private static readonly Regex HeaderPattern = new Regex(
            @"^\s*(?:METHOD|FUNCTION|PROPERTY)(?!_BLOCK)\s+" +
            @"(?:(?:PRIVATE|PUBLIC|PROTECTED|INTERNAL|FINAL|ABSTRACT)\s+)*" +
            @"\w+\s*:\s*" +
            @"(?<type>POINTER\s+TO\s+\w+|REFERENCE\s+TO\s+\w+|W?STRING\s*\(\s*[^()\n]+\s*\)|\w+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // False both for a callable declared with no return type ("METHOD
        // M_Do") and for declaration text that is not a callable header at
        // all - a FUNCTION_BLOCK/PROGRAM header, or the bare VAR block a
        // hand-built PouAst carries.
        public static bool TryGetReturnTypeName(string declarationText, out string typeName)
        {
            typeName = null;
            if (string.IsNullOrEmpty(declarationText))
                return false;

            var match = HeaderPattern.Match(StripLeadingComments(declarationText));
            if (!match.Success)
                return false;

            typeName = match.Groups["type"].Value.Trim();
            return true;
        }

        // Strips leading (* ... *) and // comments, plus interleaved
        // whitespace, so a header-anchored regex still matches a declaration
        // that opens with a purpose comment - which most ST declarations do.
        // Shared with Engine's GlobalFunctionDeclarationPattern check, which
        // needs the identical treatment for the identical reason.
        public static string StripLeadingComments(string declarationText)
        {
            var index = 0;
            while (index < declarationText.Length)
            {
                var match = LeadingCommentPattern.Match(declarationText, index);
                if (!match.Success || match.Length == 0)
                    break;
                index = match.Index + match.Length;
            }

            return declarationText.Substring(index);
        }

        private static readonly Regex LeadingCommentPattern = new Regex(
            @"\G\s*(\(\*.*?\*\)|//[^\n]*)",
            RegexOptions.Compiled | RegexOptions.Singleline);
    }
}
