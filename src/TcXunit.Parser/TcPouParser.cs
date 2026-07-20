using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TcXunit.Parser
{
    public static class TcPouParser
    {
        private static readonly Regex ExtendsPattern = new Regex(
            @"FUNCTION_BLOCK\s+\S+\s+EXTENDS\s+(?<baseType>[\w.]+)",
            RegexOptions.Compiled);

        public static PouAst Parse(string xml)
        {
            var doc = XDocument.Parse(xml);
            var pou = doc.Root.Element("POU");
            var name = pou.Attribute("Name").Value;
            var declarationText = pou.Element("Declaration").Value;
            var implementationText = pou.Element("Implementation").Element("ST").Value;

            var extendsMatch = ExtendsPattern.Match(declarationText);
            var baseTypeName = extendsMatch.Success ? extendsMatch.Groups["baseType"].Value : null;

            return new PouAst(name, baseTypeName, declarationText, implementationText);
        }
    }
}
