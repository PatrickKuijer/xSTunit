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
        public void Parse_NoPackModeAttribute_LeavesPackModeUnset()
        {
            var structAst = StructDeclParser.Parse(@"TYPE ST_Point :
STRUCT
	x : DINT;
END_STRUCT
END_TYPE");

            Assert.Null(structAst.PackMode);
        }

        // An explicit '0' is a pragma TwinCAT honours as "no gaps", so it must
        // stay distinguishable from no pragma at all, which means natural
        // alignment.
        [Fact]
        public void Parse_ExplicitPackModeZero_IsCapturedRatherThanReadAsAbsent()
        {
            var structAst = StructDeclParser.Parse(@"{attribute 'pack_mode' := '0'}
TYPE ST_Packed0 :
STRUCT
	bFlag : BOOL;
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

        // TYPE/STRUCT/UNION are IEC 61131-3 keywords and TwinCAT accepts them
        // in any case. Read only in upper case, a lower-case STRUCT DUT is
        // never routed to DutStructLoader, and every field access on it fails
        // as though the type did not exist.
        [Theory]
        [InlineData("type ST_Point :\nstruct\n\tx : DINT;\n\ty : DINT;\nend_struct\nend_type", false)]
        [InlineData("Type ST_Point :\nStruct\n\tx : DINT;\n\ty : DINT;\nEnd_Struct\nEnd_Type", false)]
        [InlineData("type ST_Point :\nunion\n\tx : DINT;\n\ty : DINT;\nend_union\nend_type", true)]
        public void Parse_TypeAndBodyKeywordsInAnyCase_ReadsNameFieldsAndBody(string declaration, bool isUnion)
        {
            var structAst = StructDeclParser.Parse(declaration);

            Assert.Equal(isUnion ? StructDeclParser.UnionBody : StructDeclParser.StructBody, StructDeclParser.DeclaredBody(declaration));
            Assert.Equal(!isUnion, DutStructLoader.IsStructDeclaration(declaration));
            Assert.Equal("ST_Point", structAst.Name);
            Assert.Equal(isUnion, structAst.IsUnion);
            Assert.Equal(new[] { "x", "y" }, structAst.Fields.Select(f => f.Name).ToArray());
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

        // DeclaredBody already recognises STRUCT on the header line, so Parse
        // must deliver its fields too rather than an empty struct.
        [Fact]
        public void Parse_StructOnTypeHeaderLine_ReadsFields()
        {
            var structAst = StructDeclParser.Parse("TYPE ST_Point : STRUCT\n\tx : DINT;\n\ty : DINT;\nEND_STRUCT\nEND_TYPE");

            Assert.Equal("ST_Point", structAst.Name);
            Assert.Equal(new[] { "x", "y" }, structAst.Fields.Select(f => f.Name));
        }

        [Fact]
        public void Parse_UnionOnTypeHeaderLine_ReadsFieldsAsUnion()
        {
            var structAst = StructDeclParser.Parse("TYPE U_Word : union\n\tw : WORD;\n\tb : BYTE;\nEND_UNION\nEND_TYPE");

            Assert.True(structAst.IsUnion);
            Assert.Equal(new[] { "w", "b" }, structAst.Fields.Select(f => f.Name));
        }
    }
}
