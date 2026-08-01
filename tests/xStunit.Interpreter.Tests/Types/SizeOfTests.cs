using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // Struct and array sizes assume natural alignment unless a pack_mode
    // pragma caps it; see the layout rules in Engine.SizeOf.cs.
    public class SizeOfTests
    {
        private static (Engine Engine, FbInstance Instance, Frame Frame) NewHolder(
            string varBlock,
            IEnumerable<StructAst> structTypes = null,
            IEnumerable<KeyValuePair<string, string>> aliases = null)
        {
            var fb = new PouAst("FB_Holder", null, varBlock, "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }, structTypes, aliases: aliases));
            var instance = engine.NewInstance("FB_Holder");
            return (engine, instance, new Frame(instance, "FB_Holder"));
        }

        [Theory]
        [InlineData("BOOL", 1)]
        [InlineData("SINT", 1)]
        [InlineData("USINT", 1)]
        [InlineData("BYTE", 1)]
        [InlineData("INT", 2)]
        [InlineData("UINT", 2)]
        [InlineData("WORD", 2)]
        [InlineData("DINT", 4)]
        [InlineData("UDINT", 4)]
        [InlineData("DWORD", 4)]
        [InlineData("REAL", 4)]
        [InlineData("TIME", 4)]
        [InlineData("DATE", 4)]
        [InlineData("DATE_AND_TIME", 4)]
        [InlineData("TIME_OF_DAY", 4)]
        [InlineData("LINT", 8)]
        [InlineData("ULINT", 8)]
        [InlineData("LWORD", 8)]
        [InlineData("LREAL", 8)]
        [InlineData("LTIME", 8)]
        public void SizeOf_ScalarVariable_ReturnsByteWidth(string typeName, int expectedBytes)
        {
            var (engine, _, frame) = NewHolder($"VAR\n\tx : {typeName};\nEND_VAR");

            var result = engine.Evaluate(Parser.ParseExpression("SIZEOF(x)"), frame);

            Assert.Equal(expectedBytes, result);
        }

        [Fact]
        public void SizeOf_BareTypeName_ReturnsByteWidth()
        {
            var (engine, _, frame) = NewHolder("VAR\n\tx : INT;\nEND_VAR");

            var result = engine.Evaluate(Parser.ParseExpression("SIZEOF(DINT)"), frame);

            Assert.Equal(4, result);
        }

        [Fact]
        public void SizeOf_DefaultString_ReturnsEightyOnePlusNull()
        {
            var (engine, _, frame) = NewHolder("VAR\n\ts : STRING;\nEND_VAR");

            var result = engine.Evaluate(Parser.ParseExpression("SIZEOF(s)"), frame);

            Assert.Equal(81, result);
        }

        [Fact]
        public void SizeOf_SizedString_ReturnsLengthPlusNull()
        {
            var (engine, _, frame) = NewHolder("VAR\n\ts : STRING(10);\nEND_VAR");

            var result = engine.Evaluate(Parser.ParseExpression("SIZEOF(s)"), frame);

            Assert.Equal(11, result);
        }

        [Fact]
        public void SizeOf_StringSizedByGvlQualifiedConstant_ReturnsLengthPlusNull()
        {
            var gvl = new GvlAst("cScratchConstants", "VAR_GLOBAL CONSTANT\n\tMAX_LABEL_STRING_SIZE : UINT := 32;\nEND_VAR");
            var fb = new PouAst(
                "FB_Holder", null, "VAR\n\ts : STRING(cScratchConstants.MAX_LABEL_STRING_SIZE);\nEND_VAR", "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }, null, new[] { gvl }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            var result = engine.Evaluate(Parser.ParseExpression("SIZEOF(s)"), frame);

            Assert.Equal(33, result);
        }

        // WSTRING is UCS-2 on the wire: two bytes per character plus a
        // two-byte terminator, so it is not the narrow size even though the
        // value model stores both in one CLR string.
        [Fact]
        public void SizeOf_DefaultWString_ReturnsTwoBytesPerCharacterPlusTerminator()
        {
            var (engine, _, frame) = NewHolder("VAR\n\ts : WSTRING;\nEND_VAR");

            var result = engine.Evaluate(Parser.ParseExpression("SIZEOF(s)"), frame);

            Assert.Equal(162, result);
        }

        [Fact]
        public void SizeOf_SizedWString_ReturnsTwoBytesPerCharacterPlusTerminator()
        {
            var (engine, _, frame) = NewHolder("VAR\n\ts : WSTRING(10);\nEND_VAR");

            var result = engine.Evaluate(Parser.ParseExpression("SIZEOF(s)"), frame);

            Assert.Equal(22, result);
        }

        // A WSTRING member's alignment is 2, not 1, so it both pads itself onto
        // an even offset and pushes every field after it - the consequence a
        // scalar SIZEOF check on its own would miss.
        [Fact]
        public void SizeOf_StructWithWStringField_AlignsMemberAndFollowingFieldsToTwoBytes()
        {
            var structType = StructDeclParser.Parse(@"TYPE ST_WideMsg :
STRUCT
	flag : BYTE;
	label : WSTRING(2);
	count : INT;
END_STRUCT
END_TYPE");
            var (engine, _, frame) = NewHolder("VAR\n\tm : ST_WideMsg;\nEND_VAR", new[] { structType });

            var result = engine.Evaluate(Parser.ParseExpression("SIZEOF(m)"), frame);

            // flag at offset 0; label needs 2-byte alignment so a 1-byte pad
            // lands it at offset 2..7 (6 bytes); count at offset 8..9 - 10
            // bytes, already a multiple of the struct's alignment of 2.
            Assert.Equal(10, result);
        }

        [Fact]
        public void SizeOf_Array_ReturnsElementSizeTimesCount()
        {
            var (engine, _, frame) = NewHolder("VAR\n\ta : ARRAY[0..9] OF INT;\nEND_VAR");

            var result = engine.Evaluate(Parser.ParseExpression("SIZEOF(a)"), frame);

            Assert.Equal(20, result);
        }

        [Fact]
        public void SizeOf_Struct_PadsFieldsToNaturalAlignment()
        {
            // BYTE at offset 0 (1 byte), then INT needs 2-byte alignment so
            // a 1-byte pad is inserted before it at offset 2, landing it at
            // offset 2..3; DINT needs 4-byte alignment and is already at
            // offset 4, so no further pad - total 8 bytes (already a
            // multiple of the struct's largest member alignment, 4).
            var structType = StructDeclParser.Parse(@"TYPE ST_Msg :
STRUCT
	flag : BYTE;
	count : INT;
	total : DINT;
END_STRUCT
END_TYPE");
            var (engine, _, frame) = NewHolder("VAR\n\tm : ST_Msg;\nEND_VAR", new[] { structType });

            var result = engine.Evaluate(Parser.ParseExpression("SIZEOF(m)"), frame);

            Assert.Equal(8, result);
        }

        [Fact]
        public void SizeOf_StructBareTypeName_MatchesVariableSize()
        {
            var structType = StructDeclParser.Parse(@"TYPE ST_Point :
STRUCT
	x : DINT;
	y : DINT;
END_STRUCT
END_TYPE");
            var (engine, _, frame) = NewHolder("VAR\n\tp : ST_Point;\nEND_VAR", new[] { structType });

            var result = engine.Evaluate(Parser.ParseExpression("SIZEOF(ST_Point)"), frame);

            Assert.Equal(8, result);
        }

        [Fact]
        public void SizeOf_NestedStructAndArrayField_ComputesRecursively()
        {
            var inner = StructDeclParser.Parse(@"TYPE ST_Inner :
STRUCT
	a : BYTE;
	b : INT;
END_STRUCT
END_TYPE");
            var outer = StructDeclParser.Parse(@"TYPE ST_Outer :
STRUCT
	items : ARRAY[0..1] OF ST_Inner;
	flag : BOOL;
END_STRUCT
END_TYPE");
            var (engine, _, frame) = NewHolder("VAR\n\to : ST_Outer;\nEND_VAR", new[] { inner, outer });

            var result = engine.Evaluate(Parser.ParseExpression("SIZEOF(o)"), frame);

            // ST_Inner is 4 bytes (BYTE padded to offset 2, +2-byte INT),
            // align 2; the 2-element array is 8 bytes, align 2. flag (BOOL,
            // 1 byte) lands at offset 8 with no pad needed, bringing the
            // struct to 9 bytes - but the struct's own alignment is 2 (its
            // largest member), so the overall size pads up to 10.
            Assert.Equal(10, result);
        }

        [Fact]
        public void SizeOf_EnumVariable_DefaultsToIntByteWidth()
        {
            // An ENUM with no explicit base type is INT-backed, and reaches
            // SIZEOF as an alias to that base type like any other alias.
            var aliases = new[] { new KeyValuePair<string, string>("E_Color", "INT") };
            var (engine, _, frame) = NewHolder("VAR\n\tc : E_Color;\nEND_VAR", aliases: aliases);

            var result = engine.Evaluate(Parser.ParseExpression("SIZEOF(c)"), frame);

            Assert.Equal(2, result);
        }

        [Fact]
        public void SizeOf_EnumWithExplicitBaseType_ReturnsBaseTypeByteWidth()
        {
            var aliases = new[] { new KeyValuePair<string, string>("eWidgetValueKind", "DINT") };
            var (engine, _, frame) = NewHolder(
                "VAR\n\td : eWidgetValueKind;\nEND_VAR", aliases: aliases);

            var result = engine.Evaluate(Parser.ParseExpression("SIZEOF(d)"), frame);

            Assert.Equal(4, result);
        }

        // {attribute 'pack_mode' := '1'} byte-packs a struct: no per-field
        // alignment padding at all. The same fields at natural alignment pad
        // the LREAL out to offset 8 and the struct to 16 bytes, as the test
        // immediately below shows.
        [Fact]
        public void SizeOf_PackedStruct_HasNoAlignmentPadding()
        {
            var structType = StructDeclParser.Parse(@"{attribute 'pack_mode' := '1'}
TYPE uGadgetSettingValue :
STRUCT
	nIndex : UINT;
	eType : UINT;
	rValue : LREAL;
END_STRUCT
END_TYPE");
            var (engine, _, frame) = NewHolder("VAR\n\tm : uGadgetSettingValue;\nEND_VAR", new[] { structType });

            var result = engine.Evaluate(Parser.ParseExpression("SIZEOF(m)"), frame);

            Assert.Equal(12, result);
        }

        [Fact]
        public void SizeOf_SameFieldsWithoutPackMode_PadsToNaturalAlignment()
        {
            var structType = StructDeclParser.Parse(@"TYPE uGadgetSettingValue :
STRUCT
	nIndex : UINT;
	eType : UINT;
	rValue : LREAL;
END_STRUCT
END_TYPE");
            var (engine, _, frame) = NewHolder("VAR\n\tm : uGadgetSettingValue;\nEND_VAR", new[] { structType });

            var result = engine.Evaluate(Parser.ParseExpression("SIZEOF(m)"), frame);

            Assert.Equal(16, result);
        }

        [Fact]
        public void SizeOf_StructFieldOfEnumType_ComputesRecursively()
        {
            var structType = StructDeclParser.Parse(@"TYPE ST_WithEnum :
STRUCT
	flag : BYTE;
	kind : E_Color;
END_STRUCT
END_TYPE");
            var aliases = new[] { new KeyValuePair<string, string>("E_Color", "INT") };
            var (engine, _, frame) = NewHolder(
                "VAR\n\tm : ST_WithEnum;\nEND_VAR", new[] { structType }, aliases);

            var result = engine.Evaluate(Parser.ParseExpression("SIZEOF(m)"), frame);

            // BYTE at offset 0 (1 byte), then the enum (resolved to INT, 2
            // bytes) needs 2-byte alignment - a 1-byte pad before it lands
            // it at offset 2..3, total 4 bytes.
            Assert.Equal(4, result);
        }
    }
}
