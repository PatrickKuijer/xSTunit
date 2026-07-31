using System;
using System.Collections.Generic;
using System.Text;

namespace TcXunit.Interpreter
{
    // Tokenizes the ST statement subset the fixture actually uses
    // (TcXunit-w5x.8/.12). No unary minus, no real/string escapes -
    // grow-on-demand as new fixture bodies need them.
    public static class Lexer
    {
        // Strips (* ... *) and // ... comments from text, leaving everything
        // else - including string literal contents - untouched. Used by
        // SuiteCoverage (TcXunit-2o9.2) so a type name mentioned only inside
        // a comment isn't treated as a real reference from a suite.
        //
        // Mirrors the comment-recognition branches from Tokenize above
        // (same "(*"/"*)"/"//" detection, same string-literal skip via
        // TryConsumeDollarEscape so an escaped quote doesn't end a string
        // early) rather than duplicating that logic with a second regex.
        // Deliberately NOT a call to Tokenize itself: callers pass whole
        // declaration text, which may carry constructs Tokenize doesn't
        // handle (e.g. a leading {attribute '...'} pragma) and would throw
        // on; this only needs to recognize comments and strings, so
        // everything else is copied through unchanged.
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

            // Line tracking (TcXunit-p3t.2). `i` only ever moves forward, so
            // folding whatever was consumed since the previous token into the
            // counter once per loop turn is O(n) overall and covers skipped
            // whitespace, // comments and multi-line (* *) comments alike -
            // no per-branch bookkeeping to forget.
            var line = 1;
            var counted = 0;

            while (i < text.Length)
            {
                CountLineBreaks(text, ref counted, i, ref line);

                // Stamped on whatever token this turn produces: a token's
                // Line is where it STARTS, so a multi-line string literal
                // keeps its opening line.
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

                    if (word == "REF" && i < text.Length && text[i] == '=')
                    {
                        i++;
                        tokens.Add(new Token(TokenType.RefAssign, "REF=", tokenLine));
                        continue;
                    }

                    if ((word == "REAL" || word == "LREAL") && i < text.Length && text[i] == '#')
                    {
                        i++; // '#'
                        var numStart = i;
                        while (i < text.Length && char.IsDigit(text[i]))
                            i++;
                        ConsumeFraction(text, ref i);
                        var numText = text.Substring(numStart, i - numStart);
                        tokens.Add(new Token(word == "REAL" ? TokenType.RealLiteral : TokenType.LrealLiteral, numText, tokenLine));
                        continue;
                    }

                    if ((word == "LTIME" || word == "LT") && i < text.Length && text[i] == '#')
                    {
                        i++; // '#'
                        var durStart = i;
                        while (i < text.Length && char.IsLetterOrDigit(text[i]))
                            i++;
                        tokens.Add(new Token(TokenType.LtimeLiteral, text.Substring(durStart, i - durStart), tokenLine));
                        continue;
                    }

                    if ((word == "TIME" || word == "T") && i < text.Length && text[i] == '#')
                    {
                        i++; // '#'
                        var durStart = i;
                        while (i < text.Length && char.IsLetterOrDigit(text[i]))
                            i++;
                        tokens.Add(new Token(TokenType.TimeLiteral, text.Substring(durStart, i - durStart), tokenLine));
                        continue;
                    }

                    // DATE/DATE_AND_TIME/TIME_OF_DAY (TcXunit-gd2.13): calendar/
                    // clock literals, not TIME's duration-segment grammar, so
                    // their body is a run of digits/'-'/':'/'.' rather than
                    // digits+unit-letters.
                    if ((word == "DATE_AND_TIME" || word == "DT") && i < text.Length && text[i] == '#')
                    {
                        i++; // '#'
                        var litStart = i;
                        while (i < text.Length && IsDateTimeLiteralChar(text[i]))
                            i++;
                        tokens.Add(new Token(TokenType.DateAndTimeLiteral, text.Substring(litStart, i - litStart), tokenLine));
                        continue;
                    }

                    if ((word == "TIME_OF_DAY" || word == "TOD") && i < text.Length && text[i] == '#')
                    {
                        i++; // '#'
                        var litStart = i;
                        while (i < text.Length && IsDateTimeLiteralChar(text[i]))
                            i++;
                        tokens.Add(new Token(TokenType.TimeOfDayLiteral, text.Substring(litStart, i - litStart), tokenLine));
                        continue;
                    }

                    if ((word == "DATE" || word == "D") && i < text.Length && text[i] == '#')
                    {
                        i++; // '#'
                        var litStart = i;
                        while (i < text.Length && IsDateTimeLiteralChar(text[i]))
                            i++;
                        tokens.Add(new Token(TokenType.DateLiteral, text.Substring(litStart, i - litStart), tokenLine));
                        continue;
                    }

                    tokens.Add(new Token(TokenType.Identifier, word, tokenLine));
                    continue;
                }

