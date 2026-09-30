using System;
using System.Collections.Generic;
using System.Text;

namespace xStunit.Interpreter
{
    // Tokenizes only the ST subset the fixtures actually use, extended
    // on demand as new fixture bodies need it rather than built out to the
    // full IEC 61131-3 grammar up front. A construct this lexer doesn't
    // recognize is a gap, not necessarily a bug.
    public static class Lexer
    {
        // The reserved words the parser dispatches on, in the one spelling it
        // compares against. IEC 61131-3 keywords are case-insensitive, so a
        // word matching one of these in any case is emitted upper-case and
        // every parser check holds for 'if', 'If' and 'IF' alike. Any other
        // word keeps its written spelling: identifiers resolve
        // case-insensitively downstream, and diagnostics echo the name as the
        // source wrote it.
        private static readonly HashSet<string> ParserDispatchKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "IF", "THEN", "ELSIF", "ELSE", "END_IF",
            "CASE", "OF", "END_CASE",
            "FOR", "TO", "BY", "DO", "END_FOR",
            "WHILE", "END_WHILE",
            "REPEAT", "UNTIL", "END_REPEAT",
            "EXIT", "RETURN",
            "AND", "AND_THEN", "OR", "OR_ELSE", "XOR", "NOT", "MOD",
            "THIS", "SUPER",
        };

        // Strips (* ... *) and // ... comments, leaving everything else -
        // including string literal contents - untouched.
        //
        // Deliberately not implemented as a call to Tokenize: callers pass
        // whole declaration text, which may carry constructs Tokenize would
        // throw on (e.g. a leading {attribute '...'} pragma). Only comments
        // and string literals need recognizing here; the rest is copied
        // through verbatim.
        public static string StripComments(string text)
        {
            var sb = new StringBuilder(text.Length);
            var i = 0;

            while (i < text.Length)
            {
                var c = text[i];

                if (c == '(' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    var end = text.IndexOf("*)", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? text.Length : end + 2;
                    continue;
                }

                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    var end = text.IndexOf('\n', i);
                    i = end < 0 ? text.Length : end;
                    continue;
                }

                if (c == '\'' || c == '"')
                {
                    var quote = c;
                    sb.Append(c);
                    i++;
                    while (i < text.Length && text[i] != quote)
                    {
                        if (text[i] == '$' && i + 1 < text.Length)
                        {
                            var start = i;
                            if (TryConsumeDollarEscape(text, ref i, quote).HasValue)
                            {
                                sb.Append(text, start, i - start);
                                continue;
                            }
                        }

                        sb.Append(text[i]);
                        i++;
                    }

                    if (i < text.Length)
                    {
                        sb.Append(text[i]); // closing quote
                        i++;
                    }

                    continue;
                }

                sb.Append(c);
                i++;
            }

            return sb.ToString();
        }

        public static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            var i = 0;

            // `i` only ever moves forward, so folding whatever was consumed
            // since the previous token into the counter once per loop turn is
            // O(n) overall and covers skipped whitespace, // comments and
            // multi-line (* *) comments alike - no per-branch bookkeeping to
            // forget.
            var line = 1;
            var counted = 0;

