using System.Xml.Linq;

namespace TcXunit.Parser
{
    // Parses a .TcGVL file's XML into a GvlAst. A GVL's root element is
    // <GVL Name="X"> alongside <POU>, not a variant of it (TcXunit-71o) -
    // TcPouParser.Parse hardcoding <POU> is exactly why GVLs couldn't be
    // parsed before this existed.
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
