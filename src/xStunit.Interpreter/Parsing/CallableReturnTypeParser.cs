using System.Text.RegularExpressions;

namespace xStunit.Interpreter
{
    // Parses the declared return type off a METHOD/FUNCTION header line, e.g.
    // the "LREAL" in "METHOD PRIVATE M_Read : LREAL" (TcXunit-cq6).
    //
    // VarBlockParser can't do this: it only ever looks at lines *inside* a
    // VAR...END_VAR section, and a callable's return type is declared on the
    // header line above the first VAR - so before this existed, the return
    // type was simply never read, and Engine had to infer the return cell's
    // type from whatever value happened to be assigned into it first.
    //
    // Plain string in, plain string out - no Engine/TypeRegistry dependency,
    // same unit-testable-in-isolation shape as NumericCoercion. Alias
    // resolution is deliberately *not* done here; callers run the result
    // through TypeRegistry.ResolveAlias themselves, as every other type-name
    // consumer does.
    internal static class CallableReturnTypeParser
    {
        // Anchored to the start of the (comment-stripped) declaration text and
        // never multiline, so only the header line itself can match - a
        // "nIndex : UINT;" line inside the VAR block below it must not.
        //
        // (?!_BLOCK) keeps FUNCTION_BLOCK out, same guard and reason as
        // Engine.Invocation's GlobalFunctionDeclarationPattern. Each access/
        // inheritance modifier must be followed by whitespace, so a method
        // named FINALIZE isn't read as FINAL + "IZE".
        //
        // PROPERTY joins METHOD/FUNCTION in the keyword alternation
        // (TcXunit-8we): a PROPERTY Get/Set accessor's Local-named-after-the-
        // property has the exact same lazily-created-by-first-assignment
        // shape as a METHOD/FUNCTION's return value, so its header ("PROPERTY
        // nGain : LREAL") needs the identical parse. PROPERTY has no _BLOCK
        // form, so the (?!_BLOCK) guard is harmless noise for it, not a
        // second guard that needs its own reasoning.
        //
        // The return-type alternation mirrors VarBlockParser.VarLinePattern's
        // - a W?STRING(...) length may be any IEC constant expression, and
        // POINTER/REFERENCE TO are two-word type names - with one deliberate
        // difference: the STRING length excludes \n here, because that pattern
        // matches a whole ";"-terminated VAR line whereas this one has only
        // the header line to work with and must not run on into the VAR block
        // below it. Keep the two in step if either grows a new type shape.
        private static readonly Regex HeaderPattern = new Regex(
            @"^\s*(?:METHOD|FUNCTION|PROPERTY)(?!_BLOCK)\s+" +
            @"(?:(?:PRIVATE|PUBLIC|PROTECTED|INTERNAL|FINAL|ABSTRACT)\s+)*" +
            @"\w+\s*:\s*" +
            @"(?<type>POINTER\s+TO\s+\w+|REFERENCE\s+TO\s+\w+|W?STRING\s*\(\s*[^()\n]+\s*\)|\w+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // False for a callable declared with no return type ("METHOD M_Do"),
        // and for declaration text that isn't a callable header at all (a
        // FUNCTION_BLOCK/PROGRAM header, or the bare VAR block a hand-built
        // PouAst carries).
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

        // Strips leading (* ... *) block comments and // line comments (with
        // any interleaved whitespace) from the start of IEC declaration text,
        // so header-anchored regexes can match declarations that open with a
        // purpose comment - this codebase's standard convention (TcXunit-9k6).
        // Shared with Engine.Invocation's GlobalFunctionDeclarationPattern
        // check, which needs the identical treatment for the identical reason.
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
