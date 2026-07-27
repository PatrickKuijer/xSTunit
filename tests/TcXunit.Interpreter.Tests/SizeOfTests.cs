using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-l1x: SIZEOF() native call. Byte-size computation for
    // scalars/STRING/ARRAY/STRUCT, assuming TwinCAT's default
    // natural-alignment struct packing (see Engine.SizeOf.cs remarks).
    public class SizeOfTests
    {
        private static (Engine Engine, FbInstance Instance, Frame Frame) NewHolder(
            string varBlock, IEnumerable<StructAst> structTypes = null)
        {
            var fb = new PouAst("FB_Holder", null, varBlock, "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }, structTypes));
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
    }
}
