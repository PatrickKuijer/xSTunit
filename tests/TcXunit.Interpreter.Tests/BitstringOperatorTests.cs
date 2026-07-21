using System;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-w5x.15.2: MOD (integer-only) and bitstring AND/OR/XOR/NOT on
    // INT/BOOL operands.
    public class BitstringOperatorTests
    {
        private static Engine NewEngine() => new Engine(new TypeRegistry(Array.Empty<PouAst>()));

        private static Frame NewFrame() => new Frame(new FbInstance("Test"), "Test");

        [Fact]
        public void Evaluate_Mod_ReturnsRemainder()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("7 MOD 3"), NewFrame());

            Assert.Equal(1, result);
        }

        [Fact]
        public void Evaluate_Mod_OnRealOperand_Throws()
        {
            var engine = NewEngine();

            Assert.Throws<NotSupportedException>(() =>
                engine.Evaluate(Parser.ParseExpression("REAL#7.0 MOD 3"), NewFrame()));
        }

        [Fact]
        public void Evaluate_AndOnInt_ReturnsBitwiseAnd()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("6 AND 3"), NewFrame());

            Assert.Equal(2, result);
        }

        [Fact]
        public void Evaluate_OrOnInt_ReturnsBitwiseOr()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("6 OR 1"), NewFrame());

            Assert.Equal(7, result);
        }

        [Fact]
        public void Evaluate_XorOnInt_ReturnsBitwiseXor()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("6 XOR 3"), NewFrame());

            Assert.Equal(5, result);
        }

        [Fact]
        public void Evaluate_NotOnInt_ReturnsBitwiseComplement()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("NOT 0"), NewFrame());

            Assert.Equal(~0, result);
        }

        [Fact]
        public void Evaluate_AndOnBoolVariables_ReturnsLogicalAnd()
        {
            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["a"] = new Cell { Value = true };
            frame.Locals["b"] = new Cell { Value = false };

            var result = engine.Evaluate(Parser.ParseExpression("a AND b"), frame);

            Assert.Equal(false, result);
        }

        [Fact]
        public void Evaluate_OrOnBoolVariables_ReturnsLogicalOr()
        {
            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["a"] = new Cell { Value = true };
            frame.Locals["b"] = new Cell { Value = false };

            var result = engine.Evaluate(Parser.ParseExpression("a OR b"), frame);

            Assert.Equal(true, result);
        }

        [Fact]
        public void Evaluate_XorOnBoolVariables_ReturnsLogicalXor()
        {
            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["a"] = new Cell { Value = true };
            frame.Locals["b"] = new Cell { Value = true };

            var result = engine.Evaluate(Parser.ParseExpression("a XOR b"), frame);

            Assert.Equal(false, result);
        }

        [Fact]
        public void Evaluate_NotOnBoolVariable_ReturnsLogicalNegation()
        {
            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["a"] = new Cell { Value = true };

            var result = engine.Evaluate(Parser.ParseExpression("NOT a"), frame);

            Assert.Equal(false, result);
        }

        [Fact]
        public void Evaluate_AndOnMismatchedOperandTypes_Throws()
        {
            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["a"] = new Cell { Value = true };

            Assert.Throws<NotSupportedException>(() =>
                engine.Evaluate(Parser.ParseExpression("a AND 1"), frame));
        }

        [Fact]
        public void Evaluate_NotOnRealOperand_Throws()
        {
            var engine = NewEngine();

            Assert.Throws<NotSupportedException>(() =>
                engine.Evaluate(Parser.ParseExpression("NOT REAL#1.0"), NewFrame()));
        }

        [Fact]
        public void Evaluate_OperatorPrecedence_ModBindsTighterThanAdditive()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("1 + 7 MOD 3"), NewFrame());

            Assert.Equal(2, result);
        }

        [Fact]
        public void Evaluate_OperatorPrecedence_AndBindsTighterThanOr()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("6 OR 1 AND 0"), NewFrame());

            Assert.Equal(6, result);
        }
    }
}
