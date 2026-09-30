using System.Collections.Generic;
using xStunit.Parser;
using xStunit.Runner;
using xStunit.Runner.TcUnitStub;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // Typed integer/bit-string/bool prefixes (INT#5, DINT#16#FF, BOOL#1) must
    // be accepted at every position that takes an integer literal, and the
    // literal must carry the type it names so the ANY asserts see that type
    // rather than an adopted one.
    public class TypedIntegerLiteralTests
    {
        private static TestCaseResult RunBody(string declaration, string body)
        {
            var suite = new PouAst(
                "FB_MySuite",
                "TcUnit.FB_TestSuite",
                "VAR\n" + declaration + "\nEND_VAR",
                "TEST('t');\n" + body + "\nTEST_FINISHED();",
                new List<MethodAst>());

            return Assert.Single(new Engine(new TypeRegistry(new[] { suite })).RunSuite("FB_MySuite"));
        }

        [Theory]
        [InlineData("SINT", "SINT#-5", -5)]
        [InlineData("INT", "int#5", 5)]
        [InlineData("DINT", "Dint#16#FF", 255)]
        [InlineData("BYTE", "BYTE#2#1010", 10)]
        [InlineData("WORD", "WORD#8#17", 15)]
        [InlineData("USINT", "USINT#255", 255)]
        [InlineData("UINT", "UINT#65535", 65535)]
        public void RunSuite_TypedLiteralAssignedToSameTypeVariable_HoldsTheValue(string typeName, string literal, int expected)
        {
            var result = RunBody($"nValue : {typeName};", $"nValue := {literal};\nAssertEquals({literal}, nValue, 'typed');\nAssertEquals_DINT({expected}, nValue, 'value');");

            Assert.True(result.Passed, string.Join("; ", result.Failures));
        }

        [Theory]
        [InlineData("UDINT", "UDINT#4294967295", 4294967295L)]
        [InlineData("DWORD", "DWORD#16#FFFF_FFFF", 4294967295L)]
        [InlineData("LINT", "LINT#-9", -9L)]
        public void RunSuite_TypedLongBoxLiteral_HoldsTheValue(string typeName, string literal, long expected)
        {
            var result = RunBody($"nValue : {typeName};", $"nValue := {literal};\nAssertEquals({literal}, nValue, 'typed');\nAssertEquals_LINT({expected}, nValue, 'value');");

            Assert.True(result.Passed, string.Join("; ", result.Failures));
        }

        [Fact]
        public void RunSuite_TypedUlintLiteral_HoldsTheFullUnsignedRange()
        {
            var result = RunBody("nValue : LWORD;", "nValue := LWORD#16#FFFFFFFFFFFFFFFF;\nAssertEquals(LWORD#16#FFFFFFFFFFFFFFFF, nValue, 'typed');");

            Assert.True(result.Passed, string.Join("; ", result.Failures));
        }

        // The named type wins over adoption: INT#5 against a DINT is a type
        // mismatch, whereas the plain literal 5 would have adopted DINT.
        [Fact]
        public void RunSuite_AssertEqualsAny_TypedLiteralAgainstDifferentDeclaredType_FailsOnTypeMismatch()
        {
            Assert.True(RunBody("nValue : INT := 5;", "AssertEquals(INT#5, nValue, 'same');").Passed);
            Assert.False(RunBody("nValue : DINT := 5;", "AssertEquals(INT#5, nValue, 'other');").Passed);
            Assert.False(RunBody("nValue : DINT := 5;", "AssertEquals(nValue, INT#5, 'other');").Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsAny_NegativeTypedLiteral_KeepsItsType()
        {
            Assert.True(RunBody("nValue : INT := -5;", "AssertEquals(-INT#5, nValue, 'same');").Passed);
            Assert.False(RunBody("nValue : DINT := -5;", "AssertEquals(-INT#5, nValue, 'other');").Passed);
        }

        [Theory]
        [InlineData("BOOL#1", true)]
        [InlineData("bool#TRUE", true)]
        [InlineData("BOOL#0", false)]
        [InlineData("Bool#FALSE", false)]
        public void RunSuite_TypedBoolLiteral_IsABool(string literal, bool expected)
        {
            var result = RunBody("bFlag : BOOL;", $"bFlag := {literal};\nAssertEquals_BOOL({(expected ? "TRUE" : "FALSE")}, bFlag, 'bool');");

            Assert.True(result.Passed, string.Join("; ", result.Failures));
        }

        [Fact]
        public void RunSuite_TypedLiteralInDeclarationInitialiser_InitialisesTheVariable()
        {
            var result = RunBody("nValue : INT := INT#-7;\nnMask : BYTE := BYTE#2#1010;", "AssertEquals_INT(-7, nValue, 'a');\nAssertEquals_BYTE(10, nMask, 'b');");

            Assert.True(result.Passed, string.Join("; ", result.Failures));
        }

        [Fact]
        public void RunSuite_TypedLiteralsAsCaseLabelsAndRanges_SelectTheArm()
        {
            var result = RunBody(
                "nSel : INT := 2;\nnHit : INT;\nnRange : INT;",
                "CASE nSel OF\nINT#1: nHit := 1;\nINT#2: nHit := 2;\nEND_CASE\n" +
                "CASE nSel OF\nINT#1..INT#3: nRange := 9;\nELSE nRange := 0;\nEND_CASE\n" +
                "AssertEquals_INT(2, nHit, 'label');\nAssertEquals_INT(9, nRange, 'range');");

            Assert.True(result.Passed, string.Join("; ", result.Failures));
        }

        [Fact]
        public void RunSuite_TypedLiteralsAsArrayBounds_SizeTheArray()
        {
            var result = RunBody(
                "aValues : ARRAY[INT#1..INT#3] OF INT := [10, 20, 30];",
                "AssertEquals_INT(10, aValues[1], 'lo');\nAssertEquals_INT(30, aValues[3], 'hi');");

            Assert.True(result.Passed, string.Join("; ", result.Failures));
        }

        [Fact]
        public void RunSuite_TypedLiteralAsArrayInitialiserRepeatCount_RepeatsTheValue()
        {
            var result = RunBody(
                "aValues : ARRAY[1..3] OF INT := [INT#3(4)];",
                "AssertEquals_INT(4, aValues[1], 'first');\nAssertEquals_INT(4, aValues[3], 'last');");

            Assert.True(result.Passed, string.Join("; ", result.Failures));
        }

        [Fact]
        public void RunSuite_TypedLiteralInArithmetic_EvaluatesLikeThePlainLiteral()
        {
            var result = RunBody("nValue : INT;", "nValue := INT#2 + DINT#16#10 * 2;\nAssertEquals_INT(34, nValue, 'sum');");

            Assert.True(result.Passed, string.Join("; ", result.Failures));
        }

        [Fact]
        public void ParseExpression_OutOfRangeTypedLiteral_ThrowsNamingTheLiteral()
        {
            var ex = Assert.Throws<ParseException>(() => Parser.ParseExpression("SINT#200"));

            Assert.Contains("SINT#200", ex.Message);
        }
    }
}
