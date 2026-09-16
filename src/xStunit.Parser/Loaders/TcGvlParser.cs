using System.Xml;
using System.Xml.Linq;

namespace xStunit.Parser
{
    // Separate from TcPouParser because a GVL's root element is <GVL>
    // alongside <POU>, not a variant of it.
    public static class TcGvlParser
    {
        public static GvlAst Parse(string xml)
        {
            var doc = XDocument.Parse(xml);
            var gvl = doc.Root?.Element("GVL")
                ?? throw new XmlException("the file has no <GVL> element.");
            var name = gvl.Attribute("Name")?.Value
                ?? throw new XmlException("<GVL> has no Name attribute.");
            var declarationText = (gvl.Element("Declaration")
                ?? throw new XmlException($"{name} has no <Declaration> element.")).Value;
            return new GvlAst(name, declarationText);
        }
    }
}
