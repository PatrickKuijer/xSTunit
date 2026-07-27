using System;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-nfd: TRUE/FALSE as literal boolean expressions, parallel to
    // Int/Real/Time literal handling.
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

        // TcXunit-vwe: BOOL = / <> between two BOOL operands must return the
        // boxed comparison result, not fall through to the int-cast numeric
        // path (which would throw InvalidCastException).
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

        // TcXunit-80v: BOOL mixed with a non-BOOL operand (INT here) has no
        // valid IEC 61131-3 semantics for '=' - it must throw a descriptive
        // NotSupportedException, not fall through to the int-cast numeric
        // path and throw an unhelpful InvalidCastException.
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

        // TcXunit-80v: relational operators (<, >, <=, >=) have no valid
        // IEC 61131-3 semantics for BOOL operands - must throw a descriptive
        // NotSupportedException rather than InvalidCastException.
        [Fact]
        public void Evaluate_BoolLessThanBool_ThrowsNotSupportedException()
        {
            var engine = NewEngine();

            var ex = Assert.Throws<NotSupportedException>(
                () => engine.Evaluate(Interpreter.Parser.ParseExpression("TRUE < FALSE"), NewFrame()));

            Assert.Contains("<", ex.Message);
            Assert.Contains("Boolean", ex.Message);
        }

        // Legitimate BOOL usage (AND/OR logical ops) must keep working - the
        // TcXunit-80v guard should only fire for BOOL-with-non-BOOL or a BOOL
        // operator with no BOOL semantics, not for these.
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
