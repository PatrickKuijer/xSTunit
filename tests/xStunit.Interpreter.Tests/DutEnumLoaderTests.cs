using System;
using System.IO;
using xStunit.Interpreter;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    public class DutEnumLoaderTests
    {
        [Fact]
        public void TryParseEnum_NoExplicitBaseType_DefaultsToInt()
        {
            const string declaration = "TYPE E_Color :\n(\n\tRed,\n\tGreen,\n\tBlue\n);\nEND_TYPE";

            var parsed = DutEnumLoader.TryParseEnum(declaration, out var name, out var underlying, out _);

            Assert.True(parsed);
            Assert.Equal("E_Color", name);
            Assert.Equal("INT", underlying);
        }

        [Fact]
        public void TryParseEnum_LowerCaseTypeKeyword_ExtractsNameAndMembers()
        {
            const string declaration = "type E_Color : (Red, Green := 5, Blue) dint;\nend_type";

            var parsed = DutEnumLoader.TryParseEnum(declaration, out var name, out var underlying, out var members);

            Assert.True(parsed);
            Assert.Equal("E_Color", name);
            Assert.Equal("dint", underlying);
            Assert.Equal(6, members["Blue"]);
        }

        [Fact]
        public void TryParseEnum_ExplicitBaseType_ExtractsBaseType()
        {
            const string declaration =
                "TYPE eWidgetValueKind : (\n\tIDT_BOOL,\n\tIDT_BYTE,\n\tIDT_INT\n) DINT;\nEND_TYPE";

            var parsed = DutEnumLoader.TryParseEnum(declaration, out var name, out var underlying, out _);

            Assert.True(parsed);
            Assert.Equal("eWidgetValueKind", name);
            Assert.Equal("DINT", underlying);
        }

        [Fact]
        public void TryParseEnum_MembersWithInitializers_DefaultsToInt()
        {
            const string declaration = "TYPE E_Status :\n(\n\tOk := 0,\n\tError := 1\n);\nEND_TYPE";

            var parsed = DutEnumLoader.TryParseEnum(declaration, out var name, out var underlying, out _);

            Assert.True(parsed);
            Assert.Equal("E_Status", name);
            Assert.Equal("INT", underlying);
        }

        [Fact]
        public void TryParseEnum_LeadingAttributePragmas_AreSkipped()
        {
            const string declaration =
                "{attribute 'qualified_only'}\n{attribute 'strict'}\nTYPE eWidgetValueKind :\n(\n\tTypeBool := 0,\n\tTypeInt := 1\n);\nEND_TYPE";

            var parsed = DutEnumLoader.TryParseEnum(declaration, out var name, out var underlying, out _);

            Assert.True(parsed);
            Assert.Equal("eWidgetValueKind", name);
            Assert.Equal("INT", underlying);
        }

        [Fact]
        public void TryParseEnum_LeadingAttributePragmasAndComment_AreSkipped()
        {
            const string declaration =
                "{attribute 'qualified_only'}\n{attribute 'strict'}\n// Channel opcode\nTYPE eWidgetOpcode :\n(\n\tAdd := 0,\n\tRemove := 1\n);\nEND_TYPE";

            var parsed = DutEnumLoader.TryParseEnum(declaration, out var name, out var underlying, out _);

            Assert.True(parsed);
            Assert.Equal("eWidgetOpcode", name);
            Assert.Equal("INT", underlying);
        }

        [Fact]
        public void TryParseEnum_LeadingBlockCommentHeader_IsSkipped()
        {
            // The house style documents a DUT with a multi-line "(* ... *)"
            // header. An enum wearing one still has to register: when it does
            // not, nothing reports a malformed DUT - the type is silently not
            // an enum, and the first symptom is "Unknown variable" from every
            // body that names one of its members.
            const string declaration =
                "(*\n    Which state the machine reports.\n\n    Held as an enum so a body can name the state instead of a magic INT.\n*)\nTYPE E_PackMLState :\n(\n\tIdle,\n\tExecute\n);\nEND_TYPE";

            var parsed = DutEnumLoader.TryParseEnum(declaration, out var name, out var underlying, out var members);

            Assert.True(parsed);
            Assert.Equal("E_PackMLState", name);
            Assert.Equal("INT", underlying);
            Assert.Equal(0, members["Idle"]);
            Assert.Equal(1, members["Execute"]);
        }

        [Fact]
        public void TryParseEnum_LeadingSingleLineBlockComment_IsSkipped()
        {
            const string declaration =
                "(* replaces the old STRUCT-based version *)\nTYPE E_Color :\n(\n\tRed,\n\tGreen\n);\nEND_TYPE";

            var parsed = DutEnumLoader.TryParseEnum(declaration, out var name, out _, out _);

            Assert.True(parsed);
            Assert.Equal("E_Color", name);
        }

        [Fact]
        public void TryParseEnum_LeadingBlockCommentThenPragmas_AreSkipped()
        {
            // Pragmas and comments come in either order and in any number; the
            // header is whatever survives all of them.
            const string declaration =
                "(*\n    Opcode carried on the channel.\n*)\n{attribute 'qualified_only'}\n// one per command\n{attribute 'strict'}\nTYPE eWidgetOpcode :\n(\n\tAdd := 0,\n\tRemove := 1\n);\nEND_TYPE";

            var parsed = DutEnumLoader.TryParseEnum(declaration, out var name, out _, out var members);

            Assert.True(parsed);
            Assert.Equal("eWidgetOpcode", name);
            Assert.Equal(1, members["Remove"]);
        }

        [Fact]
        public void TryParseEnum_BlockCommentMentioningTypeHeader_TakesTheRealHeaderName()
        {
            // Prose in the header comment can quote a TYPE line; the name must
            // still come from the declaration rather than from the commentary
            // about it.
            const string declaration =
                "(*\n    Supersedes TYPE E_Legacy : (A, B);\n*)\nTYPE E_Color :\n(\n\tRed,\n\tGreen\n);\nEND_TYPE";

            var parsed = DutEnumLoader.TryParseEnum(declaration, out var name, out _, out _);

            Assert.True(parsed);
            Assert.Equal("E_Color", name);
        }

        [Fact]
        public void TryParseEnum_AliasDeclaration_ReturnsFalse()
        {
            const string declaration = "TYPE T_MaxString : STRING(255);\nEND_TYPE";

            Assert.False(DutEnumLoader.TryParseEnum(declaration, out _, out _, out _));
        }

        [Fact]
        public void TryParseEnum_StructDeclaration_ReturnsFalse()
        {
            const string declaration = "TYPE ST_Point :\nSTRUCT\n\tx : REAL;\nEND_STRUCT\nEND_TYPE";

            Assert.False(DutEnumLoader.TryParseEnum(declaration, out _, out _, out _));
        }

        [Fact]
        public void TryParseEnum_NoExplicitInitializers_NumbersSequentiallyFromZero()
        {
            const string declaration = "TYPE E_Color :\n(\n\tRed,\n\tGreen,\n\tBlue\n);\nEND_TYPE";

            DutEnumLoader.TryParseEnum(declaration, out _, out _, out var members);

            Assert.Equal(0, members["Red"]);
            Assert.Equal(1, members["Green"]);
            Assert.Equal(2, members["Blue"]);
        }

        [Fact]
        public void TryParseEnum_ExplicitInitializers_UsesDeclaredValues()
        {
            const string declaration = "TYPE E_Status :\n(\n\tOk := 0,\n\tError := 1\n);\nEND_TYPE";

            DutEnumLoader.TryParseEnum(declaration, out _, out _, out var members);

            Assert.Equal(0, members["Ok"]);
            Assert.Equal(1, members["Error"]);
        }

        [Fact]
        public void TryParseEnum_MixedExplicitAndImplicit_ImplicitContinuesFromLastExplicitValue()
        {
            const string declaration =
                "TYPE eWidgetValueKind :\n(\n\tTypeBool := 5,\n\tTypeByte,\n\tTypeInt\n);\nEND_TYPE";

            DutEnumLoader.TryParseEnum(declaration, out _, out _, out var members);

            Assert.Equal(5, members["TypeBool"]);
            Assert.Equal(6, members["TypeByte"]);
            Assert.Equal(7, members["TypeInt"]);
        }

        [Fact]
        public void TryParseEnum_LeadingAttributePragmasAndComment_MemberTableIsExtracted()
        {
            const string declaration =
                "{attribute 'qualified_only'}\n{attribute 'strict'}\n// Channel opcode\nTYPE eWidgetOpcode :\n(\n\tAdd := 0,\n\tRemove := 1\n);\nEND_TYPE";

            DutEnumLoader.TryParseEnum(declaration, out _, out _, out var members);

            Assert.Equal(0, members["Add"]);
            Assert.Equal(1, members["Remove"]);
        }

        [Fact]
        public void TryParseEnum_MemberWithTrailingLineComment_CommentIsIgnored()
        {
            // An enum member's initializer can be followed by a trailing
            // "// ..." explanation on the same line; if that text reaches the
            // integer parse, a whole DUT fails to load over a comment.
            const string declaration =
                "TYPE eFollowerRampMode :\n(\n\tTypeA := 1\t\t\t// Ramp behavior depends on another axis's progress; limits are not considered\n\t,\n\tTypeB\n);\nEND_TYPE";

            var parsed = DutEnumLoader.TryParseEnum(declaration, out var name, out _, out var members);

            Assert.True(parsed);
            Assert.Equal("eFollowerRampMode", name);
            Assert.Equal(1, members["TypeA"]);
            Assert.Equal(2, members["TypeB"]);
        }

        [Theory]
        [InlineData("16#8", 8)]
        [InlineData("8#17", 15)]
        [InlineData("2#1010", 10)]
        [InlineData("-1", -1)]
        public void TryParseEnum_MemberWithBasedOrNegativeInitializer_ResolvesToDecimalValue(string initializer, int expected)
        {
            // An explicit initializer need not be a plain decimal literal:
            // IEC 61131-3 based-literal notation (<base>#<digits>) is equally
            // valid there, and the implicit members after it keep counting
            // from the resolved decimal value.
            var declaration = $"TYPE eBasedLiteral :\n(\n\tTypeA := {initializer},\n\tTypeB\n);\nEND_TYPE";

            DutEnumLoader.TryParseEnum(declaration, out _, out _, out var members);

            Assert.Equal(expected, members["TypeA"]);
            Assert.Equal(expected + 1, members["TypeB"]);
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
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "xstunit-enum-" + Guid.NewGuid()));
            try
            {
                const string declaration = "TYPE E_Color :\n(\n\tRed,\n\tGreen,\n\tBlue\n);\nEND_TYPE";
                File.WriteAllText(Path.Combine(tempDir.FullName, "E_Color.TcDUT"), DutXml("E_Color", declaration));

                var enums = DutEnumLoader.Load(new[] { tempDir.FullName }, out var skipped, out var memberTables);

                Assert.Empty(skipped);
                Assert.Equal("INT", enums["E_Color"]);
                Assert.Equal(0, memberTables["E_Color"]["Red"]);
                Assert.Equal(1, memberTables["E_Color"]["Green"]);
                Assert.Equal(2, memberTables["E_Color"]["Blue"]);
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }

        [Fact]
        public void Load_EnumDutWithBlockCommentHeader_IsRegisteredWithMembers()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "xstunit-enumblockcomment-" + Guid.NewGuid()));
            try
            {
                const string declaration = @"(*
    Which state the machine reports, named rather than numbered.
*)
TYPE E_PackMLState :
(
	Idle,
	Execute
);
END_TYPE";
                File.WriteAllText(Path.Combine(tempDir.FullName, "E_PackMLState.TcDUT"), DutXml("E_PackMLState", declaration));

                var enums = DutEnumLoader.Load(new[] { tempDir.FullName }, out var skipped, out var memberTables);

                Assert.Empty(skipped);
                Assert.Equal("INT", enums["E_PackMLState"]);
                Assert.Equal(1, memberTables["E_PackMLState"]["Execute"]);
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }

        [Fact]
        public void Load_StructDut_IsNotRegisteredAsEnum()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "xstunit-enumstructskip-" + Guid.NewGuid()));
            try
            {
                const string declaration = "TYPE ST_Point :\nSTRUCT\n\tx : REAL;\nEND_STRUCT\nEND_TYPE";
                File.WriteAllText(Path.Combine(tempDir.FullName, "ST_Point.TcDUT"), DutXml("ST_Point", declaration));

                var enums = DutEnumLoader.Load(new[] { tempDir.FullName }, out var skipped, out var memberTables);

                Assert.Empty(skipped);
                Assert.Empty(enums);
                Assert.Empty(memberTables);
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }
    }
}
