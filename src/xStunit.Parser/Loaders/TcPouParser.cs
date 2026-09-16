using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace xStunit.Parser
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

        /// <exception cref="TcPouRejectedException">
        /// The POU's own body, or one of its methods or property accessors,
        /// uses a construct outside the supported subset, or is written in a
        /// graphical language and so has no ST body at all.
        /// </exception>
        /// <exception cref="XmlException">
        /// The XML is malformed, or is missing an element or attribute the
        /// .TcPOU shape requires - a broken file rather than an unsupported
        /// one.
        /// </exception>
        public static PouAst Parse(string xml)
        {
            // SetLineInfo is what lets a failure be reported against a line in
            // the user's .TcPOU rather than a line in an extracted string.
            var doc = XDocument.Parse(xml, LoadOptions.SetLineInfo);
            var pou = RequiredElement(doc.Root, "POU", "the file");
            var name = RequiredAttribute(pou, "Name", "<POU>");
            var declarationText = RequiredElement(pou, "Declaration", name).Value;
            var st = RequiredStBody(pou, name);
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
            var name = RequiredAttribute(method, "Name", "<Method>");
            var declarationText = RequiredElement(method, "Declaration", name).Value;
            var st = RequiredStBody(method, name);
            var implementationText = st.Value;
            RejectIfUnsupported(name, implementationText);

            return new MethodAst(name, declarationText, implementationText, BodyStartLine(st));
        }

        // IXmlLineInfo reports the line of the <ST> start tag, and TwinCAT
        // writes the CDATA prologue on that same line (`<ST><![CDATA[`). A
        // newline directly after `<![CDATA[` stays inside the body string and
        // becomes the body's own line 0 rather than shifting the body down, so
        // the <ST> line is the file line of body line 0 in both shapes - which
        // is what makes MethodAst.BodyStartLine's arithmetic hold.
        private static int BodyStartLine(XElement st)
        {
            var lineInfo = (IXmlLineInfo)st;
            return lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1;
        }

        private static PropertyAst ParseProperty(XElement property)
        {
            var name = RequiredAttribute(property, "Name", "<Property>");
            var declarationText = property.Element("Declaration")?.Value ?? string.Empty;

            var getImplementationText = ParseAccessorImplementation(property.Element("Get"), $"{name}.Get");
            var setImplementationText = ParseAccessorImplementation(property.Element("Set"), $"{name}.Set");

            // Scope each rejection to "<name>.Get"/"<name>.Set": a property has
            // two independent bodies, so the ambiguous property name alone
            // would not say which accessor was rejected.
            if (getImplementationText != null)
                RejectIfUnsupported($"{name}.Get", getImplementationText);
            if (setImplementationText != null)
                RejectIfUnsupported($"{name}.Set", setImplementationText);

            return new PropertyAst(name, declarationText, getImplementationText, setImplementationText);
        }

        // Null (not an exception) for an accessor the POU never declared, so
        // PropertyAst.HasGet/HasSet can tell that apart from an empty body, and
        // likewise for one carrying no <Implementation> at all - TwinCAT writes
        // that element inconsistently across versions, so its absence is not
        // evidence of a broken file. An accessor whose <Implementation> is
        // present but holds no <ST> is a rejection: chaining past it to null
        // would report a graphical accessor as one the POU never wrote.
        private static string ParseAccessorImplementation(XElement accessor, string scopeName) =>
            accessor?.Element("Implementation") == null ? null : RequiredStBody(accessor, scopeName).Value;

        // A body drawn in LD/FBD/SFC/CFC/IL is written as a <NWL>/<CFC>/...
        // child instead of <ST>: well-formed and TwinCAT-valid, but carrying no
        // ST text this tool could interpret even in principle. Naming the
        // element TwinCAT did write is what makes every graphical language read
        // sensibly in the skip line.
        private static XElement RequiredStBody(XElement owner, string scopeName)
        {
            var implementation = RequiredElement(owner, "Implementation", scopeName);
            var st = implementation.Element("ST");
            if (st != null)
                return st;

            var language = implementation.Elements().FirstOrDefault()?.Name.LocalName ?? "unknown";
            throw new TcPouRejectedException(
                $"'{scopeName}' has no ST body (implementation language '{language}'), " +
                "which is outside the v1 parse subset (not yet implemented).");
        }

        // A structural gap surfaces as XmlException: the message is all a
        // skipped file gets, so it has to name what was missing and where.
        private static XElement RequiredElement(XElement parent, string elementName, string scopeName) =>
            parent?.Element(elementName)
                ?? throw new XmlException($"{scopeName} has no <{elementName}> element.");

        private static string RequiredAttribute(XElement element, string attributeName, string scopeName) =>
            element.Attribute(attributeName)?.Value
                ?? throw new XmlException($"{scopeName} has no {attributeName} attribute.");

        private static void RejectIfUnsupported(string scopeName, string implementationText)
        {
            foreach (var (pattern, constructName) in RejectedConstructs)
            {
                if (pattern.IsMatch(implementationText))
                {
                    throw new TcPouRejectedException(
                        $"'{scopeName}' uses '{constructName}', which is outside the v1 parse subset (not yet implemented).");
                }
            }
        }
    }
}
