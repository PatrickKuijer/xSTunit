using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TcXunit.Parser
{
    public static class TcPouParser
    {
        private static readonly Regex ExtendsPattern = new Regex(
            @"FUNCTION_BLOCK\s+\S+\s+EXTENDS\s+(?<baseType>[\w.]+)",
            RegexOptions.Compiled);

        private static readonly (Regex Pattern, string ConstructName)[] RejectedConstructs =
        {
            (new Regex(@"\b__NEW\b", RegexOptions.Compiled), "__NEW"),
            (new Regex(@"\bTc2_System\.", RegexOptions.Compiled), "Tc2_System"),
            (new Regex(@"\bTc2_Utilities\.", RegexOptions.Compiled), "Tc2_Utilities"),
            (new Regex(@"\bcall_after_init\b", RegexOptions.Compiled), "call_after_init"),
        };

        public static PouAst Parse(string xml)
        {
            var doc = XDocument.Parse(xml);
            var pou = doc.Root.Element("POU");
            var name = pou.Attribute("Name").Value;
            var declarationText = pou.Element("Declaration").Value;
            var implementationText = pou.Element("Implementation").Element("ST").Value;
            RejectIfUnsupported(name, implementationText);

            var extendsMatch = ExtendsPattern.Match(declarationText);
            var baseTypeName = extendsMatch.Success ? extendsMatch.Groups["baseType"].Value : null;

            var methods = pou.Elements("Method").Select(ParseMethod).ToList();

            return new PouAst(name, baseTypeName, declarationText, implementationText, methods);
        }

        private static MethodAst ParseMethod(XElement method)
        {
            var name = method.Attribute("Name").Value;
            var declarationText = method.Element("Declaration").Value;
            var implementationText = method.Element("Implementation").Element("ST").Value;
            RejectIfUnsupported(name, implementationText);

            return new MethodAst(name, declarationText, implementationText);
        }

        private static void RejectIfUnsupported(string scopeName, string implementationText)
        {
            foreach (var (pattern, constructName) in RejectedConstructs)
            {
                if (pattern.IsMatch(implementationText))
                {
                    throw new TcPouRejectedException(
                        $"'{scopeName}' uses '{constructName}', which is outside the v1 parse subset (TcXunit-w5x.6/.10).");
                }
            }
        }
    }
}
