using System.Xml.Linq;

namespace xStunit.Parser
{
    /// <summary>
    /// A parsed .TcDUT file's identity plus its raw declaration text.
    /// </summary>
    /// <remarks>
    /// <see cref="DeclarationText"/> is the "TYPE Name : STRUCT ...
    /// END_STRUCT END_TYPE" body. Interpreting that text into a StructAst is
    /// left to callers (xStunit.Interpreter's StructDeclParser), since DUT
    /// files can also declare ENUMs/aliases/unions that this parser project
    /// has no model for yet.
    /// </remarks>
    public readonly struct DutAst
    {
        /// <summary>Constructs a DUT AST from its parsed name and raw declaration text.</summary>
        public DutAst(string name, string declarationText)
        {
            Name = name;
            DeclarationText = declarationText;
        }

        /// <summary>The DUT's name, e.g. "ST_Point".</summary>
        public string Name { get; }

        /// <summary>The raw "TYPE ... END_TYPE" declaration text, unparsed.</summary>
        public string DeclarationText { get; }
    }

    /// <summary>Parses a .TcDUT file's XML into a <see cref="DutAst"/>.</summary>
    public static class TcDutParser
    {
        /// <summary>Parses the given .TcDUT XML content into a <see cref="DutAst"/>.</summary>
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