                if (char.IsDigit(c))
                {
                    var start = i;
                    while (i < text.Length && char.IsDigit(text[i]))
                        i++;

                    // IEC 61131-3 §2.4.2 based literal: <base>#<digits>, e.g.
                    // 16#ABCD (hex), 8#17 (octal), 2#1010 (binary), with
                    // optional '_' digit separators (TcXunit-nsm).
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

                // STRING literals use '...' and WSTRING literals use "..."
                // (TcXunit-gd2.4) - both produce the same StringLiteral
                // token since Cell values are plain C# strings regardless
                // of the variable's declared STRING/WSTRING type.
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
                    // every AND-aware parser/evaluator path (including
                    // AND_THEN's short-circuit precedence) handles it with
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
                        throw new FormatException($"Unexpected character '{c}' at position {i} in: {text}");
                }
            }

            // Eof sits at the very end of the body, so fold whatever trailing
            // whitespace/comment tail is left before stamping it.
            CountLineBreaks(text, ref counted, text.Length, ref line);
            tokens.Add(new Token(TokenType.Eof, string.Empty, line));
            return tokens;
        }

        // Folds the line breaks in text[counted..end) into line, advancing
        // counted to end (TcXunit-p3t.2). "\r\n" counts as ONE break -
        // TwinCAT writes CRLF into .TcPOU bodies while the fixtures on disk
        // are LF, and both must yield the same line numbers - as does a bare
        // "\n" or a lone "\r".
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

        // Recognizes IEC 61131-3 '$'-escape sequences inside single-quoted STRING
        // or double-quoted WSTRING literals (e.g. $$ -> '$', $' -> '\'' (or
        // $" -> '"' for WSTRING), $hh -> two-hex-digit character code). quote
        // is the literal's own delimiter ('\'' or '"'), so $<quote> escapes
        // that literal's embedded quote character. On a match, advances i past
        // the escape sequence and returns the decoded character; returns null
        // (and leaves i untouched) if text[i..] is not a recognized escape, so
        // the caller falls back to treating '$' as a literal character.
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

        // Returns true if c is 0-9, a-f, or A-F.
        private static bool IsHexDigit(char c)
            => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        // Char class for the body of D#/DT#/TOD# literals (TcXunit-gd2.13):
        // digits plus the date/time separators '-', ':', '.' - no letters,
        // unlike TIME's digits+unit-letters body.
        private static bool IsDateTimeLiteralChar(char c)
            => char.IsDigit(c) || c == '-' || c == ':' || c == '.';

        // Decodes an IEC 61131-3 §2.4.2 based-literal digit run (already
        // stripped of '_' separators) for the given base (2, 8, or 16 -
        // the only bases the standard defines; TcXunit-nsm). Letters A-Z/a-z
        // count as digits 10-35 so callers get a clear "invalid digit"
        // error rather than silent misparsing for out-of-range digits.
        private static long ParseBasedLiteral(string digits, int numberBase, string text, int position)
        {
            if (numberBase != 2 && numberBase != 8 && numberBase != 16)
                throw new FormatException($"Unsupported based-literal base '{numberBase}#' at position {position} in: {text}");

            if (digits.Length == 0)
                throw new FormatException($"Based literal has no digits after '#' at position {position} in: {text}");

            long value = 0;
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
                    throw new FormatException($"Digit '{ch}' is invalid for base {numberBase} at position {position} in: {text}");

                value = (value * numberBase) + digitValue;
            }

            return value;
        }

        // Consumes an optional '.digits' fraction and/or '[eE][+-]digits' exponent
        // starting at i, advancing i past whatever it consumes. Returns true if
        // anything real-valued (fraction and/or exponent) was found.
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
