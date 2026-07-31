using xStunit.Interpreter;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcXunit-cq6: VarBlockParser only ever looked inside VAR...END_VAR, so a
    // callable's declared return type - which lives on the METHOD/FUNCTION
    // header line itself - was never parsed at all. These cover the header
    // parse in isolation; the Engine-level consequence of not having it is
    // covered by CallableReturnTypeSeedingTests.
    public class CallableReturnTypeParserTests
    {
        [Theory]
        [InlineData("METHOD M_Read : LREAL", "LREAL")]
        [InlineData("METHOD PRIVATE M_Read : LREAL", "LREAL")]
        [InlineData("METHOD PUBLIC M_Read : REAL", "REAL")]
        [InlineData("METHOD PROTECTED M_Read : INT", "INT")]
        [InlineData("METHOD INTERNAL M_Read : DINT", "DINT")]
        [InlineData("METHOD PUBLIC ABSTRACT M_Read : UDINT", "UDINT")]
        [InlineData("METHOD FINAL M_Read : ULINT", "ULINT")]
        [InlineData("FUNCTION F_Double : INT", "INT")]
        [InlineData("METHOD M_Read:LREAL", "LREAL")]
        [InlineData("METHOD   M_Read   :   LREAL   ", "LREAL")]
        [InlineData("PROPERTY nGain : LREAL", "LREAL")]
        [InlineData("PROPERTY PUBLIC nGain : REAL", "REAL")]
        public void TryGetReturnTypeName_HeaderWithReturnType_ReturnsIt(string header, string expected)
        {
            Assert.True(CallableReturnTypeParser.TryGetReturnTypeName(header, out var typeName));
            Assert.Equal(expected, typeName);
        }

        [Fact]
        public void TryGetReturnTypeName_LowercaseHeaderKeywords_ParseButTypeTextTravelsVerbatim()
        {
            // ST is case-insensitive, so the METHOD/PRIVATE keywords match
            // either way. The captured type name is NOT normalized, matching
            // VarBlockParser, which likewise hands back declared type text
            // exactly as written - so a lowercase spelling parses here and
            // then misses Engine.SeedReturnCell's case-sensitive
            // IecNumericType lookup. That gap is interpreter-wide and tracked
            // as TcXunit-fzm; this test pins where the boundary currently is.
            Assert.True(CallableReturnTypeParser.TryGetReturnTypeName("method private m_read : lreal", out var typeName));
            Assert.Equal("lreal", typeName);
        }

        [Fact]
        public void TryGetReturnTypeName_HeaderFollowedByVarBlock_ReadsOnlyTheHeaderLine()
        {
            const string decl = "METHOD PRIVATE M_Read : LREAL\nVAR_INPUT\n\tnIndex : UINT;\nEND_VAR";

            Assert.True(CallableReturnTypeParser.TryGetReturnTypeName(decl, out var typeName));
            Assert.Equal("LREAL", typeName);
        }

        [Fact]
        public void TryGetReturnTypeName_HeaderLeadsWithBlockComment_StillParses()
        {
            // Same convention GlobalFunctionDeclarationPattern already had to
            // tolerate (TcXunit-9k6): a purpose comment above the header.
            const string decl = "(* Reads one value.\n   Second line. *)\nMETHOD PRIVATE M_Read : LREAL\nVAR\n\ttfValue : LREAL;\nEND_VAR";

            Assert.True(CallableReturnTypeParser.TryGetReturnTypeName(decl, out var typeName));
            Assert.Equal("LREAL", typeName);
        }

        [Fact]
        public void TryGetReturnTypeName_HeaderLeadsWithLineComment_StillParses()
        {
            const string decl = "// Reads one value.\nMETHOD M_Read : LREAL";

            Assert.True(CallableReturnTypeParser.TryGetReturnTypeName(decl, out var typeName));
            Assert.Equal("LREAL", typeName);
        }

        [Theory]
        [InlineData("METHOD M_Do")]
        [InlineData("METHOD PRIVATE M_Do")]
        [InlineData("METHOD M_Do\nVAR_INPUT\n\tnIn : INT;\nEND_VAR")]
        public void TryGetReturnTypeName_MethodWithNoReturnType_ReturnsFalse(string decl)
        {
            Assert.False(CallableReturnTypeParser.TryGetReturnTypeName(decl, out var typeName));
            Assert.Null(typeName);
        }

        [Theory]
        // A FUNCTION_BLOCK/PROGRAM/INTERFACE header has no return type, and an
        // FB's declaration text is the bare VAR block in hand-built ASTs.
        [InlineData("FUNCTION_BLOCK FB_Widget EXTENDS FB_Base")]
        [InlineData("PROGRAM MAIN")]
        [InlineData("VAR\n\tnValue : INT := 21;\nEND_VAR")]
        [InlineData("")]
        public void TryGetReturnTypeName_NotACallableHeader_ReturnsFalse(string decl)
        {
            Assert.False(CallableReturnTypeParser.TryGetReturnTypeName(decl, out var typeName));
            Assert.Null(typeName);
        }

        [Fact]
        public void TryGetReturnTypeName_MethodNamedLikeAnAccessModifierPrefix_IsNotEatenByIt()
        {
            // "FINAL" is an access-modifier alternative; a method named
            // FINALIZE must not have its first five characters consumed as one.
            Assert.True(CallableReturnTypeParser.TryGetReturnTypeName("METHOD FINALIZE : INT", out var typeName));
            Assert.Equal("INT", typeName);
        }

        [Fact]
        public void TryGetReturnTypeName_StringReturnTypeWithLength_KeepsTheLength()
        {
            Assert.True(CallableReturnTypeParser.TryGetReturnTypeName("METHOD M_Name : STRING(80)", out var typeName));
            Assert.Equal("STRING(80)", typeName);
        }
    }
}
