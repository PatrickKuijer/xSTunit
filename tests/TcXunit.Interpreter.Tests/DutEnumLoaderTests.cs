using System;
using System.IO;
using TcXunit.Interpreter;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-fyu: ENUM .TcDUT definitions (e.g. "TYPE E_Color : (Red,
    // Green, Blue); END_TYPE") aren't resolved by the interpreter today -
    // SIZEOF() (TcXunit-l1x) throws "doesn't know the byte size of type X"
    // for a field/variable declared with an enum type. DutEnumLoader parses
    // these into the same name -> underlying-type-text shape
    // DutAliasLoader uses, so they can feed the same TypeRegistry alias map
    // and every existing ResolveAlias call site (including SIZEOF's).
    public class DutEnumLoaderTests
    {
        [Fact]
        public void TryParseEnum_NoExplicitBaseType_DefaultsToInt()
        {
            const string declaration = "TYPE E_Color :\n(\n\tRed,\n\tGreen,\n\tBlue\n);\nEND_TYPE";

            var parsed = DutEnumLoader.TryParseEnum(declaration, out var name, out var underlying);

            Assert.True(parsed);
            Assert.Equal("E_Color", name);
            Assert.Equal("INT", underlying);
        }

        [Fact]
        public void TryParseEnum_ExplicitBaseType_ExtractsBaseType()
        {
            const string declaration =
                "TYPE eModuleParameterDataTypes : (\n\tIDT_BOOL,\n\tIDT_BYTE,\n\tIDT_INT\n) DINT;\nEND_TYPE";

            var parsed = DutEnumLoader.TryParseEnum(declaration, out var name, out var underlying);

            Assert.True(parsed);
            Assert.Equal("eModuleParameterDataTypes", name);
            Assert.Equal("DINT", underlying);
        }

        [Fact]
        public void TryParseEnum_MembersWithInitializers_DefaultsToInt()
        {
            const string declaration = "TYPE E_Status :\n(\n\tOk := 0,\n\tError := 1\n);\nEND_TYPE";

            var parsed = DutEnumLoader.TryParseEnum(declaration, out var name, out var underlying);

            Assert.True(parsed);
            Assert.Equal("E_Status", name);
            Assert.Equal("INT", underlying);
        }

        [Fact]
        public void TryParseEnum_AliasDeclaration_ReturnsFalse()
        {
            const string declaration = "TYPE T_MaxString : STRING(255);\nEND_TYPE";

            Assert.False(DutEnumLoader.TryParseEnum(declaration, out _, out _));
        }

        [Fact]
        public void TryParseEnum_StructDeclaration_ReturnsFalse()
        {
            const string declaration = "TYPE ST_Point :\nSTRUCT\n\tx : REAL;\nEND_STRUCT\nEND_TYPE";

            Assert.False(DutEnumLoader.TryParseEnum(declaration, out _, out _));
        }

        private static string DutXml(string typeName, string declaration) => $@"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <DUT Name=""{typeName}"" Id=""{{a1b2c3d4-0008-4a1a-8b1b-0000000000ff}}"">
    <Declaration><![CDATA[{declaration}]]></Declaration>
  </DUT>
</TcPlcObject>";

        [Fact]
        public void Load_EnumDut_IsRegisteredWithUnderlyingType()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-enum-" + Guid.NewGuid()));
            try
            {
                const string declaration = "TYPE E_Color :\n(\n\tRed,\n\tGreen,\n\tBlue\n);\nEND_TYPE";
                File.WriteAllText(Path.Combine(tempDir.FullName, "E_Color.TcDUT"), DutXml("E_Color", declaration));

                var enums = DutEnumLoader.Load(new[] { tempDir.FullName }, out var skipped);

                Assert.Empty(skipped);
                Assert.Equal("INT", enums["E_Color"]);
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }

        [Fact]
        public void Load_StructDut_IsNotRegisteredAsEnum()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-enumstructskip-" + Guid.NewGuid()));
            try
            {
                const string declaration = "TYPE ST_Point :\nSTRUCT\n\tx : REAL;\nEND_STRUCT\nEND_TYPE";
                File.WriteAllText(Path.Combine(tempDir.FullName, "ST_Point.TcDUT"), DutXml("ST_Point", declaration));

                var enums = DutEnumLoader.Load(new[] { tempDir.FullName }, out var skipped);

                Assert.Empty(skipped);
                Assert.Empty(enums);
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }
    }
}
