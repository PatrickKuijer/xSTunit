using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // An integer literal takes the narrowest box that holds it (int, then
    // long, then ulong), independent of the declared type it is assigned to.
    // These tests pin both halves of that rule: a literal wide enough to need
    // 64 bits reaches a LINT/ULINT/LWORD intact, and a literal that already
    // fit an int keeps boxing as int so nothing downstream widens.
    public class WideIntegerLiteralTests
    {
        private static Engine NewEngine() => new Engine(new TypeRegistry(Array.Empty<PouAst>()));

        private static Frame NewFrame() => new Frame(new FbInstance("Test"), "Test");

        private static FbInstance NewInstanceWithVar(string declarations)
        {
            var pou = new PouAst("FB_Wide", null, "VAR\n" + declarations + "\nEND_VAR", "", new List<MethodAst>());
            return new Engine(new TypeRegistry(new[] { pou })).NewInstance("FB_Wide");
        }

        // The regression the whole rule exists for: every literal used to be
        // parsed at int width, so this one escaped as an OverflowException out
        // of int.Parse before it could reach the LINT it initialises.
        [Fact]
        public void NewInstance_LintInitialisedFromHexLiteralWiderThanInt_KeepsAllSixtyFourBits()
        {
            var instance = NewInstanceWithVar("\tvalue : LINT := 16#0102030405060708;");

            Assert.IsType<long>(instance.Fields["value"].Value);
            Assert.Equal(0x0102030405060708L, instance.Fields["value"].Value);
        }

        [Fact]
        public void NewInstance_LintInitialisedFromDecimalLiteralWiderThanInt_KeepsAllSixtyFourBits()
        {
            var instance = NewInstanceWithVar("\tvalue : LINT := 72623859790382856;");

            Assert.IsType<long>(instance.Fields["value"].Value);
            Assert.Equal(72623859790382856L, instance.Fields["value"].Value);
        }

        // ULINT and LWORD box as ulong, and their top half of the range is
        // above long.MaxValue - a literal there is representable in 64 bits but
        // not as a signed long, so the width rule has to reach past long.
        [Fact]
        public void NewInstance_UlintInitialisedFromLiteralAboveLongMaxValue_KeepsTheUnsignedValue()
        {
            var instance = NewInstanceWithVar("\tvalue : ULINT := 16#FFFFFFFFFFFFFFFF;");

            Assert.IsType<ulong>(instance.Fields["value"].Value);
            Assert.Equal(ulong.MaxValue, instance.Fields["value"].Value);
        }

        [Fact]
        public void NewInstance_LwordInitialisedFromDecimalLiteralAboveLongMaxValue_KeepsTheUnsignedValue()
        {
            var instance = NewInstanceWithVar("\tvalue : LWORD := 18446744073709551615;");

            Assert.IsType<ulong>(instance.Fields["value"].Value);
            Assert.Equal(ulong.MaxValue, instance.Fields["value"].Value);
        }

        // The literal takes the narrowest box that holds it, so a value above
        // long.MaxValue arrives as a ulong even where the declaration says
        // LINT. It has no LINT representation, so the initializer is rejected
        // naming both type groups: storing it would leave a ulong sitting in a
        // cell every later 'is long' test reads as not-a-LINT.
        [Fact]
        public void NewInstance_LintInitialisedFromLiteralAboveLongMaxValue_ThrowsNamingBothTypeGroups()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => NewInstanceWithVar("\tvalue : LINT := 16#FFFFFFFFFFFFFFFF;"));

            Assert.Contains("ULINT/LWORD", ex.Message);
            Assert.Contains("LINT/UDINT/DWORD", ex.Message);
        }

        // A negative literal is unary minus over a positive one, so a wide
        // negative LINT initializer only works if negation reaches past int.
        [Fact]
        public void NewInstance_LintInitialisedFromNegativeLiteralWiderThanInt_KeepsTheNegativeValue()
        {
            var instance = NewInstanceWithVar("\tvalue : LINT := -5000000000;");

            Assert.IsType<long>(instance.Fields["value"].Value);
            Assert.Equal(-5000000000L, instance.Fields["value"].Value);
        }

        // long.MinValue is the one LINT value whose magnitude does not fit a
        // long, so the literal parser can only hand it over unsigned - if
        // negation refused that box, no source text could produce it.
        [Fact]
        public void NewInstance_LintInitialisedFromLongMinValue_KeepsTheNegativeValue()
        {
            var instance = NewInstanceWithVar("\tvalue : LINT := -9223372036854775808;");

            Assert.IsType<long>(instance.Fields["value"].Value);
            Assert.Equal(long.MinValue, instance.Fields["value"].Value);
        }

        [Fact]
        public void ParseExpression_LiteralWiderThanInt_ProducesLintLiteralExpr()
        {
            var lit = Assert.IsType<LintLiteralExpr>(Parser.ParseExpression("16#0102030405060708"));

            Assert.Equal(0x0102030405060708L, lit.Value);
        }

        [Fact]
        public void ParseExpression_LiteralAboveLongMaxValue_ProducesUlintLiteralExpr()
        {
            var lit = Assert.IsType<UlintLiteralExpr>(Parser.ParseExpression("16#FFFFFFFFFFFFFFFF"));

            Assert.Equal(ulong.MaxValue, lit.Value);
        }

        // The boundaries of the widest-fits rule: int.MaxValue is the last
        // value that still boxes as int, long.MaxValue the last that still
        // boxes as long. A literal one past either boundary steps up a box
        // rather than wrapping or throwing.
        [Theory]
        [InlineData("2147483647", typeof(int))]
        [InlineData("2147483648", typeof(long))]
        [InlineData("9223372036854775807", typeof(long))]
        [InlineData("9223372036854775808", typeof(ulong))]
        [InlineData("18446744073709551615", typeof(ulong))]
        public void Evaluate_IntegerLiteralAtWidthBoundary_BoxesAtTheNarrowestFittingWidth(string source, Type expectedBox)
        {
            var value = NewEngine().Evaluate(Parser.ParseExpression(source), NewFrame());

            Assert.IsType(expectedBox, value);
        }

        // The rule must not widen anything that already worked: an ordinary
        // small literal still boxes as int, so INT arithmetic and every
        // NumericCoercion path keyed off a boxed int behave exactly as before.
        [Theory]
        [InlineData("0")]
        [InlineData("1")]
        [InlineData("42")]
        [InlineData("16#FF")]
        [InlineData("2#1010")]
        [InlineData("8#17")]
        public void Evaluate_SmallIntegerLiteral_StillBoxesAsInt(string source)
        {
            var expr = Parser.ParseExpression(source);

            Assert.IsType<IntLiteralExpr>(expr);
            Assert.IsType<int>(NewEngine().Evaluate(expr, NewFrame()));
        }

        // A literal past 64 bits has no IEC type to live in, so it is a parse
        // error - and the message names the literal, rather than letting a bare
        // OverflowException out of the number parser say nothing about which
        // literal was too wide.
        [Fact]
        public void ParseExpression_DecimalLiteralWiderThanSixtyFourBits_ThrowsNamingTheLiteral()
        {
            var ex = Assert.Throws<ParseException>(() => Parser.ParseExpression("18446744073709551616"));

            Assert.Contains("18446744073709551616", ex.Message);
            Assert.Equal("18446744073709551616", ex.Token);
        }

        [Fact]
        public void ParseExpression_BasedLiteralWiderThanSixtyFourBits_ThrowsNamingTheLiteral()
        {
            var ex = Assert.Throws<ParseException>(() => Parser.ParseExpression("16#1FFFFFFFFFFFFFFFF"));

            Assert.Contains("16#1FFFFFFFFFFFFFFFF", ex.Message);
        }

        // A based literal that overflowed 64 bits used to wrap silently in the
        // lexer's unchecked accumulator, so 16#FFFFFFFFFFFFFFFFF (17 digits)
        // arrived as a plausible-looking small number instead of an error.
        [Fact]
        public void Tokenize_BasedLiteralWiderThanSixtyFourBits_DoesNotWrapSilently()
        {
            Assert.Throws<ParseException>(() => Lexer.Tokenize("x := 16#FFFFFFFFFFFFFFFFF;"));
        }
    }
}
