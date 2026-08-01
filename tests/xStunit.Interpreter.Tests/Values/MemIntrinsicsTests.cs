using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using xStunit.Runner;
using Xunit;

namespace xStunit.Interpreter.Tests
{
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
            // Consuming 2 bytes off the head of a ring buffer. dest < src, so
            // a forward copy has no overlap hazard - this is the direction
            // MEMMOVE and MEMCPY agree on.
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tbuf : ARRAY[0..4] OF BYTE := [10, 20, 30, 40, 50];\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMMOVE(ADR(buf), ADR(buf) + 2, 3)"), frame);

            var buf = (ArrayValue)instance.Fields["buf"].Value;
            Assert.Equal(new object[] { 30, 40, 50, 40, 50 }, buf.Elements);
        }

        [Fact]
        public void Memmove_OverlappingBackwardShift_DestAheadOfSrc_CopiesBackwardWithoutClobbering()
        {
            // dest is ahead of src within the same array and the regions
            // overlap: a forward copy would overwrite src[2] (=30) before
            // reading it. MEMMOVE must copy backward instead.
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tbuf : ARRAY[0..4] OF BYTE := [10, 20, 30, 40, 50];\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMMOVE(ADR(buf) + 2, ADR(buf), 3)"), frame);

            var buf = (ArrayValue)instance.Fields["buf"].Value;
            Assert.Equal(new object[] { 10, 20, 10, 20, 30 }, buf.Elements);
        }

        [Fact]
        public void Memcpy_ForwardOverlap_NaivelyClobbersSource_UnlikeMemmove()
        {
            // The clobbering here is deliberate, not a defect: MEMCPY is not
            // overlap-safe, which is the whole reason MEMMOVE exists.
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

        // A scalar has no per-byte cells to write into, so the copied bytes
        // are packed into the INT's own little-endian byte representation.
        [Fact]
        public void Memcpy_IntoScalarCell_PacksBytesIntoValue()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tcount : INT;\n\tsrc : ARRAY[0..1] OF BYTE := [1, 2];\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(count), ADR(src), 2)"), frame);

            Assert.Equal(513, instance.Fields["count"].Value); // 0x0201 little-endian
        }

        [Fact]
        public void Memcpy_FromScalarCell_ReadsBytesOutOfValue()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tcount : INT := 513;\n\tdst : ARRAY[0..1] OF BYTE;\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(dst), ADR(count), 2)"), frame);

            var dst = (ArrayValue)instance.Fields["dst"].Value;
            Assert.Equal(new object[] { 1, 2 }, dst.Elements);
        }

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

        // A whole-struct MEMCPY has to honour the pack_mode pragma: rValue
        // sits right after eType at byte offset 4, not at the offset 8 an
        // 8-byte LREAL would take under natural alignment.
        [Fact]
        public void Memcpy_WholePackedStruct_PacksFieldsWithNoAlignmentPadding()
        {
            var structType = StructDeclParser.Parse(@"{attribute 'pack_mode' := '1'}
TYPE uGadgetSettingValue :
STRUCT
	nIndex : UINT;
	eType : UINT;
	rValue : LREAL;
END_STRUCT
END_TYPE");
            var fb = new PouAst(
                "FB_Holder", null,
                "VAR\n\tm : uGadgetSettingValue;\n\tout : ARRAY[0..11] OF BYTE;\n\tresult : LREAL;\nEND_VAR",
                "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }, new[] { structType }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");
            var m = (StructInstance)instance.Fields["m"].Value;
            m.Fields["nIndex"].Value = 1;
            m.Fields["eType"].Value = 2;
            m.Fields["rValue"].Value = 3.5;

            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(out), ADR(m), 12)"), frame);
            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(result), ADR(out[4]), 8)"), frame);

            Assert.Equal(3.5, instance.Fields["result"].Value);
        }

        // The inverse direction: unpacking must read the same packed offsets
        // packing wrote, so rValue's 8 bytes start at offset 4 of the 12-byte
        // source.
        [Fact]
        public void Memcpy_IntoWholePackedStruct_UnpacksFieldsWithNoAlignmentPadding()
        {
            var structType = StructDeclParser.Parse(@"{attribute 'pack_mode' := '1'}
TYPE uGadgetSettingValue :
STRUCT
	nIndex : UINT;
	eType : UINT;
	rValue : LREAL;
END_STRUCT
END_TYPE");
            var fb = new PouAst(
                "FB_Holder", null,
                "VAR\n\tm : uGadgetSettingValue;\n\tsrc : ARRAY[0..11] OF BYTE := [1, 0, 2, 0, 0, 0, 0, 0, 0, 0, 12, 64];\nEND_VAR",
                "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }, new[] { structType }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            // src bytes: nIndex=1, eType=2, rValue=3.5 (IEEE754 LE: 00 00 00 00 00 00 0C 40)
            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(m), ADR(src), 12)"), frame);

            var m = (StructInstance)instance.Fields["m"].Value;
            Assert.Equal(1, m.Fields["nIndex"].Value);
            Assert.Equal(2, m.Fields["eType"].Value);
            Assert.Equal(3.5, m.Fields["rValue"].Value);
        }

        // A STRING(n) field packs as ASCII bytes padded out with nulls, so a
        // wire record carrying a name can round-trip through a byte buffer.
        [Fact]
        public void Memcpy_StringStructField_RoundTripsThroughByteBuffer()
        {
            var structType = StructDeclParser.Parse(@"TYPE ST_Msg :
STRUCT
	name : STRING(5);
END_STRUCT
END_TYPE");
            var fb = new PouAst(
                "FB_Holder", null, "VAR\n\tm : ST_Msg := (name := 'abc');\n\tout : ARRAY[0..5] OF BYTE;\nEND_VAR",
                "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }, new[] { structType }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(out), ADR(m.name), 6)"), frame);

            var outBuf = (ArrayValue)instance.Fields["out"].Value;
            Assert.Equal(new object[] { 97, 98, 99, 0, 0, 0 }, outBuf.Elements); // "abc\0\0\0"
        }

        // Unpacking stops at the first null: the trailing padding bytes are
        // not part of the value.
        [Fact]
        public void Memcpy_IntoStringStructField_UnpacksBytesAsString()
        {
            var structType = StructDeclParser.Parse(@"TYPE ST_Msg :
STRUCT
	name : STRING(5);
END_STRUCT
END_TYPE");
            var fb = new PouAst(
                "FB_Holder", null,
                "VAR\n\tm : ST_Msg;\n\tsrc : ARRAY[0..5] OF BYTE := [120, 121, 0, 0, 0, 0];\nEND_VAR", // "xy\0\0\0\0"
                "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }, new[] { structType }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(m.name), ADR(src), 6)"), frame);

            var m = (StructInstance)instance.Fields["m"].Value;
            Assert.Equal("xy", m.Fields["name"].Value);
        }

        // The declared length, not the current value's length, decides how
        // many bytes a STRING occupies on the wire.
        [Fact]
        public void Memcpy_StringLongerThanDeclaredLength_TruncatesToDeclaredLength()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\ts : STRING(3) := 'abcdef';\n\tout : ARRAY[0..3] OF BYTE;\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(out), ADR(s), 4)"), frame);

            var outBuf = (ArrayValue)instance.Fields["out"].Value;
            Assert.Equal(new object[] { 97, 98, 99, 0 }, outBuf.Elements); // "abc\0"
        }

        // A narrow STRING goes on the wire as Latin-1, so an accented
        // character above U+007F packs as the single byte TwinCAT would write.
        [Fact]
        public void Memcpy_StringWithLatin1Character_PacksItAsOneByte()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\ts : STRING(3) := 'aäb';\n\tout : ARRAY[0..3] OF BYTE;\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(out), ADR(s), 4)"), frame);

            var outBuf = (ArrayValue)instance.Fields["out"].Value;
            Assert.Equal(new object[] { 97, 0xE4, 98, 0 }, outBuf.Elements);
        }

        [Fact]
        public void Memcpy_IntoString_UnpacksBytesAsLatin1Characters()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\ts : STRING(3);\n\tsrc : ARRAY[0..3] OF BYTE := [97, 16#E4, 98, 0];\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(s), ADR(src), 4)"), frame);

            Assert.Equal("aäb", instance.Fields["s"].Value);
        }

        // Packing used to write the low byte of anything - a euro sign became
        // 0xAC, a byte TwinCAT would never have produced for it.
        [Fact]
        public void Memcpy_StringWithCharacterAboveLatin1_ThrowsRatherThanPackingTheLowByte()
        {
            var (engine, _, frame) = NewHolder(
                "VAR\n\ts : STRING(3) := 'a€b';\n\tout : ARRAY[0..3] OF BYTE;\nEND_VAR");

            var ex = Assert.Throws<UnsupportedConstructException>(
                () => engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(out), ADR(s), 4)"), frame));

            Assert.Contains("€", ex.Message);
        }

        // WSTRING goes on the wire as little-endian UCS-2 - two bytes per
        // character, matching TwinCAT on x86 - not as one byte per character
        // the way a narrow STRING does.
        [Fact]
        public void Memcpy_WStringVariable_PacksTwoLittleEndianBytesPerCharacter()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\ts : WSTRING(3) := \"AB\";\n\tout : ARRAY[0..7] OF BYTE;\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(out), ADR(s), 8)"), frame);

            var outBuf = (ArrayValue)instance.Fields["out"].Value;
            Assert.Equal(new object[] { 65, 0, 66, 0, 0, 0, 0, 0 }, outBuf.Elements);
        }

        [Fact]
        public void Memcpy_IntoWStringVariable_ReadsTwoBytesPerCharacterAndStopsAtWideTerminator()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\ts : WSTRING(3);\n\tsrc : ARRAY[0..7] OF BYTE := [120, 0, 121, 0, 0, 0, 0, 0];\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(s), ADR(src), 8)"), frame);

            Assert.Equal("xy", instance.Fields["s"].Value);
        }

        // A character above U+00FF is representable in a WSTRING, so the high
        // byte must survive the round trip rather than being dropped the way
        // one-byte-per-character packing dropped it.
        [Fact]
        public void Memcpy_WStringWithNonLatin1Character_KeepsBothBytes()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\ts : WSTRING(2) := \"€\";\n\tout : ARRAY[0..5] OF BYTE;\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(out), ADR(s), 6)"), frame);

            var outBuf = (ArrayValue)instance.Fields["out"].Value;
            Assert.Equal(new object[] { 0xAC, 0x20, 0, 0, 0, 0 }, outBuf.Elements);
        }

        // A field declared after a WSTRING member sits past two bytes per
        // character plus the wide terminator, so narrow sizing would place it
        // several bytes early and corrupt everything from there on.
        [Fact]
        public void Memcpy_StructWithWStringField_PlacesFollowingFieldAfterTheWideBuffer()
        {
            var structType = StructDeclParser.Parse(@"TYPE ST_WideMsg :
STRUCT
	label : WSTRING(2);
	count : INT;
END_STRUCT
END_TYPE");
            var fb = new PouAst(
                "FB_Holder", null,
                "VAR\n\tm : ST_WideMsg := (label := \"AB\", count := 258);\n\tout : ARRAY[0..7] OF BYTE;\nEND_VAR",
                "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }, new[] { structType }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(out), ADR(m), 8)"), frame);

            var outBuf = (ArrayValue)instance.Fields["out"].Value;
            // "AB" fills offsets 0..3, the wide terminator 4..5, and count
            // (0x0102, little-endian) lands at 6..7.
            Assert.Equal(new object[] { 65, 0, 66, 0, 0, 0, 2, 1 }, outBuf.Elements);
        }

        [Fact]
        public void Memset_OnScalarCell_FillsBytesOfValue()
        {
            var (engine, instance, frame) = NewHolder("VAR\n\tcount : DINT := 0;\nEND_VAR");

            engine.Evaluate(Parser.ParseExpression("MEMSET(ADR(count), 1, 4)"), frame);

            Assert.Equal(16843009, instance.Fields["count"].Value); // 0x01010101
        }

        // Byte addressing needs a declared type to know the width and layout
        // of the value, so a Cell with no VarDecl behind it cannot be a
        // MEMCPY target however plausible its runtime value looks.
        [Fact]
        public void Memcpy_TargetCellWithNoDeclaredType_ThrowsNotSupported()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tsrc : ARRAY[0..1] OF BYTE := [1, 2];\nEND_VAR");
            frame.Locals["p"] = new Cell { Value = new Pointer(new Cell { Value = 0 }) };

            Assert.Throws<NotSupportedException>(() =>
                engine.Evaluate(Parser.ParseExpression("MEMCPY(p, ADR(src), 2)"), frame));
        }

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

        [Fact]
        public void Memcpy_MissingNArgument_ThrowsWithParamNameInMessage()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tsrc : ARRAY[0..3] OF BYTE := [1, 2, 3, 4];\n\tdst : ARRAY[0..3] OF BYTE;\nEND_VAR");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                engine.Evaluate(Parser.ParseExpression("MEMCPY(ADR(dst), ADR(src))"), frame));

            Assert.Equal("MEMCPY missing required argument 'n'", ex.Message);
        }

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
