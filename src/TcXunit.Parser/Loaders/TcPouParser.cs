using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
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
            // SetLineInfo is what makes failure logs able to point at a line in
            // the user's .TcPOU instead of a line in an extracted string
            // (TcXunit-p3t.3).
            var doc = XDocument.Parse(xml, LoadOptions.SetLineInfo);
            var pou = doc.Root.Element("POU");
            var name = pou.Attribute("Name").Value;
            var declarationText = pou.Element("Declaration").Value;
            var st = pou.Element("Implementation").Element("ST");
            var implementationText = st.Value;
            RejectIfUnsupported(name, implementationText);

            var extendsMatch = ExtendsPattern.Match(declarationText);
            var baseTypeName = extendsMatch.Success ? extendsMatch.Groups["baseType"].Value : null;

            var methods = pou.Elements("Method").Select(ParseMethod).ToList();
            var properties = pou.Elements("Property").Select(ParseProperty).ToList();

            return new PouAst(name, baseTypeName, declarationText, implementationText, methods, properties, BodyStartLine(st));
        }

        private static MethodAst ParseMethod(XElement method)
        {
            var name = method.Attribute("Name").Value;
            var declarationText = method.Element("Declaration").Value;
            var st = method.Element("Implementation").Element("ST");
            var implementationText = st.Value;
            RejectIfUnsupported(name, implementationText);

            return new MethodAst(name, declarationText, implementationText, BodyStartLine(st));
        }

        // IXmlLineInfo reports the line of the <ST> start tag, and TwinCAT
        // writes the CDATA prologue on that same line (`<ST><![CDATA[`). A
        // newline directly after `<![CDATA[` stays inside the body string, so
        // it becomes the body's own (empty) line 0 rather than shifting the
        // body down - which makes the <ST> line the file line of body line 0
        // in both shapes, i.e. BodyStartLine + zeroBasedLineWithinBody is the
        // real file line (TcXunit-p3t.3).
        private static int BodyStartLine(XElement st)
        {
            var lineInfo = (IXmlLineInfo)st;
            return lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1;
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
