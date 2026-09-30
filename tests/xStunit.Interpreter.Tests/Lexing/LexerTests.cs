using System;
using xStunit.Interpreter;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    public class LexerTests
    {
        [Fact]
        public void Tokenize_DollarEscapedQuote_ProducesEmbeddedSingleQuotes()
        {
            var tokens = Lexer.Tokenize("'Failed to find test $'%s$''");

            var literal = Assert.Single(tokens, t => t.Type == TokenType.StringLiteral);
            Assert.Equal("Failed to find test '%s'", literal.Text);
        }

        [Theory]
        [InlineData("'$'s widget'", "'s widget")]
        [InlineData("'the widget$'s heartbeat'", "the widget's heartbeat")]
        [InlineData("'the widget heartbeat$''", "the widget heartbeat'")]
        public void Tokenize_DollarEscapedQuote_AtStartMiddleAndEnd_ProducesEmbeddedSingleQuote(string source, string expected)
        {
            var tokens = Lexer.Tokenize(source);

            var literal = Assert.Single(tokens, t => t.Type == TokenType.StringLiteral);
            Assert.Equal(expected, literal.Text);
        }

        [Fact]
        public void Tokenize_DoubleDollar_ProducesLiteralDollarSign()
        {
            var tokens = Lexer.Tokenize("'Price: $$5'");

            var literal = Assert.Single(tokens, t => t.Type == TokenType.StringLiteral);
            Assert.Equal("Price: $5", literal.Text);
        }

        [Theory]
        [InlineData("$L", "\n")]
        [InlineData("$l", "\n")]
        [InlineData("$N", "\n")]
        [InlineData("$n", "\n")]
        [InlineData("$P", "\f")]
        [InlineData("$p", "\f")]
        [InlineData("$R", "\r")]
        [InlineData("$r", "\r")]
        [InlineData("$T", "\t")]
        [InlineData("$t", "\t")]
        public void Tokenize_DollarControlEscape_ProducesControlCharacter(string escape, string expected)
        {
            var tokens = Lexer.Tokenize($"'a{escape}b'");

            var literal = Assert.Single(tokens, t => t.Type == TokenType.StringLiteral);
            Assert.Equal($"a{expected}b", literal.Text);
        }

        [Theory]
        [InlineData("$41", 'A')]
        [InlineData("$4A", 'J')]
        [InlineData("$4a", 'J')]
        [InlineData("$09", '\t')]
        public void Tokenize_DollarHexEscape_ProducesCharacterWithGivenCode(string escape, char expected)
        {
            var tokens = Lexer.Tokenize($"'a{escape}b'");

            var literal = Assert.Single(tokens, t => t.Type == TokenType.StringLiteral);
            Assert.Equal($"a{expected}b", literal.Text);
        }

        [Fact]
        public void Tokenize_DollarFollowedByNonHexDigits_IsLiteralDollar()
        {
            var tokens = Lexer.Tokenize("'a$zzb'");

            var literal = Assert.Single(tokens, t => t.Type == TokenType.StringLiteral);
            Assert.Equal("a$zzb", literal.Text);
        }

        [Fact]
        public void Tokenize_DollarHexEscapeTruncatedByClosingQuote_IsLiteralDollar()
        {
            var tokens = Lexer.Tokenize("'a$4'");

            var literal = Assert.Single(tokens, t => t.Type == TokenType.StringLiteral);
            Assert.Equal("a$4", literal.Text);
        }

        [Fact]
        public void Tokenize_StringLiteralWithoutEscapes_IsUnaffected()
        {
            var tokens = Lexer.Tokenize("'plain text'");

            var literal = Assert.Single(tokens, t => t.Type == TokenType.StringLiteral);
            Assert.Equal("plain text", literal.Text);
        }

        // A double-quoted literal is a WSTRING, where STRING uses '...'; the
        // lexer deliberately collapses both onto one token type.
        [Fact]
        public void Tokenize_DoubleQuotedLiteral_ProducesStringLiteralToken()
        {
            var tokens = Lexer.Tokenize("\"wide text\"");

            var literal = Assert.Single(tokens, t => t.Type == TokenType.StringLiteral);
            Assert.Equal("wide text", literal.Text);
        }

        [Fact]
        public void Tokenize_DoubleQuotedLiteral_DollarEscapedQuote_ProducesEmbeddedDoubleQuotes()
        {
            var tokens = Lexer.Tokenize("\"Failed to find test $\"%s$\"\"");

            var literal = Assert.Single(tokens, t => t.Type == TokenType.StringLiteral);
            Assert.Equal("Failed to find test \"%s\"", literal.Text);
        }

        [Fact]
        public void Tokenize_DoubleQuotedLiteral_DoesNotTreatEmbeddedSingleQuoteAsDelimiter()
        {
            var tokens = Lexer.Tokenize("\"it's wide\"");

            var literal = Assert.Single(tokens, t => t.Type == TokenType.StringLiteral);
            Assert.Equal("it's wide", literal.Text);
        }

        // IEC 61131-3 §2.4.2 based literals: <base>#<digits>. The lexer folds
        // them to decimal, so nothing downstream ever sees the base notation.
        [Theory]
        [InlineData("16#ABCD", "43981")]
        [InlineData("16#abcd", "43981")]
        [InlineData("16#0", "0")]
        [InlineData("8#17", "15")]
        [InlineData("2#1010", "10")]
        [InlineData("16#1234_ABCD", "305441741")]
        public void Tokenize_BasedLiteral_ProducesIntLiteralToken(string literal, string expectedDecimal)
        {
            var tokens = Lexer.Tokenize(literal);

            var token = Assert.Single(tokens, t => t.Type == TokenType.IntLiteral);
            Assert.Equal(expectedDecimal, token.Text);
        }

        [Fact]
        public void Tokenize_BasedLiteralInAssignment_ProducesIntLiteralToken()
        {
            var tokens = Lexer.Tokenize("nChecksum := 16#ABCD;");

            var token = Assert.Single(tokens, t => t.Type == TokenType.IntLiteral);
            Assert.Equal("43981", token.Text);
        }

        [Fact]
        public void Tokenize_BasedLiteralWithInvalidDigitForBase_Throws()
        {
            Assert.Throws<ParseException>(() => Lexer.Tokenize("2#1012"));
        }

        [Fact]
        public void Tokenize_BasedLiteralWithUnsupportedBase_Throws()
        {
            Assert.Throws<ParseException>(() => Lexer.Tokenize("10#123"));
        }

        [Theory]
        [InlineData("INT#5", "INT", "5")]
        [InlineData("int#5", "INT", "5")]
        [InlineData("Dint#16#FF", "DINT", "255")]
        [InlineData("BYTE#2#1010", "BYTE", "10")]
        [InlineData("WORD#8#17", "WORD", "15")]
        [InlineData("DWORD#16#FFFF_FFFF", "DWORD", "4294967295")]
        [InlineData("LWORD#16#FFFFFFFFFFFFFFFF", "LWORD", "18446744073709551615")]
        [InlineData("ULINT#18446744073709551615", "ULINT", "18446744073709551615")]
        [InlineData("INT#-5", "INT", "-5")]
        [InlineData("SINT#-128", "SINT", "-128")]
        [InlineData("LINT#-9223372036854775808", "LINT", "-9223372036854775808")]
        [InlineData("USINT#255", "USINT", "255")]
        [InlineData("UINT#65535", "UINT", "65535")]
        [InlineData("UDINT#4294967295", "UDINT", "4294967295")]
        [InlineData("SINT#127", "SINT", "127")]
        public void Tokenize_TypedIntegerLiteral_ProducesIntLiteralTokenCarryingTheNamedType(string literal, string typeName, string expectedDecimal)
        {
            var tokens = Lexer.Tokenize(literal);

            var token = Assert.Single(tokens, t => t.Type == TokenType.IntLiteral);
            Assert.Equal(expectedDecimal, token.Text);
            Assert.Equal(typeName, token.IecType);
        }

        [Fact]
        public void Tokenize_PlainIntegerLiteral_CarriesNoType()
        {
            var token = Assert.Single(Lexer.Tokenize("5"), t => t.Type == TokenType.IntLiteral);
            Assert.Null(token.IecType);
        }

        [Theory]
        [InlineData("BOOL#1", "TRUE")]
        [InlineData("bool#TRUE", "TRUE")]
        [InlineData("BOOL#true", "TRUE")]
        [InlineData("BOOL#0", "FALSE")]
        [InlineData("Bool#FALSE", "FALSE")]
        public void Tokenize_TypedBoolLiteral_ProducesTheBoolKeyword(string literal, string expectedKeyword)
        {
            var token = Assert.Single(Lexer.Tokenize(literal), t => t.Type == TokenType.Identifier);
            Assert.Equal(expectedKeyword, token.Text);
        }

        [Fact]
        public void Tokenize_TypedRangeOfLiterals_KeepsBothEndsAroundDotDot()
        {
            var tokens = Lexer.Tokenize("INT#1..INT#3");

            Assert.Equal(new[] { TokenType.IntLiteral, TokenType.DotDot, TokenType.IntLiteral, TokenType.Eof },
                tokens.ConvertAll(t => t.Type).ToArray());
        }

        // An out-of-range typed literal must fail at lex time and name the
        // literal, so a value TwinCAT would reject at compile time is never
        // silently truncated at run time.
        [Theory]
        [InlineData("SINT#200")]
        [InlineData("SINT#-129")]
        [InlineData("BYTE#256")]
        [InlineData("USINT#-1")]
        [InlineData("INT#32768")]
        [InlineData("UINT#65536")]
        [InlineData("DINT#2147483648")]
        [InlineData("UDINT#4294967296")]
        [InlineData("ULINT#-1")]
        [InlineData("LINT#9223372036854775808")]
        [InlineData("BYTE#16#100")]
        [InlineData("BOOL#2")]
        [InlineData("INT#")]
        public void Tokenize_TypedIntegerLiteralOutOfRangeOrMalformed_ThrowsNamingTheLiteral(string literal)
        {
            var ex = Assert.Throws<ParseException>(() => Lexer.Tokenize(literal));

            Assert.Contains(literal, ex.Message);
        }

        // E_Foo#Bar is not an integer prefix; the '#' after an arbitrary
        // identifier keeps its old failure rather than being swallowed.
        [Fact]
        public void Tokenize_HashAfterNonIntegerTypeName_StillThrows()
        {
            Assert.Throws<ParseException>(() => Lexer.Tokenize("E_Color#Red"));
        }
    }
}
