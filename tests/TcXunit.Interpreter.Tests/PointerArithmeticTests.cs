using System;
using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-sej.2: ADR(x) +/- offset, scoped to pointers targeting an array
    // element (the POINTER TO BYTE over ARRAY OF BYTE buffer-packing case
    // MEMCPY/MEMSET/MEMMOVE need, TcXunit-sej.3). Byte-offset into a scalar
    // or the interior of a STRUCT is explicitly out of scope/unsupported.
    public class PointerArithmeticTests
    {
        private static (Engine Engine, FbInstance Instance, Frame Frame) NewHolder(string varBlock)
        {
            var fb = new PouAst("FB_Holder", null, varBlock, "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
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
        public void Adr_OnScalarPlusOffset_ThrowsNotSupported()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\tcount : INT := 5;\nEND_VAR");

            Assert.Throws<NotSupportedException>(() =>
                engine.Evaluate(Parser.ParseExpression("ADR(count) + 1"), frame));
        }

        [Fact]
        public void Adr_OnArrayElement_AllowsFurtherOffset()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\tbuf : ARRAY[0..3] OF BYTE := [10, 20, 30, 40];\nEND_VAR");

            var result = engine.Evaluate(Parser.ParseExpression("ADR(buf[1]) + 2"), frame);

            var ptr = Assert.IsType<Pointer>(result);
            Assert.Equal(40, ptr.Target.Value);
        }
    }
}
