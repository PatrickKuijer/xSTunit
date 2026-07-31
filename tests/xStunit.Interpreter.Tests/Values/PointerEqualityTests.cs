using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // An unbound POINTER compares equal to 0 whatever numeric type the zero
    // literal is, which is what makes the ST null-check idiom
    // 'IF ipSrc = 0 THEN' work.
    public class PointerEqualityTests
    {
        private static (Engine Engine, FbInstance Instance, Frame Frame) NewHolder(string varBlock)
        {
            var fb = new PouAst("FB_Holder", null, varBlock, "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            return (engine, instance, new Frame(instance, "FB_Holder"));
        }

        [Fact]
        public void BoundPointer_ComparedToZero_IsNotEqual()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\tbuf : ARRAY[0..3] OF BYTE := [1,2,3,4];\n\tp : POINTER TO BYTE;\nEND_VAR");
            instance.Fields["p"].Value = engine.Evaluate(Parser.ParseExpression("ADR(buf)"), frame);

            Assert.False((bool)engine.Evaluate(Parser.ParseExpression("p = 0"), frame));
            Assert.True((bool)engine.Evaluate(Parser.ParseExpression("p <> 0"), frame));
        }

        [Fact]
        public void UnboundPointer_ComparedToZero_IsEqual()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\tp : POINTER TO BYTE;\nEND_VAR");

            Assert.True((bool)engine.Evaluate(Parser.ParseExpression("p = 0"), frame));
            Assert.False((bool)engine.Evaluate(Parser.ParseExpression("p <> 0"), frame));
        }

        [Fact]
        public void UnboundPointer_ComparedToRealZero_IsEqual()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\tp : POINTER TO BYTE;\nEND_VAR");

            Assert.True((bool)engine.Evaluate(Parser.ParseExpression("p = 0.0"), frame));
            Assert.False((bool)engine.Evaluate(Parser.ParseExpression("p <> 0.0"), frame));
        }

        [Fact]
        public void UnboundPointer_ComparedToLrealZero_IsEqual()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\tp : POINTER TO BYTE;\nEND_VAR");

            Assert.True((bool)engine.Evaluate(Parser.ParseExpression("p = LREAL#0.0"), frame));
            Assert.False((bool)engine.Evaluate(Parser.ParseExpression("p <> LREAL#0.0"), frame));
        }

        [Fact]
        public void TwoPointersToSameTarget_AreEqual()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\tbuf : ARRAY[0..3] OF BYTE := [1,2,3,4];\n\tp1 : POINTER TO BYTE;\n\tp2 : POINTER TO BYTE;\nEND_VAR");
            instance.Fields["p1"].Value = engine.Evaluate(Parser.ParseExpression("ADR(buf)"), frame);
            instance.Fields["p2"].Value = engine.Evaluate(Parser.ParseExpression("ADR(buf)"), frame);

            Assert.True((bool)engine.Evaluate(Parser.ParseExpression("p1 = p2"), frame));
        }

        [Fact]
        public void TwoPointersToDifferentTargets_AreNotEqual()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tbuf1 : ARRAY[0..3] OF BYTE := [1,2,3,4];\n\tbuf2 : ARRAY[0..3] OF BYTE := [5,6,7,8];\n\tp1 : POINTER TO BYTE;\n\tp2 : POINTER TO BYTE;\nEND_VAR");
            instance.Fields["p1"].Value = engine.Evaluate(Parser.ParseExpression("ADR(buf1)"), frame);
            instance.Fields["p2"].Value = engine.Evaluate(Parser.ParseExpression("ADR(buf2)"), frame);

            Assert.True((bool)engine.Evaluate(Parser.ParseExpression("p1 <> p2"), frame));
        }

        [Fact]
        public void TwoPointersToSameScalarTarget_AreEqual()
        {
            // A scalar target resolves to a plain Cell, not an array element,
            // so pointer identity falls back on reference equality of the
            // Cell rather than on array-and-index comparison.
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tbuf : BYTE := 1;\n\tp1 : POINTER TO BYTE;\n\tp2 : POINTER TO BYTE;\nEND_VAR");
            instance.Fields["p1"].Value = engine.Evaluate(Parser.ParseExpression("ADR(buf)"), frame);
            instance.Fields["p2"].Value = engine.Evaluate(Parser.ParseExpression("ADR(buf)"), frame);

            Assert.True((bool)engine.Evaluate(Parser.ParseExpression("p1 = p2"), frame));
        }

        [Fact]
        public void TwoPointersToDifferentScalarTargets_AreNotEqual()
        {
            // The same reference-equality fallback, in the negative: two
            // scalar VARs are distinct Cells even when they sit side by side.
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tbuf1 : BYTE := 1;\n\tbuf2 : BYTE := 2;\n\tp1 : POINTER TO BYTE;\n\tp2 : POINTER TO BYTE;\nEND_VAR");
            instance.Fields["p1"].Value = engine.Evaluate(Parser.ParseExpression("ADR(buf1)"), frame);
            instance.Fields["p2"].Value = engine.Evaluate(Parser.ParseExpression("ADR(buf2)"), frame);

            Assert.True((bool)engine.Evaluate(Parser.ParseExpression("p1 <> p2"), frame));
        }
    }
}
