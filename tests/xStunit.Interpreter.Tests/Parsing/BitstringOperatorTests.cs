using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcXunit-w5x.15.2: MOD (integer-only) and bitstring AND/OR/XOR/NOT on
    // INT/BOOL operands.
    // TcXunit-rdz: extended with the short-circuit forms AND_THEN/OR_ELSE,
    // the '&' alias for AND, and DWORD-range (long-boxed) bitstring coverage.
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

        // TcXunit-rdz: '&' (IEC 61131-3 §2.4.5's alias for AND) on INT
        // (BYTE/WORD-range) and BOOL operands.
        [Fact]
        public void Evaluate_AmpersandOnInt_ReturnsBitwiseAnd()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("6 & 3"), NewFrame());

            Assert.Equal(2, result);
        }

        [Fact]
        public void Evaluate_AmpersandOnBoolVariables_ReturnsLogicalAnd()
        {
            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["a"] = new Cell { Value = true };
            frame.Locals["b"] = new Cell { Value = false };

            var result = engine.Evaluate(Parser.ParseExpression("a & b"), frame);

            Assert.Equal(false, result);
        }

        // TcXunit-rdz: AND/OR/XOR on DWORD-range (long-boxed) operands -
        // BYTE/WORD/INT box as C# int (already covered above), but DWORD
        // exceeds Int32 range and boxes as long (IecNumericType). Values here
        // stay within int range so the expected result is unambiguous, but
        // the operand Cells are long-typed to exercise the long/long
        // EvaluateBitstring branch a plain int literal test can't reach.
        [Fact]
        public void Evaluate_AndOnDwordVariables_ReturnsBitwiseAnd()
        {
            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["a"] = new Cell { Value = 6L };
            frame.Locals["b"] = new Cell { Value = 3L };

            var result = engine.Evaluate(Parser.ParseExpression("a AND b"), frame);

            Assert.Equal(2L, result);
        }

        [Fact]
        public void Evaluate_OrOnDwordVariables_ReturnsBitwiseOr()
        {
            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["a"] = new Cell { Value = 6L };
            frame.Locals["b"] = new Cell { Value = 1L };

            var result = engine.Evaluate(Parser.ParseExpression("a OR b"), frame);

            Assert.Equal(7L, result);
        }

        [Fact]
        public void Evaluate_XorOnDwordVariables_ReturnsBitwiseXor()
        {
            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["a"] = new Cell { Value = 6L };
            frame.Locals["b"] = new Cell { Value = 3L };

            var result = engine.Evaluate(Parser.ParseExpression("a XOR b"), frame);

            Assert.Equal(5L, result);
        }

        // TcXunit-rdz: AND_THEN/OR_ELSE (IEC 61131-3 §2.4.5 short-circuit
        // forms) on BOOL operands - value correctness for every truth-table
        // combination the short-circuit path can take.
        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, false)]
        public void Evaluate_AndThenOnBoolVariables_ReturnsLogicalAndThen(bool a, bool b, bool expected)
        {
            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["a"] = new Cell { Value = a };
            frame.Locals["b"] = new Cell { Value = b };

            var result = engine.Evaluate(Parser.ParseExpression("a AND_THEN b"), frame);

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, true)]
        [InlineData(false, true, true)]
        [InlineData(false, false, false)]
        public void Evaluate_OrElseOnBoolVariables_ReturnsLogicalOrElse(bool a, bool b, bool expected)
        {
            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["a"] = new Cell { Value = a };
            frame.Locals["b"] = new Cell { Value = b };

            var result = engine.Evaluate(Parser.ParseExpression("a OR_ELSE b"), frame);

            Assert.Equal(expected, result);
        }

        // TcXunit-rdz: AND_THEN/OR_ELSE have no short-circuit meaning for
        // bitstring operands (the standard defines the short-circuit forms
        // for BOOL only), so both sides are evaluated and the result falls
        // back to plain bitwise AND/OR - covering INT (BYTE/WORD-range) and
        // DWORD-range (long-boxed) operands.
        [Fact]
        public void Evaluate_AndThenOnInt_ReturnsBitwiseAnd()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("6 AND_THEN 3"), NewFrame());

            Assert.Equal(2, result);
        }

        [Fact]
        public void Evaluate_OrElseOnInt_ReturnsBitwiseOr()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("6 OR_ELSE 1"), NewFrame());

            Assert.Equal(7, result);
        }

        [Fact]
        public void Evaluate_AndThenOnDwordVariables_ReturnsBitwiseAnd()
        {
            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["a"] = new Cell { Value = 6L };
            frame.Locals["b"] = new Cell { Value = 3L };

            var result = engine.Evaluate(Parser.ParseExpression("a AND_THEN b"), frame);

            Assert.Equal(2L, result);
        }

        [Fact]
        public void Evaluate_OrElseOnDwordVariables_ReturnsBitwiseOr()
        {
            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["a"] = new Cell { Value = 6L };
            frame.Locals["b"] = new Cell { Value = 1L };

            var result = engine.Evaluate(Parser.ParseExpression("a OR_ELSE b"), frame);

            Assert.Equal(7L, result);
        }

        // TcXunit-rdz: mixed AND_THEN/OR_ELSE chain must associate the same
        // way as AND/OR (AND_THEN binds tighter than OR_ELSE), i.e.
        // 'a AND_THEN b OR_ELSE c' parses as '(a AND_THEN b) OR_ELSE c'.
        [Theory]
        [InlineData(true, true, false, true)] // (T AND_THEN T) OR_ELSE F = T OR_ELSE F = T
        [InlineData(false, true, true, true)] // (F AND_THEN T) OR_ELSE T = F OR_ELSE T = T
        [InlineData(false, true, false, false)] // (F AND_THEN T) OR_ELSE F = F OR_ELSE F = F
        public void Evaluate_MixedAndThenOrElseChain_AssociatesLikeAndOr(bool a, bool b, bool c, bool expected)
        {
            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["a"] = new Cell { Value = a };
            frame.Locals["b"] = new Cell { Value = b };
            frame.Locals["c"] = new Cell { Value = c };

            var result = engine.Evaluate(Parser.ParseExpression("a AND_THEN b OR_ELSE c"), frame);

            Assert.Equal(expected, result);
        }

        // TcXunit-rdz: the whole point of AND_THEN/OR_ELSE - the guard-then-
        // index idiom from the bug repro must genuinely skip evaluating the
        // RHS, not just parse it. saUsed is declared ARRAY[1..3] OF BOOL, so
        // saUsed[nSlot] with nSlot = 0 indexes out of bounds and throws
        // IndexOutOfRangeException *if evaluated*. A real short-circuit
        // never reaches that evaluation; eager (AND/OR-style) evaluation
        // would throw before the IF's THEN branch is even considered.
        private static Engine NewGuardEngine(out Frame frame)
        {
            var fb = new PouAst(
                "FB_Holder",
                null,
                "VAR\n\tnSlot : INT;\n\tsaUsed : ARRAY[1..3] OF BOOL;\n\tnResult : INT;\nEND_VAR",
                "",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            frame = new Frame(instance, "FB_Holder");
            return engine;
        }

        [Fact]
        public void ExecuteStatements_AndThenGuardFalse_SkipsOutOfRangeIndexEvaluation()
        {
            var engine = NewGuardEngine(out var frame);
            engine.ExecuteStatements(Parser.ParseStatements("nSlot := 0;"), frame);

            var ex = Record.Exception(() => engine.ExecuteStatements(
                Parser.ParseStatements(
                    "IF (nSlot > 0) AND_THEN saUsed[nSlot] THEN\n\tnResult := 1;\nEND_IF"),
                frame));

            Assert.Null(ex);
            Assert.Equal(0, ((Cell)frame.ResolveCell("nResult")).Value);
        }

        [Fact]
        public void ExecuteStatements_AndThenGuardTrue_EvaluatesRhsAndSetsResult()
        {
            var engine = NewGuardEngine(out var frame);
            engine.ExecuteStatements(Parser.ParseStatements("nSlot := 2;\nsaUsed[2] := TRUE;"), frame);

            engine.ExecuteStatements(
                Parser.ParseStatements(
                    "IF (nSlot > 0) AND_THEN saUsed[nSlot] THEN\n\tnResult := 1;\nEND_IF"),
                frame);

            Assert.Equal(1, ((Cell)frame.ResolveCell("nResult")).Value);
        }

        [Fact]
        public void ExecuteStatements_OrElseGuardTrue_SkipsOutOfRangeIndexEvaluation()
        {
            var engine = NewGuardEngine(out var frame);
            engine.ExecuteStatements(Parser.ParseStatements("nSlot := 0;"), frame);

            var ex = Record.Exception(() => engine.ExecuteStatements(
                Parser.ParseStatements(
                    "IF (nSlot = 0) OR_ELSE saUsed[nSlot] THEN\n\tnResult := 1;\nEND_IF"),
                frame));

            Assert.Null(ex);
            Assert.Equal(1, ((Cell)frame.ResolveCell("nResult")).Value);
        }
    }
}
