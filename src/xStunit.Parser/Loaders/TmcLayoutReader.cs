using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace xStunit.Parser
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
        public static ModuleLayout ReadFile(string path) => Parse(File.ReadAllText(path));

        /// <param name="xml">The contents of a <c>.tmc</c> file.</param>
        /// <returns>
        /// Every data type the file declares, in file order. A file declaring
        /// none yields an empty <see cref="ModuleLayout.Types"/> rather than
        /// null.
        /// </returns>
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
                dataType.Element("EnumInfo") != null,
                HasProperty(dataType, "PouType", "FunctionBlock"),
                dataType.Elements("SubItem").Select(ParseMember).ToList());
        }

        private static DeclaredMemberLayout ParseMember(XElement subItem)
        {
            var name = subItem.Element("Name");
            var type = subItem.Element("Type");
            var bitSize = subItem.Element("BitSize");

            return new DeclaredMemberLayout(
                name?.Value,
                type?.Value,
                type?.Attribute("PointerTo") != null,
                name?.Attribute("Static")?.Value == "true",
                subItem.Elements("ArrayInfo").Select(ParseDimension).ToList(),
                ParseInt(bitSize),
                ParseInt(bitSize?.Attribute("X64")?.Value),
                ParseInt(subItem.Element("BitOffs")));
        }

        private static DeclaredArrayDimension ParseDimension(XElement arrayInfo) =>
            new DeclaredArrayDimension(
                ParseInt(arrayInfo.Element("LBound")) ?? 0,
                ParseInt(arrayInfo.Element("Elements")) ?? 0);

        // A DataType's own Properties block, not to be confused with the
        // Property elements nested inside each SubItem.
        private static bool HasProperty(XElement dataType, string name, string value) =>
            dataType.Element("Properties")?.Elements("Property")
                .Any(p => p.Element("Name")?.Value == name && p.Element("Value")?.Value == value) ?? false;

        private static int? ParseInt(XElement element) => ParseInt(element?.Value);

        private static int? ParseInt(string text) =>
            int.TryParse(text, out var value) ? value : (int?)null;
    }
}
