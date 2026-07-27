using System.Collections.Generic;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-gd2.6: AssertArrayEquals_<TYPE> for the 12 non-float IEC
    // scalar types (BOOL/BYTE/DINT/DWORD/INT/LINT/SINT/UDINT/UINT/ULINT/
    // USINT/WORD) - the ARRAY[*] counterpart to the scalar AssertEquals_
    // <TYPE> tests in NativeMethodBridgeAssertTests.cs. One theory per
    // failure mode (shape mismatch vs per-element mismatch) covers all 12
    // types via the same table-driven dispatcher rather than 12x duplicated
    // test methods.
    public class NativeMethodBridgeArrayAssertTests
    {
        private static Engine NewSuiteEngine(string declarationText, string implementationText)
        {
            var suite = new PouAst("FB_MySuite", "TcUnit.FB_TestSuite", declarationText, implementationText, new List<MethodAst>());
            return new Engine(new TypeRegistry(new[] { suite }));
        }

        public static IEnumerable<object[]> ArrayTypes => new[]
        {
            new object[] { "BOOL", "[TRUE, FALSE, TRUE]", "[TRUE, TRUE, TRUE]", "FALSE", "TRUE" },
            new object[] { "BYTE", "[10, 20, 30]", "[10, 99, 30]", "20", "99" },
            new object[] { "SINT", "[10, 20, 30]", "[10, 99, 30]", "20", "99" },
            new object[] { "USINT", "[10, 20, 30]", "[10, 99, 30]", "20", "99" },
            new object[] { "WORD", "[10, 20, 30]", "[10, 99, 30]", "20", "99" },
            new object[] { "UINT", "[10, 20, 30]", "[10, 99, 30]", "20", "99" },
            new object[] { "DINT", "[10, 20, 30]", "[10, 99, 30]", "20", "99" },
            new object[] { "DWORD", "[10, 20, 30]", "[10, 99, 30]", "20", "99" },
            new object[] { "UDINT", "[10, 20, 30]", "[10, 99, 30]", "20", "99" },
            new object[] { "LINT", "[10, 20, 30]", "[10, 99, 30]", "20", "99" },
            new object[] { "ULINT", "[10, 20, 30]", "[10, 99, 30]", "20", "99" },
            new object[] { "INT", "[10, 20, 30]", "[10, 99, 30]", "20", "99" },
        };

        [Theory]
        [MemberData(nameof(ArrayTypes))]
        public void RunSuite_AssertArrayEquals_MatchingArrays_Passes(
            string typeName, string matchingLiteral, string mismatchLiteral, string expectedFormatted, string actualFormatted)
        {
            var declarationText =
                $"VAR\n" +
                $"aExpecteds : ARRAY[0..2] OF {typeName} := {matchingLiteral};\n" +
                $"aActuals : ARRAY[0..2] OF {typeName} := {matchingLiteral};\n" +
                "END_VAR";

            var engine = NewSuiteEngine(
                declarationText,
                "TEST('t');\n" +
                "AssertArrayEquals_" + typeName + "(Expecteds := aExpecteds, Actuals := aActuals, Message := 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Theory]
        [MemberData(nameof(ArrayTypes))]
        public void RunSuite_AssertArrayEquals_SizeMismatch_ReportsSizeFailure(
            string typeName, string matchingLiteral, string mismatchLiteral, string expectedFormatted, string actualFormatted)
        {
            var declarationText =
                $"VAR\n" +
                $"aExpecteds : ARRAY[0..2] OF {typeName} := {matchingLiteral};\n" +
                $"aActuals : ARRAY[0..1] OF {typeName};\n" +
                "END_VAR";

            var engine = NewSuiteEngine(
                declarationText,
                "TEST('t');\n" +
                "AssertArrayEquals_" + typeName + "(Expecteds := aExpecteds, Actuals := aActuals, Message := 'mismatch');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            Assert.Contains("EXP: SIZE = 3, ACT: SIZE = 2", result.Failures[0].Message);
        }

        [Theory]
        [MemberData(nameof(ArrayTypes))]
        public void RunSuite_AssertArrayEquals_ElementMismatch_ReportsIndexAndValues(
            string typeName, string matchingLiteral, string mismatchLiteral, string expectedFormatted, string actualFormatted)
        {
            var declarationText =
                $"VAR\n" +
                $"aExpecteds : ARRAY[0..2] OF {typeName} := {matchingLiteral};\n" +
                $"aActuals : ARRAY[0..2] OF {typeName} := {mismatchLiteral};\n" +
                "END_VAR";

            var engine = NewSuiteEngine(
                declarationText,
                "TEST('t');\n" +
                "AssertArrayEquals_" + typeName + "(Expecteds := aExpecteds, Actuals := aActuals, Message := 'mismatch');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            Assert.Contains($"EXP: ARRAY[1] = {expectedFormatted}, ACT: ARRAY[1] = {actualFormatted}", result.Failures[0].Message);
        }

        // Lower bounds can legitimately differ between Expecteds/Actuals
        // (upstream's own documented behavior - see FB_TestSuite.TcPOU's
        // AssertArrayEquals_BOOL comment) - as long as the sizes match, a
        // pure lower-bound offset isn't a shape mismatch, and each array's
        // own real (lower-bound-relative) index is reported on a later
        // per-element mismatch.
        [Fact]
        public void RunSuite_AssertArrayEquals_DifferingLowerBounds_ReportsEachArraysOwnIndex()
        {
            var engine = NewSuiteEngine(
                "VAR\n" +
                "aExpecteds : ARRAY[1..3] OF INT := [10, 20, 30];\n" +
                "aActuals : ARRAY[5..7] OF INT := [10, 99, 30];\n" +
                "END_VAR",
                "TEST('t');\n" +
                "AssertArrayEquals_INT(Expecteds := aExpecteds, Actuals := aActuals, Message := 'mismatch');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            Assert.Contains("EXP: ARRAY[2] = 20, ACT: ARRAY[6] = 99", result.Failures[0].Message);
        }

        // TcXunit-gd2.7: AssertArrayEquals_REAL/_LREAL - the float-array
        // counterpart to the 12 non-float types above. Unlike those,
        // upstream's REAL/LREAL array asserts take a caller-supplied Delta
        // VAR_INPUT (verified against FB_TestSuite.TcPOU) and compare
        // ABS(Expecteds[i] - Actuals[i]) > Delta per element - an absolute
        // tolerance, not proportional to the expected value despite that
        // method's own doc comment. One theory per failure mode, same as
        // above, covers both REAL and LREAL via the same dispatcher.
        public static IEnumerable<object[]> FloatArrayTypes => new[]
        {
            new object[] { "REAL" },
            new object[] { "LREAL" },
        };

        [Theory]
        [MemberData(nameof(FloatArrayTypes))]
        public void RunSuite_AssertArrayEquals_WithinDelta_Passes(string typeName)
        {
            var declarationText =
                $"VAR\n" +
                $"aExpecteds : ARRAY[0..2] OF {typeName} := [1.0, 2.0, 3.0];\n" +
                $"aActuals : ARRAY[0..2] OF {typeName} := [1.05, 2.0, 3.0];\n" +
                "END_VAR";

            var engine = NewSuiteEngine(
                declarationText,
                "TEST('t');\n" +
                "AssertArrayEquals_" + typeName + "(Expecteds := aExpecteds, Actuals := aActuals, Delta := 0.1, Message := 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Theory]
        [MemberData(nameof(FloatArrayTypes))]
        public void RunSuite_AssertArrayEquals_OutsideDelta_ReportsIndexAndValues(string typeName)
        {
            var declarationText =
                $"VAR\n" +
                $"aExpecteds : ARRAY[0..2] OF {typeName} := [1.0, 2.0, 3.0];\n" +
                $"aActuals : ARRAY[0..2] OF {typeName} := [1.05, 2.5, 3.0];\n" +
                "END_VAR";

            var engine = NewSuiteEngine(
                declarationText,
                "TEST('t');\n" +
                "AssertArrayEquals_" + typeName + "(Expecteds := aExpecteds, Actuals := aActuals, Delta := 0.1, Message := 'mismatch');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            Assert.Contains("EXP: ARRAY[1] = 2, ACT: ARRAY[1] = 2.5", result.Failures[0].Message);
        }

        [Theory]
        [MemberData(nameof(FloatArrayTypes))]
        public void RunSuite_AssertArrayEquals_ShapeMismatch_ReportsSizeFailure(string typeName)
        {
            var declarationText =
                $"VAR\n" +
                $"aExpecteds : ARRAY[0..2] OF {typeName} := [1.0, 2.0, 3.0];\n" +
                $"aActuals : ARRAY[0..1] OF {typeName};\n" +
                "END_VAR";

            var engine = NewSuiteEngine(
                declarationText,
                "TEST('t');\n" +
                "AssertArrayEquals_" + typeName + "(Expecteds := aExpecteds, Actuals := aActuals, Delta := 0.1, Message := 'mismatch');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            Assert.Contains("EXP: SIZE = 3, ACT: SIZE = 2", result.Failures[0].Message);
        }
    }
}
