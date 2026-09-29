using System.Linq;
using System.Xml;
using Xunit;

namespace xStunit.Interpreter.Tests.Conformance
{
    // Every case below is XML shaped exactly as a real .tmc writes it. The
    // reader's job is to hand a consumer what the compiler declared without
    // interpreting it: bits stay bits, a pointer stays a flag on the member
    // rather than being folded into the type name.
    public class TmcLayoutReaderTests
    {
        private const string StructXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcModuleClass><DataTypes><DataType><Name GUID=""{6F5942ED-0000-0000-0000-000000000001}"" TcBaseType=""true"">ST_LibVersion</Name><BitSize>288</BitSize>
<SubItem><Name>iMajor</Name><Type>UINT</Type><BitSize>16</BitSize><BitOffs>0</BitOffs></SubItem>
<SubItem><Name>sVersion</Name><Type>STRING(23)</Type><BitSize>192</BitSize><BitOffs>96</BitOffs></SubItem>
</DataType></DataTypes>
<Modules><Module TargetPlatform=""TwinCAT RT (x64)""><Name>PLC1</Name></Module></Modules></TcModuleClass>";

        [Fact]
        public void Parse_Struct_ReadsMemberOffsetsAndSizesInBits()
        {
            var module = TmcLayoutReader.Parse(StructXml);

            var type = Assert.Single(module.Types);
            Assert.Equal("ST_LibVersion", type.Name);
            Assert.Equal(288, type.BitSize);
            Assert.Equal(new int?[] { 0, 96 }, type.Members.Select(m => m.BitOffset));
            Assert.Equal(new int?[] { 16, 192 }, type.Members.Select(m => m.BitSize));
            Assert.Equal("STRING(23)", type.Members[1].TypeName);
        }

        [Fact]
        public void Parse_Module_ReadsNameAndTargetPlatform()
        {
            var module = TmcLayoutReader.Parse(StructXml);

            Assert.Equal("PLC1", module.ModuleName);
            Assert.Equal("TwinCAT RT (x64)", module.TargetPlatform);
        }

        // The width a member has on a 64-bit target is an attribute on the
        // 32-bit value, not a second element - read the element alone and every
        // pointer in a 64-bit module measures 32 bits.
        [Fact]
        public void Parse_MemberWithTargetDependentWidth_ReadsBothWidths()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcModuleClass><DataTypes><DataType><Name>PlcAppSystemInfo</Name><BitSize>512</BitSize>
<SubItem><Name>TComSrvPtr</Name><Type GUID=""{00000030-0000-0000-E000-000000000064}"">ITComObjectServer</Type><BitSize X64=""64"">32</BitSize><BitOffs>256</BitOffs></SubItem>
</DataType></DataTypes></TcModuleClass>";

            var member = Assert.Single(TmcLayoutReader.Parse(xml).Types.Single().Members);

            Assert.Equal(32, member.BitSize);
            Assert.Equal(64, member.BitSizeX64);
        }

        [Fact]
        public void Parse_PointerAndArrayMembers_KeepTheElementTypeAndFlagTheShape()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcModuleClass><DataTypes><DataType><Name>_Implicit_Task_Info</Name><BitSize>128</BitSize>
<SubItem><Name>pszName</Name><Type PointerTo=""1"">STRING(80)</Type><BitSize>64</BitSize><BitOffs>0</BitOffs></SubItem>
<SubItem><Name>aSlots</Name><Type>DINT</Type><ArrayInfo><LBound>1</LBound><Elements>4</Elements></ArrayInfo><BitSize>128</BitSize><BitOffs>64</BitOffs></SubItem>
</DataType></DataTypes></TcModuleClass>";

            var members = TmcLayoutReader.Parse(xml).Types.Single().Members;

            Assert.True(members[0].IsPointer);
            Assert.Equal("STRING(80)", members[0].TypeName);
            Assert.Empty(members[0].ArrayDimensions);

            Assert.False(members[1].IsPointer);
            var dimension = Assert.Single(members[1].ArrayDimensions);
            Assert.Equal(1, dimension.LowerBound);
            Assert.Equal(4, dimension.ElementCount);
        }

        // A REFERENCE TO member is marked with its own attribute rather than
        // with PointerTo, and the element still names the referent. Read without
        // it, the member is sized as that referent - two bytes for an INT - and
        // silently agrees with nothing the compiler declared.
        [Fact]
        public void Parse_ReferenceMember_IsFlaggedApartFromAPointer()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcModuleClass><DataTypes><DataType><Name>ST_ReferenceWidth</Name><BitSize>96</BitSize>
<SubItem><Name>target</Name><Type ReferenceTo=""true"">INT</Type><BitSize>32</BitSize><BitOffs>32</BitOffs></SubItem>
<SubItem><Name>owned</Name><Type PointerTo=""1"">INT</Type><BitSize>32</BitSize><BitOffs>64</BitOffs></SubItem>
</DataType></DataTypes></TcModuleClass>";

            var members = TmcLayoutReader.Parse(xml).Types.Single().Members;

            Assert.True(members[0].IsReference);
            Assert.False(members[0].IsPointer);
            Assert.Equal("INT", members[0].TypeName);

            Assert.True(members[1].IsPointer);
            Assert.False(members[1].IsReference);
        }

        // The pragma survives compilation into the same DataType-level
        // Properties block that carries PouType, so a packed type can be
        // recognised as packed from the file alone.
        [Fact]
        public void Parse_PackedType_ReadsItsPackModeFromTheTypesOwnProperties()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcModuleClass><DataTypes>
