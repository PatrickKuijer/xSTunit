using System.Linq;
using System.Xml;
using xStunit.Parser;
using Xunit;

namespace xStunit.Parser.Tests
{
    // An .TcIO carries the same Declaration/Method/Property shape a .TcPOU
    // does under a different root element, and everything downstream of the
    // parse - conformance checking, the unassigned-reference fault - reads
    // only what these assertions pin.
    public class TcItfParserTests
    {
        private const string ItfXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <Itf Name=""I_Sensor"" Id=""{00000000-0000-0000-0000-000000000001}"">
    <Declaration><![CDATA[INTERFACE I_Sensor]]></Declaration>
    <Method Name=""Reset"" Id=""{00000000-0000-0000-0000-000000000002}"">
      <Declaration><![CDATA[METHOD Reset : BOOL]]></Declaration>
      <Implementation>
        <ST><![CDATA[]]></ST>
      </Implementation>
    </Method>
    <Property Name=""Active"" Id=""{00000000-0000-0000-0000-000000000003}"">
      <Declaration><![CDATA[PROPERTY Active : BOOL]]></Declaration>
      <Get Name=""Get"" Id=""{00000000-0000-0000-0000-000000000004}"">
        <Declaration><![CDATA[]]></Declaration>
        <Implementation>
          <ST><![CDATA[]]></ST>
        </Implementation>
      </Get>
    </Property>
  </Itf>
</TcPlcObject>";

        [Fact]
        public void Parse_ReadsNameDeclarationAndMembersFromTheItfRoot()
        {
            var itf = TcItfParser.Parse(ItfXml);

            Assert.Equal("I_Sensor", itf.Name);
            Assert.Contains("INTERFACE I_Sensor", itf.DeclarationText);
            Assert.Equal(new[] { "Reset" }, itf.Methods.Select(m => m.Name));
            Assert.Equal("METHOD Reset : BOOL", Assert.Single(itf.Methods).DeclarationText);
        }

        // Which accessors an interface demands is the whole content of a
        // property in a contract, so a get-only property must not read back as
        // one that also demands a setter.
        [Fact]
        public void Parse_PropertyWithGetOnly_ReportsGetPresentAndSetAbsent()
        {
            var property = Assert.Single(TcItfParser.Parse(ItfXml).Properties);

            Assert.Equal("Active", property.Name);
            Assert.True(property.HasGet);
            Assert.False(property.HasSet);
        }

        // TwinCAT is inconsistent about writing the empty <Implementation>
        // element for an interface member across versions, and a method header
        // is the only thing a contract needs from one, so a missing body
        // element must not sink the file.
        [Fact]
        public void Parse_MethodWithoutAnImplementationElement_StillParses()
        {
            var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <Itf Name=""I_Bare"" Id=""{00000000-0000-0000-0000-000000000001}"">
    <Declaration><![CDATA[INTERFACE I_Bare]]></Declaration>
    <Method Name=""Enable"" Id=""{00000000-0000-0000-0000-000000000002}"">
      <Declaration><![CDATA[METHOD Enable : BOOL]]></Declaration>
    </Method>
  </Itf>
</TcPlcObject>";

            var method = Assert.Single(TcItfParser.Parse(xml).Methods);

            Assert.Equal("Enable", method.Name);
            Assert.Equal(string.Empty, method.ImplementationText);
        }

        // The real corpus, not a hand-written minimum: a property-heavy
        // interface with mixed get-only and get/set members is what the
        // miniload pilot set actually ships.
        [Fact]
        public void Parse_MiniloadSensorInterfaceFixture_ReadsEveryDeclaredMember()
        {
            var itf = TcItfParser.Parse(
                System.IO.File.ReadAllText(
                    System.IO.Path.Combine(TestFixtures.MiniloadSensorFixtureDir(), "I_DigitalInput.TcIO")));

            Assert.Equal("I_DigitalInput", itf.Name);
            Assert.Empty(itf.Methods);
            Assert.Equal(
                new[] { "Active", "DebounceTime", "Inverted", "Simulation", "SimulatedInput", "TimeActive", "TimeInactive" },
                itf.Properties.Select(p => p.Name));

            var active = itf.Properties.Single(p => p.Name == "Active");
            Assert.True(active.HasGet);
            Assert.False(active.HasSet);

            var debounce = itf.Properties.Single(p => p.Name == "DebounceTime");
            Assert.True(debounce.HasGet);
            Assert.True(debounce.HasSet);
        }

        // A .TcIO that is truncated, or is some other file type entirely, has
        // to name the element or attribute it is missing: the parse failure
        // surfaces as a per-file skip line and a dereference names nothing.
        [Theory]
        [InlineData("<TcPlcObject Version=\"1.1.0.1\" />", "Itf")]
        [InlineData("<TcPlcObject Version=\"1.1.0.1\"><Itf Id=\"x\" /></TcPlcObject>", "Name")]
        [InlineData("<TcPlcObject Version=\"1.1.0.1\"><Itf Name=\"I_Truncated\" /></TcPlcObject>", "Declaration")]
        public void Parse_StructurallyIncompleteFile_ThrowsNamingTheMissingPart(string xml, string expectedInMessage)
        {
            var ex = Assert.Throws<XmlException>(() => TcItfParser.Parse(xml));

            Assert.Contains(expectedInMessage, ex.Message);
        }

        // The missing <Implementation> above is tolerated; a missing
        // <Declaration> is not. A method header is the entire content of an
        // interface member, so a file without one is truncated, and loading it
        // as a member with no signature hides that.
        [Fact]
        public void Parse_MethodWithoutADeclarationElement_ThrowsNamingTheMissingElement()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <Itf Name=""I_Truncated"" Id=""{00000000-0000-0000-0000-00000000000e}"">
    <Declaration><![CDATA[INTERFACE I_Truncated]]></Declaration>
    <Method Name=""Enable"" Id=""{00000000-0000-0000-0000-00000000000f}"" />
  </Itf>
</TcPlcObject>";

            var ex = Assert.Throws<XmlException>(() => TcItfParser.Parse(xml));

            Assert.Contains("Declaration", ex.Message);
        }

        // Same tolerance as the method case above, on the accessor path: an
        // accessor carries no body an interface cares about, so an empty <Get>
        // still has to report the getter the contract demands.
        [Fact]
        public void Parse_PropertyAccessorWithoutImplementationElement_StillParses()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <Itf Name=""I_Terse"" Id=""{00000000-0000-0000-0000-00000000000a}"">
    <Declaration><![CDATA[INTERFACE I_Terse]]></Declaration>
    <Property Name=""Ready"" Id=""{00000000-0000-0000-0000-00000000000c}"">
      <Declaration><![CDATA[PROPERTY Ready : BOOL]]></Declaration>
      <Get Name=""Get"" Id=""{00000000-0000-0000-0000-00000000000d}"" />
    </Property>
  </Itf>
</TcPlcObject>";

            var property = Assert.Single(TcItfParser.Parse(xml).Properties);

            Assert.True(property.HasGet);
            Assert.False(property.HasSet);
        }
    }
}
