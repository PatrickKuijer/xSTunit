using System.Linq;
using xStunit.Interpreter;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // Token.Line is 1-based within the string handed to Tokenize - a method's
    // ST body - never within the enclosing source file, so nothing here should
    // reason about where that body sits in the file.
    public class LexerLineNumberTests
    {
        [Fact]
        public void Tokenize_MultiLineSnippet_StampsEachTokenWithItsOwnLine()
        {
            var tokens = Lexer.Tokenize("x := 1;\ny := 2;\n\nz := 3;");

            Assert.Equal(1, LineOfIdentifier(tokens, "x"));
            Assert.Equal(2, LineOfIdentifier(tokens, "y"));
            Assert.Equal(4, LineOfIdentifier(tokens, "z"));
        }

        [Fact]
        public void Tokenize_FirstTokenOfBody_IsLineOne()
        {
            var tokens = Lexer.Tokenize("value := 1;");

            Assert.Equal(1, tokens[0].Line);
        }

        [Fact]
        public void Tokenize_LeadingBlankLines_ShiftFirstTokensLine()
        {
            var tokens = Lexer.Tokenize("\n\nvalue := 1;");

            Assert.Equal(3, LineOfIdentifier(tokens, "value"));
        }

        [Fact]
        public void Tokenize_CrLfLineBreak_CountsAsSingleBreak()
        {
            // TwinCAT writes CRLF into POU bodies while the fixtures on disk
            // are LF; if CRLF counted twice, every line number reported for a
            // real project would drift further off with each line.
            var tokens = Lexer.Tokenize("a := 1;\r\nb := 2;\r\nc := 3;");

            Assert.Equal(1, LineOfIdentifier(tokens, "a"));
            Assert.Equal(2, LineOfIdentifier(tokens, "b"));
            Assert.Equal(3, LineOfIdentifier(tokens, "c"));
        }

        [Fact]
        public void Tokenize_MixedCrLfAndLf_CountsEachBreakOnce()
        {
            var tokens = Lexer.Tokenize("a := 1;\r\nb := 2;\nc := 3;\r\nd := 4;");

            Assert.Equal(2, LineOfIdentifier(tokens, "b"));
            Assert.Equal(3, LineOfIdentifier(tokens, "c"));
            Assert.Equal(4, LineOfIdentifier(tokens, "d"));
        }

        [Fact]
        public void Tokenize_MultiLineBlockComment_AdvancesLineCounterAcrossIt()
        {
            var tokens = Lexer.Tokenize("a := 1;\n(* comment line 2\n   comment line 3 *)\nb := 2;");

            Assert.Equal(1, LineOfIdentifier(tokens, "a"));
            Assert.Equal(4, LineOfIdentifier(tokens, "b"));
        }

        [Fact]
        public void Tokenize_MultiLineBlockCommentWithCrLf_AdvancesLineCounterAcrossIt()
        {
            var tokens = Lexer.Tokenize("a := 1;\r\n(* comment\r\n   comment *)\r\nb := 2;");

            Assert.Equal(4, LineOfIdentifier(tokens, "b"));
        }

        [Fact]
        public void Tokenize_LineComment_LeavesFollowingTokenOnNextLine()
        {
            var tokens = Lexer.Tokenize("a := 1; // trailing note\nb := 2;");

            Assert.Equal(2, LineOfIdentifier(tokens, "b"));
        }

        [Fact]
        public void Tokenize_MultiLineStringLiteral_StampsLineWhereLiteralStarts()
        {
            var tokens = Lexer.Tokenize("a := 1;\nmsg := 'first\nsecond';\nb := 2;");

            var literal = Assert.Single(tokens, t => t.Type == TokenType.StringLiteral);
            Assert.Equal(2, literal.Line);

            // A break inside a literal still counts as a break, so everything
            // after it keeps the line numbering the author would expect.
            Assert.Equal(4, LineOfIdentifier(tokens, "b"));
        }

        [Fact]
        public void Tokenize_EofToken_CarriesLastLine()
        {
            var tokens = Lexer.Tokenize("a := 1;\nb := 2;");

            Assert.Equal(2, tokens[tokens.Count - 1].Line);
        }

        [Fact]
        public void Tokenize_TokensNeverCarryLineZero()
        {
            var tokens = Lexer.Tokenize(
                "IF a > 0 THEN\n" +
                "\tfb(IN := TRUE, PT := T#100ms);\n" +
                "\tarr[1] := REAL#1.5;\n" +
                "END_IF");

            Assert.All(tokens, t => Assert.True(t.Line >= 1, $"token {t} has no line"));
        }

        private static int LineOfIdentifier(System.Collections.Generic.List<Token> tokens, string name) =>
            tokens.Single(t => t.Type == TokenType.Identifier && t.Text == name).Line;
    }
}
