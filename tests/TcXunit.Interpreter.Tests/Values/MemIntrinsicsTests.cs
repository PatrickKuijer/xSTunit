using System;
using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-sej.3: MEMCPY/MEMSET/MEMMOVE, built on array indexing
    // (TcXunit-sej.1) and array-element pointer arithmetic (TcXunit-sej.2).
    // Scoped to POINTER TO BYTE over ARRAY OF BYTE - the Beckhoff
    // buffer-packing/message-framing case the ticket was raised for.
    public class MemIntrinsicsTests
    {
        private static (Engine Engine, FbInstance Instance, Frame Frame) NewHolder(string varBlock)
        {
            var fb = new PouAst("FB_Holder", null, varBlock, "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            return (engine, instance, new Frame(instance, "FB_Holder"));
        }

        [Fact]
        public void Memcpy_BetweenTwoArrays_CopiesNBytes()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tsrc : ARRAY[0..3] OF BYTE := [1, 2, 3, 4];\n\tdst : ARRAY[0..3] OF BYTE;\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(dst), ADR(src), 3)"), frame);

            var dst = (ArrayValue)instance.Fields["dst"].Value;
            Assert.Equal(new object[] { 1, 2, 3, 0 }, dst.Elements);
        }

        [Fact]
        public void Memcpy_MidStructOffsetIntoBuffer_CopiesFromGivenOffset()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tsrc : ARRAY[0..5] OF BYTE := [1, 2, 3, 4, 5, 6];\n\tdst : ARRAY[0..2] OF BYTE;\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(dst), ADR(src) + 2, 3)"), frame);

            var dst = (ArrayValue)instance.Fields["dst"].Value;
            Assert.Equal(new object[] { 3, 4, 5 }, dst.Elements);
        }

        [Fact]
        public void Memcpy_ReturnsDestPointer()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tsrc : ARRAY[0..1] OF BYTE := [7, 8];\n\tdst : ARRAY[0..1] OF BYTE;\nEND_VAR");

            var result = engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(dst), ADR(src), 2)"), frame);

            var ptr = Assert.IsType<Pointer>(result);
            Assert.Equal(7, ptr.Target.Value);
        }

        [Fact]
        public void Memset_FillsNBytesWithLowByteOfValue()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\tbuf : ARRAY[0..4] OF BYTE := [1, 1, 1, 1, 1];\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMSET(ADR(buf), 0, 3)"), frame);

            var buf = (ArrayValue)instance.Fields["buf"].Value;
            Assert.Equal(new object[] { 0, 0, 0, 1, 1 }, buf.Elements);
        }

        [Fact]
        public void Memset_ValueWiderThanByte_TruncatesToLowByte()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\tbuf : ARRAY[0..1] OF BYTE;\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMSET(ADR(buf), 258, 2)"), frame);

            var buf = (ArrayValue)instance.Fields["buf"].Value;
            Assert.Equal(new object[] { 2, 2 }, buf.Elements);
        }

        [Fact]
        public void Memmove_OverlappingForwardShift_RingBufferConsumeHead_ShiftsCorrectly()
        {
            // Consuming 2 bytes off the head of a 5-byte ring buffer: shift
            // the remaining 3 bytes down to index 0 (dest < src, no overlap
            // hazard for a forward copy - but exercises the same call path a
            // backward shift would).
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tbuf : ARRAY[0..4] OF BYTE := [10, 20, 30, 40, 50];\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMMOVE(ADR(buf), ADR(buf) + 2, 3)"), frame);

            var buf = (ArrayValue)instance.Fields["buf"].Value;
            Assert.Equal(new object[] { 30, 40, 50, 40, 50 }, buf.Elements);
        }

        [Fact]
        public void Memmove_OverlappingBackwardShift_DestAheadOfSrc_CopiesBackwardWithoutClobbering()
        {
            // dest is *ahead* of src within the same array and the regions
            // overlap - a naive forward MEMCPY would overwrite src[2] (=30)
            // with dest[0]'s new value before it's read. MEMMOVE must not.
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tbuf : ARRAY[0..4] OF BYTE := [10, 20, 30, 40, 50];\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMMOVE(ADR(buf) + 2, ADR(buf), 3)"), frame);

            var buf = (ArrayValue)instance.Fields["buf"].Value;
            Assert.Equal(new object[] { 10, 20, 10, 20, 30 }, buf.Elements);
        }

        [Fact]
        public void Memcpy_ForwardOverlap_NaivelyClobbersSource_UnlikeMemmove()
        {
            // Same scenario as the MEMMOVE backward-shift test, but via
            // MEMCPY - documents that MEMCPY is NOT overlap-safe (matches
            // the C intrinsic it mirrors; MEMMOVE exists for exactly this).
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tbuf : ARRAY[0..4] OF BYTE := [10, 20, 30, 40, 50];\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(buf) + 2, ADR(buf), 3)"), frame);

            var buf = (ArrayValue)instance.Fields["buf"].Value;
            Assert.Equal(new object[] { 10, 20, 10, 20, 10 }, buf.Elements);
        }

        [Fact]
        public void Memcpy_CountExceedsRemainingElements_Throws()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tsrc : ARRAY[0..2] OF BYTE := [1, 2, 3];\n\tdst : ARRAY[0..2] OF BYTE;\nEND_VAR");

            Assert.Throws<IndexOutOfRangeException>(() =>
                engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(dst), ADR(src), 4)"), frame));
        }

        // TcXunit-4vn: ADR(scalarVar) as a MEMCPY dest - no ArrayElementCell,
        // so the 2 copied bytes are packed into count's own INT byte
        // representation (little-endian) rather than requiring an
        // ArrayElementCell target.
        [Fact]
        public void Memcpy_IntoScalarCell_PacksBytesIntoValue()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tcount : INT;\n\tsrc : ARRAY[0..1] OF BYTE := [1, 2];\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(count), ADR(src), 2)"), frame);

            Assert.Equal(513, instance.Fields["count"].Value); // 0x0201 little-endian
        }

        // ADR(scalarVar) as a MEMCPY src - the scalar's current byte
        // representation is read out into the dest array.
        [Fact]
        public void Memcpy_FromScalarCell_ReadsBytesOutOfValue()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tcount : INT := 513;\n\tdst : ARRAY[0..1] OF BYTE;\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(dst), ADR(count), 2)"), frame);

            var dst = (ArrayValue)instance.Fields["dst"].Value;
            Assert.Equal(new object[] { 1, 2 }, dst.Elements);
        }

        // ADR(struct.field) - a struct field Cell holding a scalar, packed
        // by its own declared field type same as a plain scalar variable.
        [Fact]
        public void Memcpy_IntoStructField_PacksBytesIntoFieldValue()
        {
            var structType = StructDeclParser.Parse(@"TYPE ST_Msg :
STRUCT
	flag : BYTE;
	count : INT;
END_STRUCT
END_TYPE");
            var fb = new PouAst(
                "FB_Holder", null, "VAR\n\tm : ST_Msg;\n\tsrc : ARRAY[0..1] OF BYTE := [9, 0];\nEND_VAR", "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }, new[] { structType }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(m.count), ADR(src), 2)"), frame);

            var m = (StructInstance)instance.Fields["m"].Value;
            Assert.Equal(9, m.Fields["count"].Value);
        }

        // MEMSET on a scalar Cell: fills its byte representation with the
        // low byte of value, same as filling a BYTE array.
        [Fact]
        public void Memset_OnScalarCell_FillsBytesOfValue()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\tcount : DINT := 0;\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMSET(ADR(count), 1, 4)"), frame);

            Assert.Equal(16843009, instance.Fields["count"].Value); // 0x01010101
        }

        // A scalar/struct-field Cell with no declared type (not backed by a
        // VarDecl) still can't be byte-addressed - only ArrayElementCell or
        // a Cell with a known DeclaredTypeName is supported.
        [Fact]
        public void Memcpy_TargetCellWithNoDeclaredType_ThrowsNotSupported()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tsrc : ARRAY[0..1] OF BYTE := [1, 2];\nEND_VAR");
            frame.Locals["p"] = new Cell { Value = new Pointer(new Cell { Value = 0 }) };

            Assert.Throws<NotSupportedException>(() =>
                engine.Evaluate(Parser.ParseExpression("MEMCPY(p, ADR(src), 2)"), frame));
        }

        // TcXunit-996: destAddr/srcAddr passed by name (a legal ST calling
        // convention) must resolve by name, not fall through to indexing
        // PositionalArgs (which only holds the trailing positional n here).
        [Fact]
        public void Memcpy_WithNamedDestAndSrcArgs_ResolvesByName()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tsrc : ARRAY[0..3] OF BYTE := [1, 2, 3, 4];\n\tdst : ARRAY[0..3] OF BYTE;\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMCPY(destAddr := ADR(dst), srcAddr := ADR(src), 3)"), frame);

            var dst = (ArrayValue)instance.Fields["dst"].Value;
            Assert.Equal(new object[] { 1, 2, 3, 0 }, dst.Elements);
        }

        [Fact]
        public void Memmove_WithNamedDestAndSrcArgs_ResolvesByName()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tbuf : ARRAY[0..4] OF BYTE := [1, 2, 3, 4, 5];\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMMOVE(destAddr := ADR(buf) + 1, srcAddr := ADR(buf), 3)"), frame);

            var buf = (ArrayValue)instance.Fields["buf"].Value;
            Assert.Equal(new object[] { 1, 1, 2, 3, 5 }, buf.Elements);
        }

        [Fact]
        public void Memset_WithNamedDestAndValueArgs_ResolvesByName()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tdst : ARRAY[0..2] OF BYTE;\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMSET(destAddr := ADR(dst), value := 9, n := 3)"), frame);

            var dst = (ArrayValue)instance.Fields["dst"].Value;
            Assert.Equal(new object[] { 9, 9, 9 }, dst.Elements);
        }

        [Fact]
        public void Memcpy_AllNamedArgsInAnyOrder_ResolvesByName()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tsrc : ARRAY[0..3] OF BYTE := [1, 2, 3, 4];\n\tdst : ARRAY[0..3] OF BYTE;\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMCPY(n := 3, srcAddr := ADR(src), destAddr := ADR(dst))"), frame);

            var dst = (ArrayValue)instance.Fields["dst"].Value;
            Assert.Equal(new object[] { 1, 2, 3, 0 }, dst.Elements);
        }

        // TcXunit-1hc: RequireIntrinsicArg reports the missing parameter by
        // name (not by positional index) when neither a named nor enough
        // positional args are supplied.
        [Fact]
        public void Memcpy_MissingNArgument_ThrowsWithParamNameInMessage()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tsrc : ARRAY[0..3] OF BYTE := [1, 2, 3, 4];\n\tdst : ARRAY[0..3] OF BYTE;\nEND_VAR");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(dst), ADR(src))"), frame));

            Assert.Equal("MEMCPY missing required argument 'n'", ex.Message);
        }

        // TcXunit-1hc: RequirePointerArg rejects a resolved, non-missing arg
        // that isn't a Pointer (e.g. a plain INT passed where ADR(...) was
        // expected), reporting the offending param name and value type.
        [Fact]
        public void Memcpy_NamedDestArgNotAPointer_ThrowsMustBePointerToByte()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tsrc : ARRAY[0..1] OF BYTE := [1, 2];\nEND_VAR");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                engine.Evaluate(Parser.ParseExpression("MEMCPY(destAddr := 5, srcAddr := ADR(src), n := 2)"), frame));

            Assert.Equal(
                "MEMCPY argument 'destAddr' must be a POINTER TO BYTE (e.g. ADR(buf) or ADR(buf[i])), got Int32",
                ex.Message);
        }
    }
}
