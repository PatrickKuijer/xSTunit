using xStunit.Interpreter;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A .TcDUT is an ENUM, a STRUCT or an ALIAS, and each kind is picked up by
    // its own loader. Documenting one is not supposed to change which loader
    // claims it, so the three have to skip the same leading pragmas and
    // comments. They diverged once already - a "(* ... *)" header registered a
    // STRUCT but silently lost an ENUM - and these run the identical preamble
    // past all three so the next documented DUT cannot fall down that crack.
    public class DutDeclarationPreambleTests
    {
        private const string EnumDeclaration = "TYPE E_Color :\n(\n\tRed,\n\tGreen\n);\nEND_TYPE";
        private const string StructDeclaration = "TYPE ST_Point :\nSTRUCT\n\tx : REAL;\nEND_STRUCT\nEND_TYPE";
        private const string AliasDeclaration = "TYPE T_Counter : INT;\nEND_TYPE";

        public static TheoryData<string> Preambles => new TheoryData<string>
        {
            "",
            "{attribute 'qualified_only'}\n",
            "// one-line note\n",
            "(* one-line note *)\n",
            "(*\n    A multi-line header in the house style,\n    two paragraphs and all.\n*)\n",
            "(* note *)\n{attribute 'strict'}\n// trailing note\n",
            "{attribute 'strict'}\n(* note after the pragma *)\n",
        };

        [Theory]
        [MemberData(nameof(Preambles))]
        public void EnumDeclaration_BehindAnyPreamble_StillParses(string preamble)
        {
            var parsed = DutEnumLoader.TryParseEnum(preamble + EnumDeclaration, out var name, out _, out var members);

            Assert.True(parsed);
            Assert.Equal("E_Color", name);
            Assert.Equal(1, members["Green"]);
        }

        [Theory]
        [MemberData(nameof(Preambles))]
        public void StructDeclaration_BehindAnyPreamble_StillParses(string preamble)
        {
            var declaration = preamble + StructDeclaration;

            Assert.True(DutStructLoader.IsStructDeclaration(declaration));
            Assert.Equal("ST_Point", StructDeclParser.Parse(declaration).Name);
        }

        [Theory]
        [MemberData(nameof(Preambles))]
        public void AliasDeclaration_BehindAnyPreamble_StillParses(string preamble)
        {
            var parsed = DutAliasLoader.TryParseAlias(preamble + AliasDeclaration, out var name, out var underlying);

            Assert.True(parsed);
            Assert.Equal("T_Counter", name);
            Assert.Equal("INT", underlying);
        }

        // Skipping the preamble must not turn one kind of DUT into another: the
        // loaders that refuse a declaration have to keep refusing it however it
        // is documented.
        [Theory]
        [MemberData(nameof(Preambles))]
        public void EnumDeclaration_BehindAnyPreamble_IsClaimedByNoOtherLoader(string preamble)
        {
            var declaration = preamble + EnumDeclaration;

            Assert.False(DutStructLoader.IsStructDeclaration(declaration));
            Assert.False(DutAliasLoader.TryParseAlias(declaration, out _, out _));
        }

        [Theory]
        [MemberData(nameof(Preambles))]
        public void StructDeclaration_BehindAnyPreamble_IsClaimedByNoOtherLoader(string preamble)
        {
            var declaration = preamble + StructDeclaration;

            Assert.False(DutEnumLoader.TryParseEnum(declaration, out _, out _, out _));
            Assert.False(DutAliasLoader.TryParseAlias(declaration, out _, out _));
        }

        [Theory]
        [MemberData(nameof(Preambles))]
        public void AliasDeclaration_BehindAnyPreamble_IsClaimedByNoOtherLoader(string preamble)
        {
            var declaration = preamble + AliasDeclaration;

            Assert.False(DutEnumLoader.TryParseEnum(declaration, out _, out _, out _));
            Assert.False(DutStructLoader.IsStructDeclaration(declaration));
        }

        // An unterminated "(*" leaves no header to find. The DUT is malformed
        // either way, but consuming the rest of the text would hand the loaders
        // an empty declaration instead of an unrecognised one.
        [Fact]
        public void UnterminatedBlockComment_IsNotTreatedAsPreamble()
        {
            const string declaration = "(* forgot to close\n" + EnumDeclaration;

            Assert.False(DutEnumLoader.TryParseEnum(declaration, out _, out _, out _));
            Assert.StartsWith("(*", DutDeclarationPreamble.Strip(declaration));
        }

        [Fact]
        public void NestedBlockComment_IsSkippedToItsOuterClose()
        {
            const string declaration = "(* outer (* inner *) still outer *)\n" + EnumDeclaration;

            Assert.Equal(EnumDeclaration, DutDeclarationPreamble.Strip(declaration));
        }
    }
}
