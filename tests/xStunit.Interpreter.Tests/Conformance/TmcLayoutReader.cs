using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace xStunit.Interpreter.Tests.Conformance
{
    /// <summary>
    /// Reads the declared byte layout out of a TwinCAT <c>.tmc</c> module
    /// description.
    /// </summary>
    /// <remarks>
    /// The <c>.tmc</c> is written by the Beckhoff compiler at build time and
    /// states every data type's size and every member's offset outright, which
    /// makes it an oracle for xStunit's own layout math - one that needs no
    /// TwinCAT installation to consult, only the file.
    /// <para>
    /// Only the <c>DataTypes</c> section is read. The <c>Modules</c> section is
    /// consulted for the target platform alone; its symbol list describes where
    /// instances live in the PLC's memory image, which says nothing about how a
    /// type is laid out internally.
    /// </para>
    /// </remarks>
    public static class TmcLayoutReader
    {
        /// <param name="path">Path to a <c>.tmc</c> file.</param>
        /// <exception cref="System.IO.IOException">
        /// The file cannot be read - it is missing, locked, or the path is
        /// unreachable. Distinct from <see cref="System.Xml.XmlException"/>,
        /// which means the file was read but is not well-formed XML.
        /// </exception>
        /// <exception cref="System.Xml.XmlException">See <see cref="Parse"/>.</exception>
        public static ModuleLayout ReadFile(string path) => Parse(File.ReadAllText(path));

        /// <param name="xml">The contents of a <c>.tmc</c> file.</param>
        /// <returns>
        /// The module's declared types, in file order. A file declaring none
        /// yields an empty <see cref="ModuleLayout.Types"/> rather than null.
        /// </returns>
        /// <exception cref="System.Xml.XmlException">
        /// <paramref name="xml"/> is not well-formed XML. Well-formed XML that
        /// is not a <c>.tmc</c> does not throw - it reads as a module declaring
        /// no types.
        /// </exception>
        public static ModuleLayout Parse(string xml)
        {
            var root = XDocument.Parse(xml).Root;
            var module = root.Element("Modules")?.Elements("Module").FirstOrDefault();

            var types = root.Element("DataTypes")?.Elements("DataType").Select(ParseType).ToList()
                ?? new List<DeclaredTypeLayout>();

            return new ModuleLayout(
                module?.Element("Name")?.Value,
                module?.Attribute("TargetPlatform")?.Value,
                types);
        }

        private static DeclaredTypeLayout ParseType(XElement dataType)
        {
            var baseType = dataType.Element("BaseType");
            return new DeclaredTypeLayout(
                dataType.Element("Name")?.Value,
                ParseInt(dataType.Element("BitSize")),
                baseType?.Value,
                baseType?.Attribute("PointerTo") != null,
                dataType.Elements("ArrayInfo").Select(ParseDimension).ToList(),
                HasProperty(dataType, "PouType", "FunctionBlock"),
                ParseInt(PropertyValue(dataType, "pack_mode")),
                dataType.Elements("SubItem").Select(s => ParseMember(s, dataType.Element("Name")?.Value)).ToList(),
                dataType.Elements("Implements").Select(i => i.Value).ToList(),
                dataType.Element("ExtendsType")?.Value == "PVOID"
                    || Properties(dataType).Any(p => p.Element("Name")?.Value == "TcPlcInterfaceType"));
        }

        private static DeclaredMemberLayout ParseMember(XElement subItem, string ownerName)
        {
            var name = subItem.Element("Name");
            var type = subItem.Element("Type");
            var bitSize = subItem.Element("BitSize");

            return new DeclaredMemberLayout(
                name?.Value,
                type?.Value,
                type?.Attribute("PointerTo") != null,
                type?.Attribute("ReferenceTo") != null,
                name?.Attribute("Static")?.Value == "true",
                subItem.Elements("ArrayInfo").Select(ParseDimension).ToList(),
                ParseInt(bitSize),
                ParseInt(bitSize?.Attribute("X64")?.Value),
                ParseInt(subItem.Element("BitOffs")),
                IsMethodInstance(subItem, name?.Value, ownerName));
        }

        // Either signal alone is enough: the compiler flags some of these cells
        // with an implicit_inst_var property and leaves others bare, but names
        // them all __<OWNER>__<METHOD>__<VAR> in upper case.
        private static bool IsMethodInstance(XElement subItem, string memberName, string ownerName)
        {
            if (subItem.Element("Properties")?.Elements("Property")
                    .Any(p => p.Element("Name")?.Value == "implicit_inst_var") == true)
                return true;

            return memberName != null && ownerName != null
                && Regex.IsMatch(
                    memberName,
                    "^__" + Regex.Escape(ownerName.ToUpperInvariant()) + "__.+__.+$");
        }

        private static DeclaredArrayDimension ParseDimension(XElement arrayInfo) =>
            new DeclaredArrayDimension(
                ParseInt(arrayInfo.Element("LBound")) ?? 0,
                ParseInt(arrayInfo.Element("Elements")) ?? 0);

        // A DataType's own Properties block, not to be confused with the
        // Property elements nested inside each SubItem.
        private static bool HasProperty(XElement dataType, string name, string value) =>
            Properties(dataType).Any(p =>
                p.Element("Name")?.Value == name && p.Element("Value")?.Value == value);

        private static string PropertyValue(XElement dataType, string name) =>
            Properties(dataType)
                .FirstOrDefault(p => p.Element("Name")?.Value == name)
                ?.Element("Value")?.Value;

        private static IEnumerable<XElement> Properties(XElement dataType) =>
            dataType.Element("Properties")?.Elements("Property") ?? Enumerable.Empty<XElement>();

        private static int? ParseInt(XElement element) => ParseInt(element?.Value);

        private static int? ParseInt(string text) =>
            int.TryParse(text, out var value) ? value : (int?)null;
    }
}
