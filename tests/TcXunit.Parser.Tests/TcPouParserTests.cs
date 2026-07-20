using TcXunit.Parser;
using Xunit;

namespace TcXunit.Parser.Tests
{
    public class TcPouParserTests
    {
        [Fact]
        public void Parse_MinimalFunctionBlock_ReadsNameDeclarationAndImplementation()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <POU Name=""FB_AddLrealInt"" Id=""{d98c697c-4a7c-44d3-a5f9-ca6e839b6f15}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_AddLrealInt
VAR_INPUT
	lrealValue : LREAL;
	intValue : INT;
END_VAR
VAR_OUTPUT
	result : LREAL;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[result := lrealValue + TO_LREAL(intValue);]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

            var ast = TcPouParser.Parse(xml);

            Assert.Equal("FB_AddLrealInt", ast.Name);
            Assert.Contains("FUNCTION_BLOCK FB_AddLrealInt", ast.DeclarationText);
            Assert.Equal("result := lrealValue + TO_LREAL(intValue);", ast.ImplementationText);
            Assert.Null(ast.BaseTypeName);
        }

        [Fact]
        public void Parse_ExtendsAnotherFunctionBlock_ReadsBaseTypeName()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_ClampedCounter"" Id=""{a1b2c3d4-0002-4a1a-8b1b-000000000001}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_ClampedCounter EXTENDS FB_Counter
VAR
	ceiling : INT;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

            var ast = TcPouParser.Parse(xml);

            Assert.Equal("FB_ClampedCounter", ast.Name);
            Assert.Equal("FB_Counter", ast.BaseTypeName);
        }

        [Fact]
        public void Parse_ExtendsLibraryQualifiedFunctionBlock_ReadsQualifiedBaseTypeName()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_CounterTests"" Id=""{a1b2c3d4-0003-4a1a-8b1b-000000000001}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_CounterTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

            var ast = TcPouParser.Parse(xml);

            Assert.Equal("TcUnit.FB_TestSuite", ast.BaseTypeName);
        }
    }
}
