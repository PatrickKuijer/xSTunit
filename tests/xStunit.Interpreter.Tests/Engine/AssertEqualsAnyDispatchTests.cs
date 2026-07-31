using System.Collections.Generic;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcXunit-gd2.5: type-erased AssertEquals(Expected: ANY, Actual: ANY,
    // Message) dispatcher. Unlike the AssertEquals_<TYPE> tests in
    // NativeMethodBridgeAssertTests.cs, these calls carry no type suffix -
    // Expected/Actual's IEC type has to come from the *declared* type of the
    // variable passed in (there's no literal fallback), so every case here
    // declares typed VAR fields and calls AssertEquals with bare
    // identifiers, mirroring how SIZEOF() resolves a declared type.
    public class AssertEqualsAnyDispatchTests
    {
        private static Engine NewSuiteEngine(string declarationText, string implementationText)
        {
            var suite = new PouAst("FB_MySuite", "TcUnit.FB_TestSuite", declarationText, implementationText, new List<MethodAst>());
            return new Engine(new TypeRegistry(new[] { suite }));
        }

        [Theory]
        [InlineData("nExpected : INT := 5;\nnActual : INT := 5;")]
        [InlineData("byExpected : BYTE := 5;\nbyActual : BYTE := 5;")]
        [InlineData("sExpected : SINT := 5;\nsActual : SINT := 5;")]
        [InlineData("usExpected : USINT := 5;\nusActual : USINT := 5;")]
        [InlineData("wExpected : WORD := 5;\nwActual : WORD := 5;")]
        [InlineData("uExpected : UINT := 5;\nuActual : UINT := 5;")]
        [InlineData("dExpected : DINT := 5;\ndActual : DINT := 5;")]
        [InlineData("dwExpected : DWORD := 5;\ndwActual : DWORD := 5;")]
        [InlineData("udExpected : UDINT := 5;\nudActual : UDINT := 5;")]
        [InlineData("lExpected : LINT := 5;\nlActual : LINT := 5;")]
        [InlineData("lwExpected : LWORD := 5;\nlwActual : LWORD := 5;")]
        [InlineData("ulExpected : ULINT := 5;\nulActual : ULINT := 5;")]
        [InlineData("rExpected : REAL := 1.5;\nrActual : REAL := 1.5;")]
        [InlineData("lrExpected : LREAL := 1.5;\nlrActual : LREAL := 1.5;")]
        [InlineData("bExpected : BOOL := TRUE;\nbActual : BOOL := TRUE;")]
        [InlineData("tExpected : TIME := T#1s;\ntActual : TIME := T#1s;")]
        [InlineData("ltExpected : LTIME := LTIME#1s;\nltActual : LTIME := LTIME#1s;")]
        public void RunSuite_AssertEqualsAny_DispatchesToMatchingScalarType_AndPasses(string declarations)
        {
            var declarationText = $"VAR\n{declarations}\nEND_VAR";
            var identifiers = ExtractIdentifiers(declarations);

            var engine = NewSuiteEngine(
                declarationText,
                "TEST('t');\n" +
                $"AssertEquals(Expected := {identifiers.Expected}, Actual := {identifiers.Actual}, Message := 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [InlineData("STRING", "'abc'", "'abc'")]
        [InlineData("WSTRING", "\"abc\"", "\"abc\"")]
        [Theory]
        public void RunSuite_AssertEqualsAny_DispatchesToStringFamily_AndPasses(string typeName, string expectedLiteral, string actualLiteral)
        {
            var declarationText =
                $"VAR\n" +
                $"sExpected : {typeName} := {expectedLiteral};\n" +
                $"sActual : {typeName} := {actualLiteral};\n" +
                "END_VAR";

            var engine = NewSuiteEngine(
                declarationText,
                "TEST('t');\n" +
                "AssertEquals(Expected := sExpected, Actual := sActual, Message := 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsAny_ValueMismatch_ReportsFailureUsingTypedFormatter()
        {
            var engine = NewSuiteEngine(
                "VAR\nnExpected : INT := 5;\nnActual : INT := 6;\nEND_VAR",
                "TEST('t');\n" +
                "AssertEquals(Expected := nExpected, Actual := nActual, Message := 'mismatch');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            Assert.Contains("EXP: 5, ACT: 6", result.Failures[0].Message);
            Assert.Contains("MSG: mismatch", result.Failures[0].Message);
        }

        [Fact]
        public void RunSuite_AssertEqualsAny_TypeMismatch_FailsWithoutComparingValues()
        {
            var engine = NewSuiteEngine(
                "VAR\nnExpected : INT := 5;\nsActual : STRING := '5';\nEND_VAR",
                "TEST('t');\n" +
                "AssertEquals(Expected := nExpected, Actual := sActual, Message := 'type mismatch');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            Assert.Contains("EXP: (Type class = INT)", result.Failures[0].Message);
            Assert.Contains("ACT: (Type class = STRING)", result.Failures[0].Message);
        }

        [Fact]
        public void RunSuite_AssertEqualsAny_Real_UsesExactEquality_NotCallerDelta()
        {
            // Unlike AssertEquals_REAL (caller-supplied Delta), the ANY
            // overload always compares with Delta := 0.0 - a value within
            // what would otherwise be an acceptable tolerance still fails.
            var engine = NewSuiteEngine(
                "VAR\nrExpected : REAL := 1.0;\nrActual : REAL := 1.05;\nEND_VAR",
                "TEST('t');\n" +
                "AssertEquals(Expected := rExpected, Actual := rActual, Message := 'no delta on ANY overload');\n" +
                "TEST_FINISHED();");

            Assert.False(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsAny_PositionalArgs_ReachableThroughInterpreter()
        {
            var engine = NewSuiteEngine(
                "VAR\nnExpected : INT := 5;\nnActual : INT := 5;\nEND_VAR",
                "TEST('t');\n" +
                "AssertEquals(nExpected, nActual, 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        private static (string Expected, string Actual) ExtractIdentifiers(string declarations)
        {
            // Each InlineData row declares exactly two fields, one per line,
            // named "<prefix>Expected"/"<prefix>Actual" - pull their
            // identifiers out rather than repeating them in a second column.
            var lines = declarations.Split('\n');
            var expectedName = lines[0].Substring(0, lines[0].IndexOf(':')).Trim();
            var actualName = lines[1].Substring(0, lines[1].IndexOf(':')).Trim();

            return (expectedName, actualName);
        }
    }
}
