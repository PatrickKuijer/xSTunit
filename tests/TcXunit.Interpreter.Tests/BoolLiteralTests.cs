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
    }
}
