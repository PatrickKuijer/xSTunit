using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace xStunit.Parser
{
    /// <summary>
    /// Reads a .TcIO interface file. The XML is the same shape a .TcPOU has -
    /// a Declaration plus Method/Property children - under an
    /// &lt;Itf&gt; root instead of &lt;POU&gt;.
    /// </summary>
    public static class TcItfParser
    {
        // Methods and properties are parsed but nothing yet reads them: an FB's
        // IMPLEMENTS clause is still parsed past without effect, so a
        // non-conformant FB loads clean. Checking it needs the whole signature
        // shape this already captures, which is why it is captured before there
        // is a consumer.
        public static InterfaceAst Parse(string xml)
        {
            var itf = XDocument.Parse(xml).Root?.Element("Itf")
                ?? throw new XmlException("the file has no <Itf> element.");
            var name = itf.Attribute("Name")?.Value
                ?? throw new XmlException("<Itf> has no Name attribute.");

            return new InterfaceAst(
                name,
                (itf.Element("Declaration")
                    ?? throw new XmlException($"{name} has no <Declaration> element.")).Value,
                itf.Elements("Method").Select(ParseMethod).ToList(),
                itf.Elements("Property").Select(ParseProperty).ToList());
        }

        // Empty implementation text rather than whatever the file carries: an
        // interface method is a signature, and TwinCAT writes the empty <ST>
        // element inconsistently across versions.
        private static MethodAst ParseMethod(XElement method) =>
            new MethodAst(
                method.Attribute("Name")?.Value
                    ?? throw new XmlException("<Method> has no Name attribute."),
                (method.Element("Declaration")
                    ?? throw new XmlException("<Method> has no <Declaration> element.")).Value,
                string.Empty);

        // An accessor the interface declares maps to empty (not null) body
        // text, so PropertyAst.HasGet/HasSet report which accessors the
        // contract demands - the whole reason a .TcIO carries <Get>/<Set> at
        // all, since neither has anything to execute.
        private static PropertyAst ParseProperty(XElement property) =>
            new PropertyAst(
                property.Attribute("Name")?.Value
                    ?? throw new XmlException("<Property> has no Name attribute."),
                property.Element("Declaration")?.Value ?? string.Empty,
                property.Element("Get") == null ? null : string.Empty,
                property.Element("Set") == null ? null : string.Empty);
    }
}
