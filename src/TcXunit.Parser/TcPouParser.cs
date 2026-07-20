using System.Xml.Linq;

namespace TcXunit.Parser
{
    public static class TcPouParser
    {
        public static PouAst Parse(string xml)
        {
            var doc = XDocument.Parse(xml);
            var pou = doc.Root.Element("POU");
            var name = pou.Attribute("Name").Value;
            var declarationText = pou.Element("Declaration").Value;
            var implementationText = pou.Element("Implementation").Element("ST").Value;

            return new PouAst(name, declarationText, implementationText);
        }
    }
}
