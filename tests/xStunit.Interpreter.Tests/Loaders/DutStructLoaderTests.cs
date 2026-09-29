using System;
using System.IO;
using System.Linq;
using xStunit.Interpreter;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // The STRUCT-vs-ENUM/alias/union filter matches the STRUCT keyword in the
    // TYPE header, not a Contains("STRUCT") over the whole declaration text.
    // The difference only shows up on declarations carrying that substring
    // somewhere harmless - a comment, or an identifier like "STRUCTURED" -
    // which is what most of these fixtures are built to be.
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
        public void IsStructDeclaration_LowerCaseStructOnSameLineAsHeader_ReturnsTrue()
        {
            const string declaration = "type ST_Point : struct\n\tx : REAL;\nend_struct\nend_type";

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
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "xstunit-enumcomment-" + Guid.NewGuid()));
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
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "xstunit-aliasstruct-" + Guid.NewGuid()));
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

        [Theory]
        [InlineData("TYPE ST_Child EXTENDS ST_Base :\nSTRUCT\n\tx : REAL;\nEND_STRUCT\nEND_TYPE")]
        [InlineData("TYPE ST_Child EXTENDS ST_Base : STRUCT\n\tx : REAL;\nEND_STRUCT\nEND_TYPE")]
        [InlineData("TYPE ST_Child EXTENDS Lib.ST_Base :\nSTRUCT\n\tx : REAL;\nEND_STRUCT\nEND_TYPE")]
        [InlineData("type   ST_Child   extends   ST_Base:struct\n\tx : REAL;\nend_struct\nend_type")]
        [InlineData("{attribute 'pack_mode' := '1'}\n(* header *)\n// note\nTYPE ST_Child EXTENDS ST_Base :\r\nSTRUCT\r\n\tx : REAL;\r\nEND_STRUCT\r\nEND_TYPE")]
        public void Load_ExtendsStructDut_IsSkippedWithReasonNamingTheType(string declaration)
        {
            // A struct DUT with an EXTENDS clause has no field-inheritance
            // model, so it must be reported as a skipped file naming the type
            // rather than vanishing or registering with only its own fields -
            // in any spelling of the header.
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "xstunit-extendsstruct-" + Guid.NewGuid()));
            try
            {
                var path = Path.Combine(tempDir.FullName, "ST_Child.TcDUT");
                File.WriteAllText(path, DutXml("ST_Child", declaration));

                var structTypes = DutStructLoader.Load(new[] { tempDir.FullName }, out var skipped);

                Assert.Empty(structTypes);
                var skip = Assert.Single(skipped);
                Assert.Equal(path, skip.FileKey);
                Assert.Contains("ST_Child", skip.Message);
                Assert.Contains("Struct 'ST_Child' EXTENDS", skip.Message);
                Assert.Contains("inheritance is not supported", skip.Message);
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }

        [Fact]
        public void Load_ExtendsUnionDut_SkipReasonSaysUnion()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "xstunit-extendsunion-" + Guid.NewGuid()));
            try
            {
                File.WriteAllText(Path.Combine(tempDir.FullName, "U_Child.TcDUT"), DutXml("U_Child", "TYPE U_Child EXTENDS U_Base :\nUNION\n\tx : REAL;\nEND_UNION\nEND_TYPE"));

                DutStructLoader.Load(new[] { tempDir.FullName }, out var skipped);

                Assert.StartsWith("Union 'U_Child' EXTENDS 'U_Base'", Assert.Single(skipped).Message);
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }

        [Fact]
        public void Load_ExtendsStructDutAlongsidePlainStruct_StillLoadsThePlainStruct()
        {
            // Skipping the inheriting struct must cost only that file.
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "xstunit-extendsmixed-" + Guid.NewGuid()));
            try
            {
                File.WriteAllText(Path.Combine(tempDir.FullName, "ST_Child.TcDUT"), DutXml("ST_Child", "TYPE ST_Child EXTENDS ST_Base :\nSTRUCT\n\tx : REAL;\nEND_STRUCT\nEND_TYPE"));
                File.WriteAllText(Path.Combine(tempDir.FullName, "ST_Base.TcDUT"), DutXml("ST_Base", "TYPE ST_Base :\nSTRUCT\n\ty : REAL;\nEND_STRUCT\nEND_TYPE"));

                var structTypes = DutStructLoader.Load(new[] { tempDir.FullName }, out var skipped);

                Assert.Equal("ST_Base", Assert.Single(structTypes).Name);
                Assert.Single(skipped);
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }

        [Fact]
        public void Load_ActualStructDut_IsRegistered()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "xstunit-realstruct-" + Guid.NewGuid()));
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

        // A UNION DUT loads alongside the STRUCT DUTs, carrying the flag that
        // separates the two layouts. Skip it and every type holding one is
        // unsizable, which is how TcUnit's own ST_AssertResult was reachable
        // only as an unsupported construct.
        [Fact]
        public void Load_UnionDut_IsRegisteredAndMarkedAUnion()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "xstunit-union-" + Guid.NewGuid()));
            try
            {
                const string declaration = "TYPE U_Overlaid :\nUNION\n\tasWord : WORD;\n\tasLong : LWORD;\nEND_UNION\nEND_TYPE";
                File.WriteAllText(Path.Combine(tempDir.FullName, "U_Overlaid.TcDUT"), DutXml("U_Overlaid", declaration));

                var structTypes = DutStructLoader.Load(new[] { tempDir.FullName }, out var skipped);

                Assert.Empty(skipped);
                var unionAst = Assert.Single(structTypes);
                Assert.Equal("U_Overlaid", unionAst.Name);
                Assert.True(unionAst.IsUnion);
                Assert.Equal(new[] { "asWord", "asLong" }, unionAst.Fields.Select(f => f.Name));
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }

        // The STRUCT filter stays a STRUCT filter: DutAliasLoader consults it to
        // decide a DUT is not an alias, and a union answering true there would
        // change what that means.
        [Fact]
        public void IsStructDeclaration_UnionDeclaration_ReturnsFalse()
        {
            const string declaration = @"TYPE U_Overlaid :
UNION
	asWord : WORD;
END_UNION
END_TYPE";

            Assert.False(DutStructLoader.IsStructDeclaration(declaration));
        }
    }
}
