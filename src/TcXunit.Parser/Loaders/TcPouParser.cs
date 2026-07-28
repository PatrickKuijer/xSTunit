using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TcXunit.Parser
{
    public static class TcPouParser
    {
        private static readonly Regex ExtendsPattern = new Regex(
            @"FUNCTION_BLOCK(?:\s+(?:ABSTRACT|FINAL))*\s+\S+\s+EXTENDS\s+(?<baseType>[\w.]+)",
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
            var properties = pou.Elements("Property").Select(ParseProperty).ToList();

            return new PouAst(name, baseTypeName, declarationText, implementationText, methods, properties);
        }

        private static MethodAst ParseMethod(XElement method)
        {
            var name = method.Attribute("Name").Value;
            var declarationText = method.Element("Declaration").Value;
            var implementationText = method.Element("Implementation").Element("ST").Value;
            RejectIfUnsupported(name, implementationText);

            return new MethodAst(name, declarationText, implementationText);
        }

        // <Property> nests its Get/Set accessor bodies one level deeper than
        // <Method> (<Property><Get><Implementation><ST>...), and either
        // accessor is optional (a get-only property has no <Set>, and vice
        // versa) - ParseAccessorImplementation returns null for a missing
        // accessor rather than throwing, so PropertyAst.HasGet/HasSet can
        // tell "not declared" apart from "declared with an empty body".
        private static PropertyAst ParseProperty(XElement property)
        {
            var name = property.Attribute("Name").Value;
            var declarationText = property.Element("Declaration")?.Value ?? string.Empty;

            var getImplementationText = ParseAccessorImplementation(property.Element("Get"));
            var setImplementationText = ParseAccessorImplementation(property.Element("Set"));

            // Unlike ParseMethod (one body per scope name), a property has
            // two independent accessor bodies - scope each rejection message
            // to "<name>.Get"/"<name>.Set" so a rejected-construct error
            // says which accessor it came from instead of just the
            // ambiguous property name.
            if (getImplementationText != null)
                RejectIfUnsupported($"{name}.Get", getImplementationText);
            if (setImplementationText != null)
                RejectIfUnsupported($"{name}.Set", setImplementationText);

            return new PropertyAst(name, declarationText, getImplementationText, setImplementationText);
        }

        private static string ParseAccessorImplementation(XElement accessor) =>
            accessor?.Element("Implementation")?.Element("ST")?.Value;

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