<DataType><Name>ST_PackedToTwo</Name><BitSize>64</BitSize>
<SubItem><Name>wide</Name><Type>DINT</Type><BitSize>32</BitSize><BitOffs>16</BitOffs></SubItem>
<Properties><Property><Name>pack_mode</Name><Value>2</Value></Property></Properties></DataType>
<DataType><Name>ST_Natural</Name><BitSize>64</BitSize>
<SubItem><Name>wide</Name><Type>DINT</Type><BitSize>32</BitSize><BitOffs>32</BitOffs></SubItem>
</DataType>
</DataTypes></TcModuleClass>";

            var types = TmcLayoutReader.Parse(xml).Types;

            Assert.Equal(2, types[0].PackMode);
            Assert.Null(types[1].PackMode);
        }

        // An explicit pack_mode 0 packs without gaps, so reading the property
        // back as "absent" would score a correctly packed type as a mismatch.
        [Fact]
        public void Parse_ExplicitPackModeZero_IsReadAsAPragmaRatherThanAsAbsent()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcModuleClass><DataTypes>
<DataType><Name>ST_PackedToZero</Name><BitSize>48</BitSize>
<SubItem><Name>wide</Name><Type>DINT</Type><BitSize>32</BitSize><BitOffs>8</BitOffs></SubItem>
<Properties><Property><Name>pack_mode</Name><Value>0</Value></Property></Properties></DataType>
</DataTypes></TcModuleClass>";

            Assert.Equal(0, Assert.Single(TmcLayoutReader.Parse(xml).Types).PackMode);
        }

        // A handle type aliases a POINTER TO its base type, so dropping the
        // attribute would make it as wide as whatever it points at.
        [Fact]
        public void Parse_PointerAlias_FlagsTheBaseTypeAsPointedTo()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcModuleClass><DataTypes><DataType><Name>RTS_IEC_HANDLE</Name><BitSize>32</BitSize><BaseType PointerTo=""1"">BYTE</BaseType></DataType></DataTypes></TcModuleClass>";

            var type = TmcLayoutReader.Parse(xml).Types.Single();

            Assert.Equal("BYTE", type.BaseTypeName);
            Assert.True(type.BaseTypeIsPointer);
            Assert.Empty(type.Members);
        }

        [Fact]
        public void Parse_Enum_ReadsItsBaseType()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcModuleClass><DataTypes><DataType><Name>EPlcPersistentStatus</Name><BitSize>8</BitSize><BaseType>USINT</BaseType>
<EnumInfo><Text>PS_None</Text><Enum>0</Enum></EnumInfo></DataType></DataTypes></TcModuleClass>";

            var type = TmcLayoutReader.Parse(xml).Types.Single();

            Assert.Equal("USINT", type.BaseTypeName);
            Assert.False(type.BaseTypeIsPointer);
            Assert.Empty(type.ArrayDimensions);
        }

        // An array type alias carries its ArrayInfo on the DataType itself
        // rather than on a member. Reading only member-level ArrayInfo would
        // size such a type as a single element.
        [Fact]
        public void Parse_ArrayAlias_ReadsTheTypesOwnDimensions()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcModuleClass><DataTypes><DataType><Name>T_Slots</Name><BitSize>128</BitSize><BaseType>DINT</BaseType>
<ArrayInfo><LBound>1</LBound><Elements>4</Elements></ArrayInfo></DataType></DataTypes></TcModuleClass>";

            var type = TmcLayoutReader.Parse(xml).Types.Single();

            Assert.Equal("DINT", type.BaseTypeName);
            var dimension = Assert.Single(type.ArrayDimensions);
            Assert.Equal(1, dimension.LowerBound);
            Assert.Equal(4, dimension.ElementCount);
        }

        // PouType lives in the DataType's own Properties block, which is
        // spelled identically to the Properties block nested inside every
        // SubItem - matching the wrong one classifies plain structs as function
        // blocks.
        [Fact]
        public void Parse_FunctionBlock_IsFlaggedFromTheTypesOwnProperties()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcModuleClass><DataTypes>
<DataType><Name>FB_AddLrealInt</Name><BitSize>256</BitSize>
<SubItem><Name>lrealValue</Name><Type>LREAL</Type><BitSize>64</BitSize><BitOffs>64</BitOffs><Properties><Property><Name>ItemType</Name><Value>Input</Value></Property></Properties></SubItem>
<Properties><Property><Name>PouType</Name><Value>FunctionBlock</Value></Property></Properties></DataType>
<DataType><Name>ST_Plain</Name><BitSize>32</BitSize>
<SubItem><Name>value</Name><Type>DINT</Type><BitSize>32</BitSize><BitOffs>0</BitOffs><Properties><Property><Name>ItemType</Name><Value>Input</Value></Property></Properties></SubItem>
</DataType>
</DataTypes></TcModuleClass>";

            var types = TmcLayoutReader.Parse(xml).Types;

            Assert.True(types[0].IsFunctionBlock);
            Assert.False(types[1].IsFunctionBlock);
        }

        // A .tmc for a project declaring nothing of its own is still a valid
        // file; a consumer should get an empty list rather than a null one.
        [Fact]
        public void Parse_NoDataTypesSection_YieldsNoTypes()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcModuleClass><Modules><Module TargetPlatform=""TwinCAT RT (x86)""><Name>Empty</Name></Module></Modules></TcModuleClass>";

            Assert.Empty(TmcLayoutReader.Parse(xml).Types);
        }

        [Fact]
        public void Parse_MalformedXml_ThrowsXmlException()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcModuleClass><DataTypes><DataType><Name>ST_Broken</Name>
</TcModuleClass>";

            Assert.Throws<XmlException>(() => TmcLayoutReader.Parse(xml));
        }
    }
}
