using System;
using System.IO;
using System.Linq;
using TcXunit.Interpreter;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-bpk: the STRUCT-vs-ENUM/alias/union filter must match the
    // STRUCT keyword in the TYPE header, not a raw Contains("STRUCT") over
    // the whole declaration text - which would false-positive on an ENUM or
    // alias DUT whose text merely contains that substring in a comment or an
    // identifier like "STRUCTURED".
    public class DutStructLoaderTests
    {
        [Fact]
        public void IsStructDeclaration_ActualStructDeclaration_ReturnsTrue()
        {
            const string declaration = @"TYPE ST_Point :
STRUCT
	x : REAL;
	y : REAL;
END_STRUCT
END_TYPE";

            Assert.True(DutStructLoader.IsStructDeclaration(declaration));
        }

        [Fact]
        public void IsStructDeclaration_StructOnSameLineAsHeader_ReturnsTrue()
        {
            const string declaration = @"TYPE ST_Point : STRUCT
	x : REAL;
END_STRUCT
END_TYPE";

            Assert.True(DutStructLoader.IsStructDeclaration(declaration));
        }

        [Fact]
        public void IsStructDeclaration_EnumWithStructInCommentText_ReturnsFalse()
        {
            const string declaration = @"(* replaces the old STRUCT-based version *)
TYPE E_Color :
(
	Red,
	Green,
	Blue
);
END_TYPE";

            Assert.False(DutStructLoader.IsStructDeclaration(declaration));
        }

        [Fact]
        public void IsStructDeclaration_AliasWithStructSubstringIdentifier_ReturnsFalse()
        {
            const string declaration = @"TYPE ST_STRUCTURED_ALIAS : INT;
END_TYPE";

            Assert.False(DutStructLoader.IsStructDeclaration(declaration));
        }

        private static string DutXml(string typeName, string declaration) => $@"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <DUT Name=""{typeName}"" Id=""{{a1b2c3d4-0006-4a1a-8b1b-0000000000ff}}"">
    <Declaration><![CDATA[{declaration}]]></Declaration>
  </DUT>
</TcPlcObject>";

        [Fact]
        public void Load_EnumDutWithStructSubstringInComment_IsSkippedNotRegisteredAsStruct()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-enumcomment-" + Guid.NewGuid()));
            try
            {
                const string declaration = @"(* replaces the old STRUCT-based version *)
TYPE E_Color :
(
	Red,
	Green,
	Blue
);
END_TYPE";
                File.WriteAllText(Path.Combine(tempDir.FullName, "E_Color.TcDUT"), DutXml("E_Color", declaration));

                var structTypes = DutStructLoader.Load(new[] { tempDir.FullName }, out var skipped);

                Assert.Empty(structTypes);
                Assert.Empty(skipped);
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }

        [Fact]
        public void Load_AliasDutWithStructSubstringIdentifier_IsSkippedNotRegisteredAsStruct()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-aliasstruct-" + Guid.NewGuid()));
            try
            {
                const string declaration = "TYPE ST_STRUCTURED_ALIAS : INT;\nEND_TYPE";
                File.WriteAllText(Path.Combine(tempDir.FullName, "ST_STRUCTURED_ALIAS.TcDUT"), DutXml("ST_STRUCTURED_ALIAS", declaration));

                var structTypes = DutStructLoader.Load(new[] { tempDir.FullName }, out var skipped);

                Assert.Empty(structTypes);
                Assert.Empty(skipped);
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }

        [Fact]
        public void Load_ActualStructDut_IsRegistered()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-realstruct-" + Guid.NewGuid()));
            try
            {
                const string declaration = "TYPE ST_Point :\nSTRUCT\n\tx : REAL;\nEND_STRUCT\nEND_TYPE";
                File.WriteAllText(Path.Combine(tempDir.FullName, "ST_Point.TcDUT"), DutXml("ST_Point", declaration));

                var structTypes = DutStructLoader.Load(new[] { tempDir.FullName }, out var skipped);

                Assert.Empty(skipped);
                var structAst = Assert.Single(structTypes);
                Assert.Equal("ST_Point", structAst.Name);
                Assert.Equal(new[] { "x" }, structAst.Fields.Select(f => f.Name));
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }
    }
}
