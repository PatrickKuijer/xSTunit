using TcXunit.Interpreter;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-eub: the {attribute 'pack_mode' := 'N'} pragma preceding a
    // STRUCT DUT's TYPE header is captured onto StructAst.PackMode instead
    // of being silently dropped like an ordinary attribute pragma.
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
TYPE uRemoteParamValue :
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
TYPE uRemoteParamValue :
STRUCT
	nIndex : UINT;
	rValue : LREAL;
END_STRUCT
END_TYPE");

            Assert.Equal(1, structAst.PackMode);
            Assert.Equal("uRemoteParamValue", structAst.Name);
        }
    }
}
