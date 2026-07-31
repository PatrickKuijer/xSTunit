using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // Pointer arithmetic on an array element steps the real backing
    // ArrayValue, so writes through it land in the array. A scalar or whole
    // STRUCT has no elements to step, so it is packed into a synthetic
    // BYTE-array view of its current value instead - re-packed on each call,
    // and readable only.
    public class PointerArithmeticTests
    {
        private static (Engine Engine, FbInstance Instance, Frame Frame) NewHolder(
            string varBlock, IEnumerable<StructAst> structTypes = null)
        {
            var fb = new PouAst("FB_Holder", null, varBlock, "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }, structTypes));
            var instance = engine.NewInstance("FB_Holder");
            return (engine, instance, new Frame(instance, "FB_Holder"));
        }

        [Fact]
        public void Adr_OnArrayIdentifier_DecaysToFirstElement()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\tbuf : ARRAY[0..3] OF BYTE := [10, 20, 30, 40];\nEND_VAR");

            var ptr = (Pointer)engine.Evaluate(Parser.ParseExpression("ADR(buf)"), frame);

            Assert.Equal(10, ptr.Target.Value);
        }

        [Fact]
        public void Adr_PlusOffset_AdvancesToElementAtOffset()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\tbuf : ARRAY[0..3] OF BYTE := [10, 20, 30, 40];\nEND_VAR");

            var result = engine.Evaluate(Parser.ParseExpression("ADR(buf) + 2"), frame);

            var ptr = Assert.IsType<Pointer>(result);
            Assert.Equal(30, ptr.Target.Value);
        }

        [Fact]
        public void Deref_OfAdvancedPointer_ReadsElementAtOffset()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\tbuf : ARRAY[0..3] OF BYTE := [10, 20, 30, 40];\nEND_VAR");
            frame.Locals["p"] = new Cell { Value = engine.Evaluate(Parser.ParseExpression("ADR(buf) + 3"), frame) };

            var result = engine.Evaluate(Parser.ParseExpression("p^"), frame);

            Assert.Equal(40, result);
        }

        [Fact]
        public void Deref_OfAdvancedPointer_AssignmentWritesThroughToArray()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\tbuf : ARRAY[0..3] OF BYTE := [10, 20, 30, 40];\nEND_VAR");
            frame.Locals["p"] = new Cell { Value = engine.Evaluate(Parser.ParseExpression("ADR(buf) + 1"), frame) };

            ((Pointer)frame.Locals["p"].Value).Target.Value = 99;

            var buf = (ArrayValue)instance.Fields["buf"].Value;
            Assert.Equal(new object[] { 10, 99, 30, 40 }, buf.Elements);
        }

        [Fact]
        public void Adr_PlusOffset_OutOfBoundsThrows()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\tbuf : ARRAY[0..3] OF BYTE := [10, 20, 30, 40];\nEND_VAR");

            Assert.Throws<IndexOutOfRangeException>(() =>
                engine.Evaluate(Parser.ParseExpression("ADR(buf) + 4"), frame));
        }

        [Fact]
        public void Adr_MinusOffset_MovesBackward()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\tbuf : ARRAY[0..3] OF BYTE := [10, 20, 30, 40];\nEND_VAR");
            frame.Locals["p"] = new Cell { Value = engine.Evaluate(Parser.ParseExpression("ADR(buf) + 3"), frame) };

            var result = engine.Evaluate(Parser.ParseExpression("p - 1"), frame);

            var ptr = Assert.IsType<Pointer>(result);
            Assert.Equal(30, ptr.Target.Value);
        }

        [Fact]
        public void Adr_OnScalarPlusOffset_StepsThroughByteLayout()
        {
            // 258 = 0x0102, packed little-endian as INT: low byte 0x02,
            // high byte 0x01 (same layout SIZEOF/MEMCPY use).
            var (engine, instance, frame) = NewHolder("VAR\n\tcount : INT := 258;\nEND_VAR");

            var lowByte = engine.Evaluate(Parser.ParseExpression("(ADR(count) + 0)^"), frame);
            var highByte = engine.Evaluate(Parser.ParseExpression("(ADR(count) + 1)^"), frame);

            Assert.Equal(2, lowByte);
            Assert.Equal(1, highByte);
        }

        [Fact]
        public void Adr_OnStructPlusOffset_WalksAcrossFieldBoundary()
        {
            // uPair.a : USINT, uPair.b : INT (2-byte aligned) - byte offset 0
            // is 'a', offsets 2/3 are 'b' little-endian (offset 1 is alignment
            // padding), matching SIZEOF's own struct layout math.
            var structAst = new StructAst("uPair", new[]
            {
                new VarDecl("a", "USINT", null, VarSection.Local),
                new VarDecl("b", "INT", null, VarSection.Local),
            });
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tp : uPair;\nEND_VAR", new[] { structAst });
            engine.ExecuteStatements(Parser.ParseStatements("p.a := 7;\np.b := 258;"), frame);

            var byteA = engine.Evaluate(Parser.ParseExpression("(ADR(p) + 0)^"), frame);
            var byteBLow = engine.Evaluate(Parser.ParseExpression("(ADR(p) + 2)^"), frame);
            var byteBHigh = engine.Evaluate(Parser.ParseExpression("(ADR(p) + 3)^"), frame);

            Assert.Equal(7, byteA);
            Assert.Equal(2, byteBLow);
            Assert.Equal(1, byteBHigh);
        }

        [Fact]
        public void Adr_OnStructPlusOffset_ReflectsCurrentValueNotStaleSnapshot()
        {
            var structAst = new StructAst("uPair", new[]
            {
                new VarDecl("a", "USINT", null, VarSection.Local),
                new VarDecl("b", "INT", null, VarSection.Local),
            });
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tp : uPair;\nEND_VAR", new[] { structAst });
            engine.ExecuteStatements(Parser.ParseStatements("p.a := 7;"), frame);

            var before = engine.Evaluate(Parser.ParseExpression("(ADR(p) + 0)^"), frame);
            engine.ExecuteStatements(Parser.ParseStatements("p.a := 42;"), frame);
            var after = engine.Evaluate(Parser.ParseExpression("(ADR(p) + 0)^"), frame);

            Assert.Equal(7, before);
            Assert.Equal(42, after);
        }

        [Fact]
        public void Adr_OnUntypedCellPlusOffset_ThrowsNotSupported()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\tbuf : ARRAY[0..3] OF BYTE := [10, 20, 30, 40];\nEND_VAR");
            frame.Locals["untyped"] = new Cell { Value = new Pointer(new Cell { Value = 1 }) };

            Assert.Throws<NotSupportedException>(() =>
                engine.Evaluate(Parser.ParseExpression("untyped + 1"), frame));
        }

        [Fact]
        public void Adr_OnArrayElement_AllowsFurtherOffset()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\tbuf : ARRAY[0..3] OF BYTE := [10, 20, 30, 40];\nEND_VAR");

            var result = engine.Evaluate(Parser.ParseExpression("ADR(buf[1]) + 2"), frame);

            var ptr = Assert.IsType<Pointer>(result);
            Assert.Equal(40, ptr.Target.Value);
        }

        [Fact]
        public void Adr_PlusUdintOffset_DoesNotThrowInvalidCast()
        {
            // The usual buffer-compare loop counts with a UDINT index, which
            // boxes as long, so the offset must be narrowed rather than
            // unboxed straight to int.
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tbuf : ARRAY[0..3] OF BYTE := [10, 20, 30, 40];\n\ti : UDINT := 2;\nEND_VAR");

            var result = engine.Evaluate(Parser.ParseExpression("ADR(buf) + i"), frame);

            var ptr = Assert.IsType<Pointer>(result);
            Assert.Equal(30, ptr.Target.Value);
        }
    }
}
