using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Interpreter.Extensibility;
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
            IEnumerable<KeyValuePair<string, string>> aliases = null,
            TargetPlatform target = null)
        {
            var fb = new PouAst("FB_Holder", null, varBlock, "", new List<MethodAst>());
            var engine = new Engine(
                new TypeRegistry(new[] { fb }, structTypes, aliases: aliases),
                new NativePlugins(),
                target ?? TargetPlatform.Default);
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

        // IEC 61131-3 spells its two longest date/time types twice over - DT for
        // DATE_AND_TIME, TOD for TIME_OF_DAY - and TwinCAT's own
        // PlcAppSystemInfo declares a member in the short form. One type under
        // two names: sizing them apart makes the same declaration measure
        // differently depending on how its author happened to write it.
        [Theory]
        [InlineData("DT", "DATE_AND_TIME")]
        [InlineData("TOD", "TIME_OF_DAY")]
        public void SizeOf_AbbreviatedDateTimeSpelling_MatchesTheSpelledOutOne(string abbreviated, string spelledOut)
        {
            var (engine, _, frame) = NewHolder(
                $"VAR\n\tshortSpelling : {abbreviated};\n\tlongSpelling : {spelledOut};\nEND_VAR");

            Assert.Equal(
                engine.Evaluate(Parser.ParseExpression("SIZEOF(longSpelling)"), frame),
                engine.Evaluate(Parser.ParseExpression("SIZEOF(shortSpelling)"), frame));
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

        // An object-type-class id is four bytes on every target - both golden
        // .tmc modules declare PlcAppSystemInfo.ObjId at 32 bits, the x86 build
        // and the x64 one alike - so it is sizable without knowing which target
        // the code is built for, unlike the void pointer below.
        [Theory]
        [InlineData("SIZEOF(id)")]
        [InlineData("SIZEOF(OTCID)")]
        public void SizeOf_ObjectTypeClassId_IsFourBytesWithoutKnowingTheTarget(string expression)
        {
            var (engine, _, frame) = NewHolder("VAR\n\tid : OTCID;\nEND_VAR");

            var result = engine.Evaluate(Parser.ParseExpression(expression), frame);

            Assert.Equal(4, result);
        }

        // The addresses are the case the id above is not: the golden .tmc pair
        // disagrees about their width, 32 bits on x86 and 64 on x64, so any
        // fixed answer would be right on one target and wrong on the other.
        // The void pointer sizes with the spelled-out addresses beside it -
        // naming no referent is what makes it an address, not something
        // narrower.
        [Theory]
        [InlineData("x86", 4)]
        [InlineData("x64", 8)]
        public void SizeOf_AddressDeclarations_AreTheSelectedTargetsAddressWidth(
            string targetName, int expectedBytes)
        {
            Assert.True(TargetPlatform.TryParse(targetName, out var target));
            var (engine, _, frame) = NewHolder(
                "VAR\n\tp : PVOID;\n\tq : POINTER TO LREAL;\n\tr : REFERENCE TO INT;\nEND_VAR", target: target);

            Assert.Equal(expectedBytes, engine.Evaluate(Parser.ParseExpression("SIZEOF(p)"), frame));
            Assert.Equal(expectedBytes, engine.Evaluate(Parser.ParseExpression("SIZEOF(q)"), frame));
            Assert.Equal(expectedBytes, engine.Evaluate(Parser.ParseExpression("SIZEOF(r)"), frame));
        }

        // An Engine built without a target is the one every other test here
        // uses, so what it assumes is part of the layout contract rather than
        // an implementation detail: the default target's width, which
        // TargetPlatformTests pins to x64's 8 bytes.
        [Fact]
        public void SizeOf_AddressWithNoTargetSelected_IsTheDefaultTargetsAddressWidth()
        {
            var (engine, _, frame) = NewHolder("VAR\n\tp : POINTER TO LREAL;\nEND_VAR");

            Assert.Equal(
                TargetPlatform.Default.AddressSize,
                engine.Evaluate(Parser.ParseExpression("SIZEOF(p)"), frame));
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

        // A union is as wide as its widest member, not as wide as its members
        // laid end to end: reading it as a struct would make this 14 bytes.
        [Fact]
        public void SizeOf_Union_IsItsWidestMemberRatherThanTheSumOfThem()
        {
            var unionType = StructDeclParser.Parse(UnionDeclaration);
            var (engine, _, frame) = NewHolder("VAR\n\tu : U_Overlaid;\nEND_VAR", new[] { unionType });

            var result = engine.Evaluate(Parser.ParseExpression("SIZEOF(u)"), frame);

            Assert.Equal(8, result);
        }

        // A union imposes its widest member's alignment on whatever holds it,
        // which nothing about its own size reveals: an 8-byte union that
        // aligned to 1 would put the trailer at 9 and make the holder 10 bytes.
        [Fact]
        public void SizeOf_StructHoldingAUnion_AlignsItToItsWidestMember()
        {
            var unionType = StructDeclParser.Parse(UnionDeclaration);
            var holderType = StructDeclParser.Parse(@"TYPE ST_Holder :
STRUCT
	leadIn : BYTE;
	overlay : U_Overlaid;
	trailer : BYTE;
END_STRUCT
END_TYPE");
            var (engine, _, frame) = NewHolder(
                "VAR\n\th : ST_Holder;\nEND_VAR", new[] { unionType, holderType });

            var result = engine.Evaluate(Parser.ParseExpression("SIZEOF(h)"), frame);

            Assert.Equal(24, result);
        }

        // TwinCAT sizes a dereferenced or indexed operand from the declared
        // pointer type at compile time, so the answer cannot depend on where
        // the pointer points. Leaving it at 0 pins that: reading the size off
        // the pointee at runtime would fault on the null dereference instead.
        [Theory]
        [InlineData("SIZEOF(ipHistory^[0])", 2)]
        [InlineData("SIZEOF(ipHistory^)", 42)]
        [InlineData("SIZEOF(ipHistory^) - SIZEOF(ipHistory^[0])", 40)]
        public void SizeOf_DereferencedPointerToArrayOfElementary_IsSizedFromTheDeclarationEvenWhenUnbound(
            string expression, int expectedBytes)
        {
            var (engine, _, frame) = NewHolder("VAR\n\tipHistory : POINTER TO ARRAY[0..20] OF INT;\nEND_VAR");

            var result = engine.Evaluate(Parser.ParseExpression(expression), frame);

            Assert.Equal(expectedBytes, result);
        }

        [Theory]
        [InlineData("SIZEOF(ipItems^[1])", 8)]
        [InlineData("SIZEOF(ipItems^)", 24)]
        public void SizeOf_DereferencedPointerToArrayOfStruct_UsesTheStructLayout(string expression, int expectedBytes)
        {
            var structType = StructDeclParser.Parse(@"TYPE ST_Msg :
STRUCT
	flag : BYTE;
	count : INT;
	total : DINT;
END_STRUCT
END_TYPE");
            var (engine, _, frame) = NewHolder(
                "VAR\n\tipItems : POINTER TO ARRAY[1..3] OF ST_Msg;\nEND_VAR", new[] { structType });

            var result = engine.Evaluate(Parser.ParseExpression(expression), frame);

            Assert.Equal(expectedBytes, result);
        }

        // The index says which element, never how big one is, so it is not
        // evaluated at all: an index well outside the bounds - of an array
        // that is not even there - must still size as one element. Evaluating
        // it would turn a static size into an out-of-range fault.
        [Theory]
        [InlineData("SIZEOF(ipHistory^[idx])", 4)]
        [InlineData("SIZEOF(buf[idx])", 4)]
        [InlineData("SIZEOF(iaValues[idx])", 8)]
        public void SizeOf_IndexedOperand_IsTheElementSizeWithoutEvaluatingTheIndex(string expression, int expectedBytes)
        {
            var (engine, _, frame) = NewHolder(
                "VAR\n\tidx : INT := 500;\n\tbuf : ARRAY[0..3] OF DINT;\n" +
                "\tipHistory : POINTER TO ARRAY[0..3] OF DINT;\n" +
                "\tiaValues : REFERENCE TO ARRAY[0..3] OF LREAL;\nEND_VAR");

            var result = engine.Evaluate(Parser.ParseExpression(expression), frame);

            Assert.Equal(expectedBytes, result);
        }

        [Fact]
        public void SizeOf_IndexedArrayOfStruct_IsTheStructSize()
        {
            var structType = StructDeclParser.Parse(@"TYPE ST_Point :
STRUCT
	x : DINT;
	y : DINT;
END_STRUCT
END_TYPE");
            var (engine, _, frame) = NewHolder("VAR\n\tpoints : ARRAY[0..9] OF ST_Point;\nEND_VAR", new[] { structType });

            var result = engine.Evaluate(Parser.ParseExpression("SIZEOF(points[3])"), frame);

            Assert.Equal(8, result);
        }

        // A pointer declared through an ALIAS DUT is still a pointer: the
        // dereference has to see through the alias to find what it points at,
        // the same as TypeLayout already does to size the pointer itself.
        [Fact]
        public void SizeOf_DereferencedAliasOfPointerToArray_IsThePointeeSize()
        {
            var aliases = new[] { new KeyValuePair<string, string>("PT_Samples", "POINTER TO ARRAY[0..3] OF WORD") };
            var (engine, _, frame) = NewHolder("VAR\n\tipSamples : PT_Samples;\nEND_VAR", aliases: aliases);

            Assert.Equal(8, engine.Evaluate(Parser.ParseExpression("SIZEOF(ipSamples^)"), frame));
            Assert.Equal(2, engine.Evaluate(Parser.ParseExpression("SIZEOF(ipSamples^[0])"), frame));
        }

        // Only a POINTER TO can be dereferenced and only an ARRAY indexed; an
        // operand that is neither has no declared type to size, and guessing
        // one would hand MEMCPY a plausible wrong byte count.
        [Theory]
        [InlineData("SIZEOF(n^)")]
        [InlineData("SIZEOF(n[0])")]
        [InlineData("SIZEOF(n + 1)")]
        public void SizeOf_OperandWithNoDeclaredType_IsRefused(string expression)
        {
            var (engine, _, frame) = NewHolder("VAR\n\tn : INT;\nEND_VAR");

            var ex = Assert.Throws<NotSupportedException>(
                () => engine.Evaluate(Parser.ParseExpression(expression), frame));

            Assert.Contains("SIZEOF()", ex.Message);
        }

        // A refused operand is a fault in the PLC code under test, so it has
        // to name the body line holding it - otherwise the author is left
        // searching every SIZEOF in the POU for the one that was rejected.
        [Fact]
        public void SizeOf_UnsupportedOperandInASuiteBody_ReportsItsBodyLine()
        {
            var suite = new PouAst(
                "FB_SizeOfSuite",
                "TcUnit.FB_TestSuite",
                "VAR\n\tn : INT;\nEND_VAR",
                "n := 1;\nn := SIZEOF(n + 1);",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { suite }));

            var ex = Assert.Throws<PlcSourceLocationException>(() => engine.RunSuite("FB_SizeOfSuite"));

            Assert.Equal(2, ex.BodyLine);
            var inner = Assert.IsType<NotSupportedException>(ex.InnerException);
            Assert.Contains("SIZEOF()", inner.Message);
        }

        // Three deliberately different widths: all-same-width members would
        // leave "widest member" indistinguishable from "first" or "last".
        private const string UnionDeclaration = @"TYPE U_Overlaid :
UNION
	asWord : WORD;
	asBytes : ARRAY[0..3] OF BYTE;
	asLong : LWORD;
END_UNION
END_TYPE";
    }
}
