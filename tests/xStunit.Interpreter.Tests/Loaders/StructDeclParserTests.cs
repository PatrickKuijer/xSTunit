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
    }
}
