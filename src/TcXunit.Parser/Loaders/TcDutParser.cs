using System.Xml.Linq;

namespace TcXunit.Parser
{
    // A parsed .TcDUT file's identity + raw declaration text (the "TYPE Name
    // : STRUCT ... END_STRUCT END_TYPE" body). Interpreting that declaration
    // text into a StructAst is left to callers (TcXunit.Interpreter's
    // StructDeclParser) since DUT files can also declare ENUMs/aliases/unions
    // that this parser project has no model for yet.
    public readonly struct DutAst
    {
        public DutAst(string name, string declarationText)
        {
            Name = name;
            DeclarationText = declarationText;
        }

        public string Name { get; }
        public string DeclarationText { get; }
    }

    public static class TcDutParser
    {
        public static DutAst Parse(string xml)
        {
            var doc = XDocument.Parse(xml);
            var dut = doc.Root.Element("DUT");
            var name = dut.Attribute("Name").Value;
            var declarationText = dut.Element("Declaration").Value;
            return new DutAst(name, declarationText);
        }
    }
}
