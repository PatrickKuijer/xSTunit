using System;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    public class BoolLiteralTests
    {
        private static Engine NewEngine() => new Engine(new TypeRegistry(Array.Empty<PouAst>()));

        private static Frame NewFrame() => new Frame(new FbInstance("Test"), "Test");

        [Theory]
        [InlineData("TRUE", true)]
        [InlineData("FALSE", false)]
        [InlineData("true", true)]
        [InlineData("False", false)]
        public void ParseExpression_BoolKeyword_ProducesBoolLiteralExpr(string source, bool expected)
        {
            var expr = Interpreter.Parser.ParseExpression(source);

            var lit = Assert.IsType<BoolLiteralExpr>(expr);
            Assert.Equal(expected, lit.Value);
        }

        [Fact]
        public void Evaluate_TrueLiteral_ReturnsBoxedTrue()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Interpreter.Parser.ParseExpression("TRUE"), NewFrame());

            Assert.IsType<bool>(result);
            Assert.True((bool)result);
        }

        [Fact]
        public void ExecuteStatements_AssignBoolLiteral_SetsCellValue()
        {
            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["x"] = new Cell { Value = false };

            engine.ExecuteStatements(Interpreter.Parser.ParseStatements("x := TRUE;"), frame);

            Assert.Equal(true, frame.Locals["x"].Value);
        }

        [Fact]
        public void Evaluate_BoolLiteralWithAndOr_CombinesCorrectly()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Interpreter.Parser.ParseExpression("TRUE AND NOT FALSE"), NewFrame());

            Assert.IsType<bool>(result);
            Assert.True((bool)result);
        }

        [Fact]
        public void ParseExpression_IdentifierPrefixedWithTrue_StillProducesIdentifierExpr()
        {
            var expr = Interpreter.Parser.ParseExpression("TrueValue");

            Assert.IsType<IdentifierExpr>(expr);
        }

        // Comparing two BOOLs must not fall through to the numeric path, whose
        // int cast turns an ordinary ST comparison into InvalidCastException.
        [Theory]
        [InlineData("TRUE = TRUE", true)]
        [InlineData("TRUE = FALSE", false)]
        [InlineData("TRUE <> FALSE", true)]
        [InlineData("FALSE <> FALSE", false)]
        public void Evaluate_BoolEqualityBetweenTwoBools_ReturnsBoxedBool(string source, bool expected)
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Interpreter.Parser.ParseExpression(source), NewFrame());

            Assert.IsType<bool>(result);
            Assert.Equal(expected, (bool)result);
        }

        // BOOL against a non-BOOL has no IEC 61131-3 meaning, so the failure
        // has to name the operator and both types - an InvalidCastException
        // from the numeric path leaves the author with nothing to go on.
        [Fact]
        public void Evaluate_BoolEqualsInt_ThrowsNotSupportedException()
        {
            var engine = NewEngine();

            var ex = Assert.Throws<NotSupportedException>(
                () => engine.Evaluate(Interpreter.Parser.ParseExpression("TRUE = 1"), NewFrame()));

            Assert.Contains("=", ex.Message);
            Assert.Contains("Boolean", ex.Message);
            Assert.Contains("Int32", ex.Message);
        }

        // The relational operators have no BOOL semantics either, so they take
        // the same descriptive failure rather than an InvalidCastException.
        [Fact]
        public void Evaluate_BoolLessThanBool_ThrowsNotSupportedException()
        {
            var engine = NewEngine();

            var ex = Assert.Throws<NotSupportedException>(
                () => engine.Evaluate(Interpreter.Parser.ParseExpression("TRUE < FALSE"), NewFrame()));

            Assert.Contains("<", ex.Message);
            Assert.Contains("Boolean", ex.Message);
        }

        // The guard above must stay narrow: the logical operators are the
        // common, legitimate BOOL usage and must not be caught by it.
        [Fact]
        public void Evaluate_BoolOrBool_StillWorks()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Interpreter.Parser.ParseExpression("FALSE OR TRUE"), NewFrame());

            Assert.IsType<bool>(result);
            Assert.True((bool)result);
        }
    }
}
