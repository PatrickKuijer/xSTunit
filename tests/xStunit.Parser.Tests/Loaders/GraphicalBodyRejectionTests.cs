using System.Xml;
using xStunit.Parser;
using Xunit;

namespace xStunit.Parser.Tests
{
    // A POU drawn in LD/FBD/SFC/CFC/IL is well-formed and TwinCAT-valid, but
    // TwinCAT writes its body as a network list instead of <ST>, so there is no
    // ST text to interpret. The parser has to say so by name: dereferencing the
    // absent <ST> turns every graphical file in a tree into a bare
    // NullReferenceException in the skip line, naming no construct at all.
    public class GraphicalBodyRejectionTests
    {
        [Fact]
        public void Parse_PouBodyInGraphicalLanguage_RejectsNamingThePouAndTheLanguage()
        {
            var ex = Assert.Throws<TcPouRejectedException>(() => TcPouParser.Parse(GraphicalPouXml));

            Assert.Equal(
                "'FB_Graphical' has no ST body (implementation language 'NWL'), " +
                "which is outside the v1 parse subset (not yet implemented).",
                ex.Message);
        }

        // The trigger is an <Implementation> with no <ST> child, whatever else
        // it does or does not carry - an empty one has to read the same way
        // rather than fall back to the dereference.
        [Fact]
        public void Parse_PouImplementationWithNoChildren_RejectsRatherThanDereferencing()
        {
            var ex = Assert.Throws<TcPouRejectedException>(
                () => TcPouParser.Parse(GraphicalPouXml.Replace(NetworkList, "")));

            Assert.Contains("'FB_Graphical' has no ST body", ex.Message);
            Assert.Contains("outside the v1 parse subset", ex.Message);
        }

        // Latent where the POU body case is not: an ST-bodied FB with a single
        // graphical METHOD reaches a different dereference on the same absent
        // <ST>, so fixing only the POU site leaves the next tree with the
        // identical bare NRE.
        [Fact]
        public void Parse_MethodBodyInGraphicalLanguage_RejectsNamingTheMethodAndTheLanguage()
        {
            var ex = Assert.Throws<TcPouRejectedException>(() => TcPouParser.Parse(GraphicalMethodXml));

            Assert.Equal(
                "'DrawnInLadder' has no ST body (implementation language 'NWL'), " +
                "which is outside the v1 parse subset (not yet implemented).",
                ex.Message);
        }

        // Chaining past the absent <ST> to null reports a graphical accessor
        // downstream as an accessor the POU never declared - no crash, but a
        // diagnostic pointing at the wrong thing.
        [Fact]
        public void Parse_PropertyAccessorInGraphicalLanguage_RejectsNamingTheAccessor()
        {
            var ex = Assert.Throws<TcPouRejectedException>(() => TcPouParser.Parse(GraphicalAccessorXml));

            Assert.Contains("'nCounter.Get' has no ST body", ex.Message);
            Assert.Contains("'NWL'", ex.Message);
        }

        // TwinCAT omits <Implementation> on an accessor in some versions, so
        // its absence is a body the POU never wrote - not a broken file. Only
        // an <Implementation> that IS present and holds no <ST> means a
        // graphical accessor; treating the two alike would skip whole files
        // over a shape TwinCAT emits on purpose.
        [Fact]
        public void Parse_AccessorWithoutAnImplementationElement_StillParses()
        {
            var pou = TcPouParser.Parse(AccessorWithoutImplementationXml);

            var property = Assert.Single(pou.Properties);
            Assert.False(property.HasGet);
        }

        // Truncated or foreign XML is a broken file, not an unsupported
        // language, and must still name the part that is missing rather than
        // report that something, somewhere, was null.
        [Theory]
        [InlineData("<TcPlcObject Version=\"1.1.0.1\" />", "POU")]
        [InlineData("<TcPlcObject Version=\"1.1.0.1\"><POU Id=\"x\" /></TcPlcObject>", "Name")]
        [InlineData("<TcPlcObject Version=\"1.1.0.1\"><POU Name=\"FB_Truncated\" /></TcPlcObject>", "Declaration")]
        public void Parse_StructurallyIncompleteFile_ThrowsNamingTheMissingPart(string xml, string expectedInMessage)
        {
            var ex = Assert.Throws<XmlException>(() => TcPouParser.Parse(xml));

            Assert.Contains(expectedInMessage, ex.Message);
        }

        private const string NetworkList = @"<NWL>
        <XmlArchive>
          <Data>
            <o xml:space=""preserve"" t=""NWLImplementationObject"">
              <v n=""NetworkListComment"">""""</v>
            </o>
          </Data>
        </XmlArchive>
      </NWL>";

        private const string GraphicalPouXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_Graphical"" Id=""{00000000-0000-0000-0000-000000000001}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_Graphical
VAR
	bFlag : BOOL;
END_VAR
]]></Declaration>
    <Implementation>
      " + NetworkList + @"
    </Implementation>
  </POU>
</TcPlcObject>";

        private const string GraphicalMethodXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_MixedLanguages"" Id=""{00000000-0000-0000-0000-000000000002}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_MixedLanguages
VAR
	bFlag : BOOL;
END_VAR
]]></Declaration>
    <Implementation>
      <ST><![CDATA[bFlag := TRUE;]]></ST>
    </Implementation>
    <Method Name=""DrawnInLadder"" Id=""{00000000-0000-0000-0000-000000000003}"">
      <Declaration><![CDATA[METHOD PUBLIC DrawnInLadder : BOOL]]></Declaration>
      <Implementation>
        " + NetworkList + @"
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";

        private const string GraphicalAccessorXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_GraphicalAccessor"" Id=""{00000000-0000-0000-0000-000000000004}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_GraphicalAccessor
VAR
	snCounter : UINT;
END_VAR
]]></Declaration>
    <Implementation>
      <ST><![CDATA[]]></ST>
    </Implementation>
    <Property Name=""nCounter"" Id=""{00000000-0000-0000-0000-000000000005}"">
      <Declaration><![CDATA[PROPERTY PUBLIC nCounter : UINT]]></Declaration>
      <Get Name=""Get"" Id=""{00000000-0000-0000-0000-000000000006}"">
        <Declaration><![CDATA[]]></Declaration>
        <Implementation>
          " + NetworkList + @"
        </Implementation>
      </Get>
    </Property>
  </POU>
</TcPlcObject>";

        private const string AccessorWithoutImplementationXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_BodilessAccessor"" Id=""{00000000-0000-0000-0000-000000000007}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_BodilessAccessor
VAR
	snCounter : UINT;
END_VAR
]]></Declaration>
    <Implementation>
      <ST><![CDATA[]]></ST>
    </Implementation>
    <Property Name=""nCounter"" Id=""{00000000-0000-0000-0000-000000000008}"">
      <Declaration><![CDATA[PROPERTY PUBLIC nCounter : UINT]]></Declaration>
      <Get Name=""Get"" Id=""{00000000-0000-0000-0000-000000000009}"">
        <Declaration><![CDATA[]]></Declaration>
      </Get>
    </Property>
  </POU>
</TcPlcObject>";
    }
}
