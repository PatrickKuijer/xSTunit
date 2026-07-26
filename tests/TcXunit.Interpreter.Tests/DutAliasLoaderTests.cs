using System;
using System.IO;
using System.Linq;
using TcXunit.Interpreter;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-6hg: ALIAS .TcDUT definitions (e.g. "TYPE T_MaxString :
    // STRING(255); END_TYPE") aren't resolved by the interpreter today - a
    // POU declaring a var of an alias type fails type resolution because
    // the alias name is never mapped to its underlying type. DutAliasLoader
    // parses these the same resilient way DutStructLoader parses STRUCT
    // DUTs.
    public class DutAliasLoaderTests
    {
        [Fact]
        public void TryParseAlias_AliasToSizedString_ExtractsUnderlyingType()
        {
            const string declaration = "TYPE T_MaxString : STRING(255);\nEND_TYPE";

            var parsed = DutAliasLoader.TryParseAlias(declaration, out var name, out var underlying);

            Assert.True(parsed);
            Assert.Equal("T_MaxString", name);
            Assert.Equal("STRING(255)", underlying);
        }

        [Fact]
        public void TryParseAlias_AliasToScalar_ExtractsUnderlyingType()
        {
            const string declaration = "TYPE T_Counter : INT;\nEND_TYPE";

            var parsed = DutAliasLoader.TryParseAlias(declaration, out var name, out var underlying);

            Assert.True(parsed);
            Assert.Equal("T_Counter", name);
            Assert.Equal("INT", underlying);
        }

        [Fact]
        public void TryParseAlias_UnderlyingTypeOnFollowingLine_ExtractsUnderlyingType()
        {
            const string declaration = "TYPE T_MaxString :\nSTRING(255);\nEND_TYPE";

            var parsed = DutAliasLoader.TryParseAlias(declaration, out var name, out var underlying);

            Assert.True(parsed);
            Assert.Equal("T_MaxString", name);
            Assert.Equal("STRING(255)", underlying);
        }

        [Fact]
        public void TryParseAlias_StructDeclaration_ReturnsFalse()
        {
            const string declaration = "TYPE ST_Point :\nSTRUCT\n\tx : REAL;\nEND_STRUCT\nEND_TYPE";

            Assert.False(DutAliasLoader.TryParseAlias(declaration, out _, out _));
        }

        [Fact]
        public void TryParseAlias_EnumDeclaration_ReturnsFalse()
        {
            const string declaration = "TYPE E_Color :\n(\n\tRed,\n\tGreen,\n\tBlue\n);\nEND_TYPE";

            Assert.False(DutAliasLoader.TryParseAlias(declaration, out _, out _));
        }

        private static string DutXml(string typeName, string declaration) => $@"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <DUT Name=""{typeName}"" Id=""{{a1b2c3d4-0007-4a1a-8b1b-0000000000ff}}"">
    <Declaration><![CDATA[{declaration}]]></Declaration>
  </DUT>
</TcPlcObject>";

        [Fact]
        public void Load_AliasDut_IsRegisteredWithUnderlyingType()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-alias-" + Guid.NewGuid()));
            try
            {
                const string declaration = "TYPE T_MaxString : STRING(255);\nEND_TYPE";
                File.WriteAllText(Path.Combine(tempDir.FullName, "T_MaxString.TcDUT"), DutXml("T_MaxString", declaration));

                var aliases = DutAliasLoader.Load(new[] { tempDir.FullName }, out var skipped);

                Assert.Empty(skipped);
                Assert.Equal("STRING(255)", aliases["T_MaxString"]);
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }

        [Fact]
        public void Load_StructDut_IsNotRegisteredAsAlias()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-aliasstructskip-" + Guid.NewGuid()));
            try
            {
                const string declaration = "TYPE ST_Point :\nSTRUCT\n\tx : REAL;\nEND_STRUCT\nEND_TYPE";
                File.WriteAllText(Path.Combine(tempDir.FullName, "ST_Point.TcDUT"), DutXml("ST_Point", declaration));

                var aliases = DutAliasLoader.Load(new[] { tempDir.FullName }, out var skipped);

                Assert.Empty(skipped);
                Assert.Empty(aliases);
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }
    }
}
