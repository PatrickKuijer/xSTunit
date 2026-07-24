using TcXunit.Parser;
using Xunit;

namespace TcXunit.Parser.Tests
{
    // TcXunit-71o: a .TcGVL file's root is <GVL Name="X">, not <POU> -
    // TcGvlParser is the analogous entry point to TcPouParser/TcDutParser
    // for that root element.
    public class TcGvlParserTests
    {
        [Fact]
        public void Parse_SimpleGvl_ReadsNameAndDeclaration()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <GVL Name=""gFrameworkTemp"" Id=""{3459fbaa-da58-091b-1a27-116dfc4ca202}"">
    <Declaration><![CDATA[{attribute 'qualified_only'}
{attribute 'global_init_slot' := '49989'}
VAR_GLOBAL
	stMachine							: uMachine;
	stUnit								: uUnit;
END_VAR]]></Declaration>
  </GVL>
</TcPlcObject>";

            var ast = TcGvlParser.Parse(xml);

            Assert.Equal("gFrameworkTemp", ast.Name);
            Assert.Contains("VAR_GLOBAL", ast.DeclarationText);
            Assert.Contains("stMachine", ast.DeclarationText);
        }

        [Fact]
        public void Parse_GvlWithConstantModifier_ReadsDeclaration()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <GVL Name=""cFramework"" Id=""{addd82e8-d17d-02ff-179d-61e9ed5308fa}"" ParameterList=""True"">
    <Declaration><![CDATA[VAR_GLOBAL CONSTANT
	MAX_UNITS_PER_MACHINE : UINT := 16;
END_VAR]]></Declaration>
  </GVL>
</TcPlcObject>";

            var ast = TcGvlParser.Parse(xml);

            Assert.Equal("cFramework", ast.Name);
            Assert.Contains("VAR_GLOBAL CONSTANT", ast.DeclarationText);
        }
    }
}