            while (i < text.Length)
            {
                CountLineBreaks(text, ref counted, i, ref line);

                // Captured before the token is consumed: a token's Line is
                // where it STARTS, so a multi-line string literal keeps its
                // opening line.
                var tokenLine = line;
                var c = text[i];

                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (c == '(' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    var end = text.IndexOf("*)", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? text.Length : end + 2;
                    continue;
                }

                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    var end = text.IndexOf('\n', i);
                    i = end < 0 ? text.Length : end;
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    var start = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                        i++;
                    var word = text.Substring(start, i - start);
                    var foldedWord = word.ToUpperInvariant();

                    if (foldedWord == "REF" && i < text.Length && text[i] == '=')
                    {
                        i++;
                        tokens.Add(new Token(TokenType.RefAssign, "REF=", tokenLine));
                        continue;
                    }

                    if ((foldedWord == "REAL" || foldedWord == "LREAL") && i < text.Length && text[i] == '#')
                    {
                        i++; // '#'
                        var numStart = i;
                        while (i < text.Length && char.IsDigit(text[i]))
                            i++;
                        ConsumeFraction(text, ref i);
                        var numText = text.Substring(numStart, i - numStart);
                        tokens.Add(new Token(foldedWord == "REAL" ? TokenType.RealLiteral : TokenType.LrealLiteral, numText, tokenLine));
                        continue;
                    }

                    if ((foldedWord == "LTIME" || foldedWord == "LT") && i < text.Length && text[i] == '#')
                    {
                        i++; // '#'
                        var durStart = i;
                        while (i < text.Length && char.IsLetterOrDigit(text[i]))
                            i++;
                        tokens.Add(new Token(TokenType.LtimeLiteral, text.Substring(durStart, i - durStart), tokenLine));
                        continue;
                    }

                    if ((foldedWord == "TIME" || foldedWord == "T") && i < text.Length && text[i] == '#')
                    {
                        i++; // '#'
                        var durStart = i;
                        while (i < text.Length && char.IsLetterOrDigit(text[i]))
                            i++;
                        tokens.Add(new Token(TokenType.TimeLiteral, text.Substring(durStart, i - durStart), tokenLine));
                        continue;
                    }

                    // DATE/DATE_AND_TIME/TIME_OF_DAY are calendar/clock
                    // literals, not TIME's duration-segment grammar, so their
                    // body is a run of digits/'-'/':'/'.' rather than
                    // digits+unit-letters.
                    if ((foldedWord == "DATE_AND_TIME" || foldedWord == "DT") && i < text.Length && text[i] == '#')
                    {
                        i++; // '#'
                        var litStart = i;
                        while (i < text.Length && IsDateTimeLiteralChar(text[i]))
                            i++;
                        tokens.Add(new Token(TokenType.DateAndTimeLiteral, text.Substring(litStart, i - litStart), tokenLine));
                        continue;
                    }

                    if ((foldedWord == "TIME_OF_DAY" || foldedWord == "TOD") && i < text.Length && text[i] == '#')
                    {
                        i++; // '#'
                        var litStart = i;
                        while (i < text.Length && IsDateTimeLiteralChar(text[i]))
                            i++;
                        tokens.Add(new Token(TokenType.TimeOfDayLiteral, text.Substring(litStart, i - litStart), tokenLine));
                        continue;
                    }

                    if ((foldedWord == "DATE" || foldedWord == "D") && i < text.Length && text[i] == '#')
                    {
                        i++; // '#'
                        var litStart = i;
                        while (i < text.Length && IsDateTimeLiteralChar(text[i]))
                            i++;
                        tokens.Add(new Token(TokenType.DateLiteral, text.Substring(litStart, i - litStart), tokenLine));
                        continue;
                    }

                    tokens.Add(new Token(TokenType.Identifier, ParserDispatchKeywords.Contains(foldedWord) ? foldedWord : word, tokenLine)
                    {
                        GluedToEquals = i < text.Length && text[i] == '=',
                    });
                    continue;
                }

                if (char.IsDigit(c))
                {
                    var start = i;
                    while (i < text.Length && char.IsDigit(text[i]))
                        i++;

                    // IEC 61131-3 §2.4.2 based literal: <base>#<digits>, e.g.
                    // 16#ABCD (hex), 8#17 (octal), 2#1010 (binary), with
                    // optional '_' digit separators.
                    if (i < text.Length && text[i] == '#'
                        && int.TryParse(text.Substring(start, i - start), out var numberBase))
                    {
                        var hashPos = i;
                        i++; // '#'
                        var digitsStart = i;
                        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                            i++;
                        var digits = text.Substring(digitsStart, i - digitsStart).Replace("_", string.Empty);
                        var value = ParseBasedLiteral(digits, numberBase, text, hashPos);
                        tokens.Add(new Token(TokenType.IntLiteral, value.ToString(), tokenLine));
                        continue;
                    }

                    var isReal = ConsumeFraction(text, ref i);
                    tokens.Add(new Token(isReal ? TokenType.RealLiteral : TokenType.IntLiteral, text.Substring(start, i - start), tokenLine));
                    continue;
                }

                // STRING literals use '...' and WSTRING literals use "...",
                // but both produce the same StringLiteral token: Cell values
                // are plain C# strings regardless of the variable's declared
                // STRING/WSTRING type.
                if (c == '\'' || c == '"')
                {
                    var quote = c;
                    var sb = new StringBuilder();
                    i++;
                    while (i < text.Length && text[i] != quote)
                    {
                        if (text[i] == '$' && i + 1 < text.Length)
                        {
                            var escaped = TryConsumeDollarEscape(text, ref i, quote);
                            if (escaped.HasValue)
                            {
                                sb.Append(escaped.Value);
                                continue;
                            }
                        }

                        sb.Append(text[i]);
                        i++;
                    }
                    i++; // closing quote
                    tokens.Add(new Token(TokenType.StringLiteral, sb.ToString(), tokenLine));
                    continue;
                }

                if (c == ':' && i + 1 < text.Length && text[i + 1] == '=')
                {
                    tokens.Add(new Token(TokenType.Assign, ":=", tokenLine));
                    i += 2;
                    continue;
                }

                if (c == ':')
                {
                    tokens.Add(new Token(TokenType.Colon, ":", tokenLine));
                    i++;
                    continue;
                }

                if (c == '.' && i + 1 < text.Length && text[i + 1] == '.')
                {
                    tokens.Add(new Token(TokenType.DotDot, "..", tokenLine));
                    i += 2;
                    continue;
                }

                if (c == '<' && i + 1 < text.Length && text[i + 1] == '=')
                {
                    tokens.Add(new Token(TokenType.Le, "<=", tokenLine));
                    i += 2;
                    continue;
                }

                if (c == '>' && i + 1 < text.Length && text[i + 1] == '=')
                {
                    tokens.Add(new Token(TokenType.Ge, ">=", tokenLine));
                    i += 2;
                    continue;
                }

                if (c == '<' && i + 1 < text.Length && text[i + 1] == '>')
                {
                    tokens.Add(new Token(TokenType.Ne, "<>", tokenLine));
                    i += 2;
                    continue;
                }

                if (c == '=' && i + 1 < text.Length && text[i + 1] == '>')
                {
                    tokens.Add(new Token(TokenType.Arrow, "=>", tokenLine));
                    i += 2;
                    continue;
                }

                switch (c)
                {
                    // '&' is IEC 61131-3's alias for AND (§2.4.5, table 4) -
                    // lexed straight to the same Identifier/"AND" token so
                    // every AND-aware parser/evaluator path handles it with
                    // no separate token type needed.
                    case '&': tokens.Add(new Token(TokenType.Identifier, "AND", tokenLine)); i++; continue;
                    case '=': tokens.Add(new Token(TokenType.Eq, "=", tokenLine)); i++; continue;
                    case '<': tokens.Add(new Token(TokenType.Lt, "<", tokenLine)); i++; continue;
                    case '>': tokens.Add(new Token(TokenType.Gt, ">", tokenLine)); i++; continue;
                    case '+': tokens.Add(new Token(TokenType.Plus, "+", tokenLine)); i++; continue;
                    case '-': tokens.Add(new Token(TokenType.Minus, "-", tokenLine)); i++; continue;
                    case '*': tokens.Add(new Token(TokenType.Asterisk, "*", tokenLine)); i++; continue;
                    case '/': tokens.Add(new Token(TokenType.Slash, "/", tokenLine)); i++; continue;
                    case '^': tokens.Add(new Token(TokenType.Caret, "^", tokenLine)); i++; continue;
                    case '.': tokens.Add(new Token(TokenType.Dot, ".", tokenLine)); i++; continue;
                    case ',': tokens.Add(new Token(TokenType.Comma, ",", tokenLine)); i++; continue;
                    case ';': tokens.Add(new Token(TokenType.Semicolon, ";", tokenLine)); i++; continue;
                    case '(': tokens.Add(new Token(TokenType.LParen, "(", tokenLine)); i++; continue;
                    case ')': tokens.Add(new Token(TokenType.RParen, ")", tokenLine)); i++; continue;
                    case '[': tokens.Add(new Token(TokenType.LBracket, "[", tokenLine)); i++; continue;
                    case ']': tokens.Add(new Token(TokenType.RBracket, "]", tokenLine)); i++; continue;
                    default:
                        throw new ParseException(
                            $"Unexpected character '{c}' at position {i} in: {text}", c.ToString(), LineAt(text, i));
                }
            }

