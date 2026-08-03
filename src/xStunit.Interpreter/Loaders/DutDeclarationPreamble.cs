namespace xStunit.Interpreter
{
    // The pragma and comment text a .TcDUT is allowed to carry before its
    // "TYPE Name :" header. None of it belongs to a DUT's declaration shape,
    // so which forms count as preamble is decided here once and used by every
    // DUT loader: a STRUCT, ENUM and ALIAS documented in the same house style
    // must all still load, and they only do if the three loaders skip the same
    // set.
    public static class DutDeclarationPreamble
    {
        // Returns declarationText from its TYPE header onwards, with leading
        // "{...}" pragmas, "// ..." lines and "(* ... *)" blocks - in any order
        // and any number - removed. Line endings are normalised to "\n", so a
        // caller may split the result on it.
        public static string Strip(string declarationText)
        {
            var text = Normalize(declarationText);
            return text.Substring(HeaderStart(text));
        }

        // The preamble itself, for the one thing a caller needs out of it: the
        // pack_mode pragma, whose value outlives the header scan because it
        // changes the type's memory layout.
        public static string LeadingText(string declarationText)
        {
            var text = Normalize(declarationText);
            return text.Substring(0, HeaderStart(text));
        }

        private static string Normalize(string declarationText) =>
            declarationText.Replace("\r\n", "\n");

        private static int HeaderStart(string text)
        {
            var index = 0;
            while (true)
            {
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                    index++;

                var afterItem = SkipPreambleItem(text, index);
                if (afterItem < 0)
                    return index;

                index = afterItem;
            }
        }

        // The position after the preamble item starting at index, or -1 when
        // the text there is not one. An unterminated comment or pragma counts
        // as "not one": there is no header behind it to find, and consuming the
        // rest of the text would turn a malformed DUT into an empty one.
        private static int SkipPreambleItem(string text, int index)
        {
            if (index >= text.Length)
                return -1;

            if (text[index] == '{')
            {
                // A pragma is single-line, so an unclosed '{' must not swallow
                // the header on the line below it.
                var close = text.IndexOf('}', index);
                var newline = text.IndexOf('\n', index);
                return close >= 0 && (newline < 0 || close < newline) ? close + 1 : -1;
            }

            if (StartsWith(text, index, "//"))
            {
                var newline = text.IndexOf('\n', index);
                return newline < 0 ? text.Length : newline + 1;
            }

            return StartsWith(text, index, "(*") ? SkipBlockComment(text, index) : -1;
        }

        // IEC 61131-3 block comments nest, so this counts depth rather than
        // stopping at the first "*)".
        private static int SkipBlockComment(string text, int index)
        {
            var depth = 0;
            for (var i = index; i < text.Length - 1; i++)
            {
                if (StartsWith(text, i, "(*"))
                {
                    depth++;
                    i++;
                    continue;
                }

                if (!StartsWith(text, i, "*)"))
                    continue;

                depth--;
                i++;
                if (depth == 0)
                    return i + 1;
            }

            return -1;
        }

        private static bool StartsWith(string text, int index, string token) =>
            index + token.Length <= text.Length
            && string.CompareOrdinal(text, index, token, 0, token.Length) == 0;
    }
}
