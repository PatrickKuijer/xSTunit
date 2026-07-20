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
        }
    }
}
