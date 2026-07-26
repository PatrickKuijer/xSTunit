using System;
using System.Collections.Generic;
using System.Text;

namespace TcXunit.Interpreter
{
    // Tokenizes the ST statement subset the fixture actually uses
    // (TcXunit-w5x.8/.12). No unary minus, no real/string escapes, no
    // hex literals - grow-on-demand as new fixture bodies need them.
    public static class Lexer
    {
        public static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            var i = 0;

            while (i < text.Length)
            {
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
                        tokens.Add(new Token(TokenType.RefAssign, "REF="));
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
                        tokens.Add(new Token(word == "REAL" ? TokenType.RealLiteral : TokenType.LrealLiteral, numText));
                        continue;
                    }

                    if ((word == "LTIME" || word == "LT") && i < text.Length && text[i] == '#')
                    {
                        i++; // '#'
                        var durStart = i;
                        while (i < text.Length && char.IsLetterOrDigit(text[i]))
                            i++;
                        tokens.Add(new Token(TokenType.LtimeLiteral, text.Substring(durStart, i - durStart)));
                        continue;
                    }

                    if ((word == "TIME" || word == "T") && i < text.Length && text[i] == '#')
                    {
                        i++; // '#'
                        var durStart = i;
                        while (i < text.Length && char.IsLetterOrDigit(text[i]))
                            i++;
                        tokens.Add(new Token(TokenType.TimeLiteral, text.Substring(durStart, i - durStart)));
                        continue;
                    }

                    tokens.Add(new Token(TokenType.Identifier, word));
                    continue;
                }

                if (char.IsDigit(c))
                {
                    var start = i;
                    while (i < text.Length && char.IsDigit(text[i]))
                        i++;
                    var isReal = ConsumeFraction(text, ref i);
                    tokens.Add(new Token(isReal ? TokenType.RealLiteral : TokenType.IntLiteral, text.Substring(start, i - start)));
                    continue;
                }

                if (c == '\'')
                {
                    var sb = new StringBuilder();
                    i++;
                    while (i < text.Length && text[i] != '\'')
                    {
                        if (text[i] == '$' && i + 1 < text.Length)
                        {
                            var escaped = TryConsumeDollarEscape(text, ref i);
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
                    tokens.Add(new Token(TokenType.StringLiteral, sb.ToString()));
                    continue;
                }

                if (c == ':' && i + 1 < text.Length && text[i + 1] == '=')
                {
                    tokens.Add(new Token(TokenType.Assign, ":="));
                    i += 2;
                    continue;
                }

                if (c == ':')
                {
                    tokens.Add(new Token(TokenType.Colon, ":"));
                    i++;
                    continue;
                }

                if (c == '.' && i + 1 < text.Length && text[i + 1] == '.')
                {
                    tokens.Add(new Token(TokenType.DotDot, ".."));
                    i += 2;
                    continue;
                }

                if (c == '<' && i + 1 < text.Length && text[i + 1] == '=')
                {
                    tokens.Add(new Token(TokenType.Le, "<="));
                    i += 2;
                    continue;
                }

                if (c == '>' && i + 1 < text.Length && text[i + 1] == '=')
                {
                    tokens.Add(new Token(TokenType.Ge, ">="));
                    i += 2;
                    continue;
                }

                if (c == '<' && i + 1 < text.Length && text[i + 1] == '>')
                {
                    tokens.Add(new Token(TokenType.Ne, "<>"));
                    i += 2;
                    continue;
                }

                if (c == '=' && i + 1 < text.Length && text[i + 1] == '>')
                {
                    tokens.Add(new Token(TokenType.Arrow, "=>"));
                    i += 2;
                    continue;
                }

                switch (c)
                {
                    case '=': tokens.Add(new Token(TokenType.Eq, "=")); i++; continue;
                    case '<': tokens.Add(new Token(TokenType.Lt, "<")); i++; continue;
                    case '>': tokens.Add(new Token(TokenType.Gt, ">")); i++; continue;
                    case '+': tokens.Add(new Token(TokenType.Plus, "+")); i++; continue;
                    case '-': tokens.Add(new Token(TokenType.Minus, "-")); i++; continue;
                    case '*': tokens.Add(new Token(TokenType.Asterisk, "*")); i++; continue;
                    case '/': tokens.Add(new Token(TokenType.Slash, "/")); i++; continue;
                    case '^': tokens.Add(new Token(TokenType.Caret, "^")); i++; continue;
                    case '.': tokens.Add(new Token(TokenType.Dot, ".")); i++; continue;
                    case ',': tokens.Add(new Token(TokenType.Comma, ",")); i++; continue;
                    case ';': tokens.Add(new Token(TokenType.Semicolon, ";")); i++; continue;
                    case '(': tokens.Add(new Token(TokenType.LParen, "(")); i++; continue;
                    case ')': tokens.Add(new Token(TokenType.RParen, ")")); i++; continue;
                    case '[': tokens.Add(new Token(TokenType.LBracket, "[")); i++; continue;
                    case ']': tokens.Add(new Token(TokenType.RBracket, "]")); i++; continue;
                    default:
                        throw new FormatException($"Unexpected character '{c}' at position {i} in: {text}");
                }
            }

            tokens.Add(new Token(TokenType.Eof, string.Empty));
            return tokens;
        }

        // Recognizes IEC 61131-3 '$'-escape sequences inside single-quoted STRING
        // literals (e.g. $$ -> '$', $' -> '\'', $hh -> two-hex-digit character
        // code). On a match, advances i past the escape sequence and returns the
        // decoded character; returns null (and leaves i untouched) if text[i..]
        // is not a recognized escape, so the caller falls back to treating '$'
        // as a literal character.
        private static char? TryConsumeDollarEscape(string text, ref int i)
        {
            var next = text[i + 1];
            char? decoded = next switch
            {
                '$' => '$',
                '\'' => '\'',
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
