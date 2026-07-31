using System.Xml.Linq;

namespace xStunit.Parser
{
    /// <summary>Parses a .TcGVL file's XML into a <see cref="GvlAst"/>.</summary>
    /// <remarks>
    /// A GVL's root element is &lt;GVL Name="X"&gt; alongside &lt;POU&gt;,
    /// not a variant of it - <see cref="TcPouParser.Parse"/> hardcoding
    /// &lt;POU&gt; is exactly why GVLs needed their own parser.
    /// </remarks>
    public static class TcGvlParser
    {
        /// <summary>Parses the given .TcGVL XML content into a <see cref="GvlAst"/>.</summary>
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
