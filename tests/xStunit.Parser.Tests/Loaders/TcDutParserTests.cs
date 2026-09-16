using System.Xml;
using xStunit.Parser;
using Xunit;

namespace xStunit.Parser.Tests
{
    // The STRUCT/ENUM/EXTENDS cases below all pin the same line: TcDutParser
    // extracts Name plus the declaration text verbatim and never inspects that
    // text. Teaching it to recognize - or reject - a DUT flavour here would
    // strand every downstream reader that parses the declaration itself.
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

        // Each of the three structural gaps below reaches the reader as a
        // per-file skip line, so the message has to name the element or
        // attribute that was absent - a dereference failure names nothing.
        [Fact]
        public void Parse_XmlWithoutDutElement_ThrowsNamingTheMissingElement()
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

            var ex = Assert.Throws<XmlException>(() => TcDutParser.Parse(xml));
            Assert.Contains("DUT", ex.Message);
        }

        [Fact]
        public void Parse_DutMissingNameAttribute_ThrowsNamingTheMissingAttribute()
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

            var ex = Assert.Throws<XmlException>(() => TcDutParser.Parse(xml));
            Assert.Contains("Name", ex.Message);
        }

        [Fact]
        public void Parse_DutMissingDeclarationElement_ThrowsNamingTheMissingElement()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <DUT Name=""ST_NoDeclaration"" Id=""{a1b2c3d4-0006-4a1a-8b1b-0000000000f9}"">
  </DUT>
</TcPlcObject>";

            var ex = Assert.Throws<XmlException>(() => TcDutParser.Parse(xml));
            Assert.Contains("Declaration", ex.Message);
        }
    }
}
