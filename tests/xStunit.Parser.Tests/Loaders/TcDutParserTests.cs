using System;
using System.Xml;
using xStunit.Parser;
using Xunit;

namespace xStunit.Parser.Tests
{
    // TcXunit-w5x.15.6: a .TcDUT file's root is <DUT Name="X">, not <POU> -
    // TcDutParser is the analogous entry point to TcPouParser/TcGvlParser for
    // that root element. TcDutParser itself only extracts Name + the raw
    // Declaration text; it deliberately does not care whether that text is a
    // STRUCT, ENUM, alias or union, or whether a STRUCT uses "EXTENDS Base" -
    // interpreting the declaration text is left to callers (e.g.
    // xStunit.Interpreter's StructDeclParser/DutStructLoader), since this
    // parser project has no model for ENUM/alias/union DUTs or struct
    // inheritance yet (see the DutAst doc comment in TcDutParser.cs).
    public class TcDutParserTests
    {
        [Fact]
        public void Parse_StructDut_ReadsNameAndDeclarationText()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <DUT Name=""ST_Point"" Id=""{a1b2c3d4-0006-4a1a-8b1b-0000000000ff}"">
    <Declaration><![CDATA[TYPE ST_Point :
STRUCT
	x : REAL;
	y : REAL;
END_STRUCT
END_TYPE]]></Declaration>
  </DUT>
</TcPlcObject>";

            var ast = TcDutParser.Parse(xml);

            Assert.Equal("ST_Point", ast.Name);
            Assert.Contains("TYPE ST_Point :", ast.DeclarationText);
            Assert.Contains("STRUCT", ast.DeclarationText);
            Assert.Contains("x : REAL;", ast.DeclarationText);
        }

        [Fact]
        public void Parse_EnumDut_ReadsNameAndDeclarationTextVerbatimWithoutInterpretingIt()
        {
            // TcDutParser has no model for ENUM DUTs - it must still extract
            // Name/DeclarationText without attempting to interpret the
            // content or skip based on it (that's DutStructLoader's job via
            // IsStructDeclaration).
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <DUT Name=""E_Color"" Id=""{a1b2c3d4-0006-4a1a-8b1b-0000000000fe}"">
    <Declaration><![CDATA[TYPE E_Color :
(
	Red,
	Green,
	Blue
);
END_TYPE]]></Declaration>
  </DUT>
</TcPlcObject>";

            var ast = TcDutParser.Parse(xml);

            Assert.Equal("E_Color", ast.Name);
            Assert.Contains("TYPE E_Color :", ast.DeclarationText);
            Assert.Contains("Red,", ast.DeclarationText);
        }

        [Fact]
        public void Parse_StructExtendsBaseDut_ReadsNameAndDeclarationTextIncludingExtendsClause()
        {
            // Struct inheritance ("TYPE X EXTENDS Base :") is likewise just
            // raw text to TcDutParser - the EXTENDS clause is preserved
            // verbatim in DeclarationText for whatever downstream caller
            // decides how to handle it (currently: StructDeclParser can't
            // find a name after "EXTENDS Base" in its TYPE-header regex, so
            // callers like DutStructLoader end up skipping it).
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <DUT Name=""ST_Derived"" Id=""{a1b2c3d4-0006-4a1a-8b1b-0000000000fd}"">
    <Declaration><![CDATA[TYPE ST_Derived EXTENDS ST_Base :
STRUCT
	extra : INT;
END_STRUCT
END_TYPE]]></Declaration>
  </DUT>
</TcPlcObject>";

            var ast = TcDutParser.Parse(xml);

            Assert.Equal("ST_Derived", ast.Name);
            Assert.Contains("TYPE ST_Derived EXTENDS ST_Base :", ast.DeclarationText);
            Assert.Contains("extra : INT;", ast.DeclarationText);
        }

        [Fact]
        public void Parse_MalformedXml_ThrowsXmlException()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <DUT Name=""ST_Broken"" Id=""{a1b2c3d4-0006-4a1a-8b1b-0000000000fc}"">
    <Declaration><![CDATA[TYPE ST_Broken :
STRUCT
	value : INT;
END_STRUCT
END_TYPE]]></Declaration>
</TcPlcObject>";

            Assert.Throws<XmlException>(() => TcDutParser.Parse(xml));
        }

        [Fact]
        public void Parse_XmlWithoutDutElement_ThrowsNullReferenceException()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <POU Name=""FB_NotADut"" Id=""{a1b2c3d4-0006-4a1a-8b1b-0000000000fb}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_NotADut]]></Declaration>
    <Implementation>
      <ST><![CDATA[]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

            Assert.Throws<NullReferenceException>(() => TcDutParser.Parse(xml));
        }

        [Fact]
        public void Parse_DutMissingNameAttribute_ThrowsNullReferenceException()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <DUT Id=""{a1b2c3d4-0006-4a1a-8b1b-0000000000fa}"">
    <Declaration><![CDATA[TYPE ST_Nameless :
STRUCT
	value : INT;
END_STRUCT
END_TYPE]]></Declaration>
  </DUT>
</TcPlcObject>";

            Assert.Throws<NullReferenceException>(() => TcDutParser.Parse(xml));
        }

        [Fact]
        public void Parse_DutMissingDeclarationElement_ThrowsNullReferenceException()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <DUT Name=""ST_NoDeclaration"" Id=""{a1b2c3d4-0006-4a1a-8b1b-0000000000f9}"">
  </DUT>
</TcPlcObject>";

            Assert.Throws<NullReferenceException>(() => TcDutParser.Parse(xml));
        }
    }
}
