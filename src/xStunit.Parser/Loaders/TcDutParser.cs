using System.Xml.Linq;

namespace xStunit.Parser
{
    /// <summary>
    /// A parsed .TcDUT file's name plus its raw, UNPARSED declaration text -
    /// the "TYPE Name : STRUCT ... END_STRUCT END_TYPE" body.
    /// </summary>
    /// <remarks>
    /// Left unparsed because a DUT file can also declare ENUMs, aliases and
    /// unions, none of which this project has a model for - callers interpret
    /// the text as far as they need (xStunit.Interpreter's StructDeclParser).
    /// </remarks>
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
