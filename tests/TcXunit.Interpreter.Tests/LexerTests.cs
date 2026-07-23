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

        [Fact]
        public void Tokenize_StringLiteralWithoutEscapes_IsUnaffected()
        {
            var tokens = Lexer.Tokenize("'plain text'");

            var literal = Assert.Single(tokens, t => t.Type == TokenType.StringLiteral);
            Assert.Equal("plain text", literal.Text);
        }
    }
}
