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
            var gvl = doc.Root.Element("GVL");
            var name = gvl.Attribute("Name").Value;
            var declarationText = gvl.Element("Declaration").Value;
            return new GvlAst(name, declarationText);
        }
    }
}
