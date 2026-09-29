using xStunit.Interpreter;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A callable's declared return type lives on the METHOD/FUNCTION/PROPERTY
    // header line, not in any VAR block, so it needs a parser of its own.
    // CallableReturnTypeSeedingTests covers what the Engine then does with it.
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
        [InlineData("METHOD M_Arr : ARRAY[1..3] OF INT", "ARRAY[1..3] OF INT")]
        [InlineData("PROPERTY aVals : ARRAY[0..1, 1..2] OF STRING(10)", "ARRAY[0..1, 1..2] OF STRING(10)")]
        public void TryGetReturnTypeName_HeaderWithReturnType_ReturnsIt(string header, string expected)
        {
            Assert.True(CallableReturnTypeParser.TryGetReturnTypeName(header, out var typeName));
            Assert.Equal(expected, typeName);
        }

        [Fact]
        public void TryGetReturnTypeName_LowercaseHeaderKeywords_ParseButTypeTextTravelsVerbatim()
        {
            // ST is case-insensitive, so the keywords match either way, but the
            // captured type name travels verbatim - as it does out of
            // VarBlockParser. A lowercase spelling therefore parses here and
            // only later misses the case-sensitive type lookup downstream. This
            // pins where that boundary currently sits, not where it belongs.
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
            // FINAL is an access modifier, so a method whose name merely starts
            // with it must not have those five characters eaten as one.
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
