using System.Linq;
using xStunit.Interpreter;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // pack_mode is the one attribute pragma the parser does not drop: it is
    // captured onto StructAst.PackMode because it changes the struct's memory
    // layout, which SIZEOF and byte-level assertions depend on.
    public class StructDeclParserTests
    {
        [Fact]
        public void Parse_NoPackModeAttribute_DefaultsToZero()
        {
            var structAst = StructDeclParser.Parse(@"TYPE ST_Point :
STRUCT
	x : DINT;
END_STRUCT
END_TYPE");

            Assert.Equal(0, structAst.PackMode);
        }

        [Fact]
        public void Parse_PackModeAttribute_IsCapturedOnStructAst()
        {
            var structAst = StructDeclParser.Parse(@"{attribute 'pack_mode' := '1'}
TYPE uGadgetSettingValue :
STRUCT
	nIndex : UINT;
	rValue : LREAL;
END_STRUCT
END_TYPE");

            Assert.Equal(1, structAst.PackMode);
        }

        [Fact]
        public void Parse_PackModeAttributeWithLeadingCommentLine_IsStillCaptured()
        {
            var structAst = StructDeclParser.Parse(@"{attribute 'pack_mode' := '1'}
// Value channel wire record
TYPE uGadgetSettingValue :
STRUCT
	nIndex : UINT;
	rValue : LREAL;
END_STRUCT
END_TYPE");

            Assert.Equal(1, structAst.PackMode);
            Assert.Equal("uGadgetSettingValue", structAst.Name);
        }

        [Fact]
        public void Parse_PackModeAttributeBehindBlockCommentHeader_IsStillCaptured()
        {
            // The pragma shares the header comment's territory, so whatever
            // skips a "(* ... *)" header on the way to the TYPE line must not
            // take the layout attribute with it.
            var structAst = StructDeclParser.Parse(@"(*
    Wire record handed to the HMI, packed to match the published layout.
*)
{attribute 'pack_mode' := '1'}
TYPE uGadgetSettingValue :
STRUCT
	nIndex : UINT;
	rValue : LREAL;
END_STRUCT
END_TYPE");

            Assert.Equal(1, structAst.PackMode);
            Assert.Equal("uGadgetSettingValue", structAst.Name);
        }

        // The UNION keyword is the only thing separating a type whose fields
        // are laid end to end from one whose fields are all at offset 0, and it
        // survives nowhere else - a .tmc records the layout, never the keyword.
        [Fact]
        public void Parse_UnionDeclaration_KeepsItsFieldsAndIsMarkedAUnion()
        {
            var unionAst = StructDeclParser.Parse(@"TYPE U_Overlaid :
UNION
	asWord : WORD;
	asLong : LWORD;
END_UNION
END_TYPE");

            Assert.True(unionAst.IsUnion);
            Assert.Equal("U_Overlaid", unionAst.Name);
            Assert.Equal(new[] { "asWord", "asLong" }, unionAst.Fields.Select(f => f.Name).ToArray());
        }

        [Fact]
        public void Parse_StructDeclaration_IsNotMarkedAUnion()
        {
            var structAst = StructDeclParser.Parse(@"TYPE ST_Point :
STRUCT
	x : DINT;
END_STRUCT
END_TYPE");

            Assert.False(structAst.IsUnion);
        }
    }
}
