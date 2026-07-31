using System;
using TcXunit.Interpreter;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    public class LexerTests
    {
        [Fact]
        public void Tokenize_DollarEscapedQuote_ProducesEmbeddedSingleQuotes()
        {
            // TwinCAT/IEC 61131-3 '$'' escapes an embedded single-quote.
            var tokens = Lexer.Tokenize("'Failed to find test $'%s$''");

            var literal = Assert.Single(tokens, t => t.Type == TokenType.StringLiteral);
            Assert.Equal("Failed to find test '%s'", literal.Text);
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
            // Only one hex digit before the closing quote: not a valid $hh escape.
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

        // WSTRING literals use "..." rather than STRING's '...' (TcXunit-gd2.4).
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

        // IEC 61131-3 §2.4.2 based literals: <base>#<digits> (TcXunit-nsm).
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
    }
}