            // Eof sits at the very end of the body, so fold whatever trailing
            // whitespace/comment tail is left before stamping it.
            CountLineBreaks(text, ref counted, text.Length, ref line);
            tokens.Add(new Token(TokenType.Eof, string.Empty, line));
            return tokens;
        }

        // Folds the line breaks in text[counted..end) into line, advancing
        // counted to end. "\r\n" counts as ONE break, as does a bare "\n" or
        // a lone "\r": TwinCAT writes CRLF into .TcPOU bodies while the
        // fixtures on disk are LF, and both must report the same line numbers
        // to the user.
        private static void CountLineBreaks(string text, ref int counted, int end, ref int line)
        {
            while (counted < end)
            {
                var c = text[counted];
                if (c == '\n')
                    line++;
                else if (c == '\r' && (counted + 1 >= text.Length || text[counted + 1] != '\n'))
                    line++;
                counted++;
            }
        }

        // The 1-based line containing offset, counted from scratch: a throw
        // site needs a line for one arbitrary offset, not the next stretch of
        // the in-progress scan CountLineBreaks is driving. Shares that
        // method's CRLF-as-one-break rule so a ParseException's BodyLine
        // agrees with every other line number this lexer produces.
        private static int LineAt(string text, int offset)
        {
            var line = 1;
            var counted = 0;
            CountLineBreaks(text, ref counted, Math.Min(offset, text.Length), ref line);
            return line;
        }

        // Decodes an IEC 61131-3 '$'-escape at text[i], where quote is the
        // enclosing literal's own delimiter so $<quote> escapes an embedded
        // quote. On a match, advances i past the escape and returns the
        // decoded character; returns null and leaves i untouched otherwise,
        // so the caller falls back to treating '$' as a literal character.
        private static char? TryConsumeDollarEscape(string text, ref int i, char quote)
        {
            var next = text[i + 1];
            char? decoded = next == quote ? quote : next switch
            {
                '$' => '$',
                'L' or 'l' => '\n',
                'N' or 'n' => '\n',
                'P' or 'p' => '\f',
                'R' or 'r' => '\r',
                'T' or 't' => '\t',
                _ => (char?)null,
            };

            if (decoded.HasValue)
            {
                i += 2;
                return decoded;
            }

            if (i + 2 < text.Length && IsHexDigit(text[i + 1]) && IsHexDigit(text[i + 2])
                && int.TryParse(text.Substring(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var code))
            {
                i += 3;
                return (char)code;
            }

            return null;
        }

        private static bool IsHexDigit(char c)
            => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        // Char class for the body of D#/DT#/TOD# literals: digits plus the
        // date/time separators, and no letters - unlike TIME's
        // digits+unit-letters body.
        private static bool IsDateTimeLiteralChar(char c)
            => char.IsDigit(c) || c == '-' || c == ':' || c == '.';

        // Decodes a based-literal digit run (already stripped of '_'
        // separators) for the given base - 2, 8 or 16, the only ones IEC
        // 61131-3 §2.4.2 defines. Letters A-Z/a-z count as digits 10-35 even
        // above the base, so an out-of-range digit produces a clear "invalid
        // digit" error rather than silently misparsing.
        // Accumulates in ulong, not long: a full-width LWORD/ULINT bit pattern
        // (16#FFFFFFFFFFFFFFFF) is a legal 64-bit value with no signed
        // equivalent, and the token this feeds carries the value as digits, so
        // an unsigned spelling is the only one that survives the round trip.
        // Checked, so digits past 64 bits name the literal instead of wrapping
        // into a plausible-looking small number.
        private static ulong ParseBasedLiteral(string digits, int numberBase, string text, int position)
        {
            if (numberBase != 2 && numberBase != 8 && numberBase != 16)
                throw new ParseException(
                    $"Unsupported based-literal base '{numberBase}#' at position {position} in: {text}",
                    numberBase + "#", LineAt(text, position));

            if (digits.Length == 0)
                throw new ParseException(
                    $"Based literal has no digits after '#' at position {position} in: {text}",
                    "#", LineAt(text, position));

            ulong value = 0;
            foreach (var ch in digits)
            {
                var digitValue = ch switch
                {
                    >= '0' and <= '9' => ch - '0',
                    >= 'A' and <= 'Z' => ch - 'A' + 10,
                    >= 'a' and <= 'z' => ch - 'a' + 10,
                    _ => -1,
                };

                if (digitValue < 0 || digitValue >= numberBase)
                    throw new ParseException(
                        $"Digit '{ch}' is invalid for base {numberBase} at position {position} in: {text}",
                        ch.ToString(), LineAt(text, position));

                try
                {
                    value = checked((value * (ulong)numberBase) + (ulong)digitValue);
                }
                catch (OverflowException)
                {
                    throw new ParseException(
                        $"Based literal {numberBase}#{digits} does not fit in 64 bits, the width of the widest " +
                        $"IEC integer type (ULINT/LWORD), at position {position} in: {text}",
                        $"{numberBase}#{digits}", LineAt(text, position));
                }
            }

            return value;
        }

        // Consumes an optional '.digits' fraction and/or '[eE][+-]digits'
        // exponent at i, advancing i past whatever it takes. True means the
        // number is real-valued rather than an integer.
        private static bool ConsumeFraction(string text, ref int i)
        {
            var isReal = false;

            if (i + 1 < text.Length && text[i] == '.' && char.IsDigit(text[i + 1]))
            {
                isReal = true;
                i++;
                while (i < text.Length && char.IsDigit(text[i]))
                    i++;
            }

            if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
            {
                var j = i + 1;
                if (j < text.Length && (text[j] == '+' || text[j] == '-'))
                    j++;
                if (j < text.Length && char.IsDigit(text[j]))
                {
                    isReal = true;
                    j++;
                    while (j < text.Length && char.IsDigit(text[j]))
                        j++;
                    i = j;
                }
            }

            return isReal;
        }
    }
}
