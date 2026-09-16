using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // ABS is an IEC 61131-3 operator, not a POU, and it is overloaded over
    // ANY_NUM: the result keeps the argument's own type rather than widening
    // to LREAL. Its single input is named IN.
    public class AbsIntrinsicTests
    {
        private static (Engine Engine, FbInstance Instance, Frame Frame) NewHolder(string varBlock)
        {
            var fb = new PouAst("FB_Holder", null, varBlock, "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            return (engine, instance, new Frame(instance, "FB_Holder"));
        }

        private static object Eval(string varBlock, string expression)
        {
            var (engine, _, frame) = NewHolder(varBlock);
            return engine.Evaluate(Parser.ParseExpression(expression), frame);
        }

        [Fact]
        public void Abs_NegativeLreal_ReturnsPositiveLreal()
        {
            var result = Eval("VAR\n\tfValue : LREAL := -3.5;\nEND_VAR", "ABS(fValue)");

            Assert.Equal(3.5, Assert.IsType<double>(result));
        }

        [Fact]
        public void Abs_PositiveLreal_ReturnsSameLreal()
        {
            var result = Eval("VAR\n\tfValue : LREAL := 3.5;\nEND_VAR", "ABS(fValue)");

            Assert.Equal(3.5, Assert.IsType<double>(result));
        }

        [Fact]
        public void Abs_ZeroLreal_ReturnsZeroLreal()
        {
            var result = Eval("VAR\n\tfValue : LREAL := 0.0;\nEND_VAR", "ABS(fValue)");

            Assert.Equal(0.0, Assert.IsType<double>(result));
        }

        [Fact]
        public void Abs_NegativeReal_StaysReal()
        {
            var result = Eval("VAR\n\tfValue : REAL := -2.25;\nEND_VAR", "ABS(fValue)");

            Assert.Equal(2.25f, Assert.IsType<float>(result));
        }

        [Fact]
        public void Abs_NegativeInt_StaysInt()
        {
            var result = Eval("VAR\n\tnValue : INT := -7;\nEND_VAR", "ABS(nValue)");

            Assert.Equal(7, Assert.IsType<int>(result));
        }

        [Fact]
        public void Abs_NegativeLint_StaysLong()
        {
            var result = Eval("VAR\n\tnValue : LINT := -5000000000;\nEND_VAR", "ABS(nValue)");

            Assert.Equal(5000000000L, Assert.IsType<long>(result));
        }

        [Fact]
        public void Abs_Ulint_IsIdentity()
        {
            var result = Eval("VAR\n\tnValue : ULINT := 42;\nEND_VAR", "ABS(nValue)");

            Assert.Equal(42UL, Assert.IsType<ulong>(result));
        }

        [Fact]
        public void Abs_OfDifference_IsNotWidenedToLreal()
        {
            // The whole point of the per-box switch: an INT difference must
            // come back as an INT, not a double, or the next assignment or
            // assertion sees the wrong IEC type.
            var result = Eval("VAR\n\tnA : INT := 3;\n\tnB : INT := 10;\nEND_VAR", "ABS(nA - nB)");

            Assert.Equal(7, Assert.IsType<int>(result));
        }

        [Fact]
        public void Abs_NamedInArgument_Binds()
        {
            var result = Eval("VAR\n\tfValue : LREAL := -1.5;\nEND_VAR", "ABS(IN := fValue)");

            Assert.Equal(1.5, Assert.IsType<double>(result));
        }

        [Fact]
        public void Abs_MissingArgument_ThrowsRequiredArgumentException()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Eval("VAR\nEND_VAR", "ABS()"));

            Assert.Contains("IN", ex.Message);
        }

        [Fact]
        public void Abs_NonNumericArgument_Throws()
        {
            Assert.Throws<NotSupportedException>(() => Eval("VAR\nEND_VAR", "ABS('text')"));
        }

        [Fact]
        public void Abs_InToleranceComparison_OnLreals()
        {
            // The shape the PLC fixture actually uses:
            //   F_bInTolerance := ABS(ifAct - ifReq) <= ifTolerance;
            var varBlock = "VAR\n\tfAct : LREAL := 9.95;\n\tfReq : LREAL := 10.0;\n\tfTol : LREAL := 0.1;\nEND_VAR";

            Assert.Equal(true, Eval(varBlock, "ABS(fAct - fReq) <= fTol"));
        }
    }
}
