using System.Collections.Generic;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // The array counterpart to the scalar asserts in
    // NativeMethodBridgeAssertTests. All twelve non-float IEC scalar types go
    // through one table-driven dispatcher, so a theory per failure mode covers
    // them without twelve near-identical test methods.
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

        // Two arrays of equal size but different lower bounds compare
        // element-by-element rather than counting as a shape mismatch, and a
        // reported index is each array's own declared index - so the two sides
        // of one failure message can legitimately name different numbers.
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

        // The float array asserts take a caller-supplied Delta and compare
        // ABS(Expecteds[i] - Actuals[i]) > Delta per element. That tolerance is
        // absolute, not proportional to the expected value - upstream's own doc
        // comment says otherwise, and the behaviour is what is pinned here.
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

        // Multi-dimensional arrays are stored flat and their declared literals
        // are flat too, row-major, regardless of dimension count - a 2x2
        // initializer is just the four values in row-major order. The 2D/3D
        // asserts reuse the same dispatcher as the 1D case; these prove that
        // rather than assume it.
        [Theory]
        [MemberData(nameof(FloatArrayTypes))]
        public void RunSuite_AssertArray2dEquals_WithinDelta_Passes(string typeName)
        {
            var declarationText =
                $"VAR\n" +
                $"aExpecteds : ARRAY[0..1,0..1] OF {typeName} := [1.0, 2.0, 3.0, 4.0];\n" +
                $"aActuals : ARRAY[0..1,0..1] OF {typeName} := [1.05, 2.0, 3.0, 4.0];\n" +
                "END_VAR";

            var engine = NewSuiteEngine(
                declarationText,
                "TEST('t');\n" +
                "AssertArray2dEquals_" + typeName + "(Expecteds := aExpecteds, Actuals := aActuals, Delta := 0.1, Message := 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Theory]
        [MemberData(nameof(FloatArrayTypes))]
        public void RunSuite_AssertArray2dEquals_OutsideDelta_ReportsIndexAndValues(string typeName)
        {
            var declarationText =
                $"VAR\n" +
                $"aExpecteds : ARRAY[0..1,0..1] OF {typeName} := [1.0, 2.0, 3.0, 4.0];\n" +
                $"aActuals : ARRAY[0..1,0..1] OF {typeName} := [1.0, 2.0, 3.5, 4.0];\n" +
                "END_VAR";

            var engine = NewSuiteEngine(
                declarationText,
                "TEST('t');\n" +
                "AssertArray2dEquals_" + typeName + "(Expecteds := aExpecteds, Actuals := aActuals, Delta := 0.1, Message := 'mismatch');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            // Flat index 2 in a [0..1,0..1] row-major layout unflattens to
            // [1,0]: a reported index is always in declared coordinates.
            Assert.Contains("EXP: ARRAY[1,0] = 3, ACT: ARRAY[1,0] = 3.5", result.Failures[0].Message);
        }

        [Theory]
        [MemberData(nameof(FloatArrayTypes))]
        public void RunSuite_AssertArray2dEquals_ShapeMismatch_ReportsSizeFailure(string typeName)
        {
            var declarationText =
                $"VAR\n" +
                $"aExpecteds : ARRAY[0..1,0..1] OF {typeName} := [1.0, 2.0, 3.0, 4.0];\n" +
                $"aActuals : ARRAY[0..1,0..2] OF {typeName} := [1.0, 2.0, 3.0, 4.0, 5.0, 6.0];\n" +
                "END_VAR";

            var engine = NewSuiteEngine(
                declarationText,
                "TEST('t');\n" +
                "AssertArray2dEquals_" + typeName + "(Expecteds := aExpecteds, Actuals := aActuals, Delta := 0.1, Message := 'mismatch');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            Assert.Contains("EXP: SIZE = 2x2, ACT: SIZE = 2x3", result.Failures[0].Message);
        }

        [Theory]
        [MemberData(nameof(FloatArrayTypes))]
        public void RunSuite_AssertArray3dEquals_WithinDelta_Passes(string typeName)
        {
            var declarationText =
                $"VAR\n" +
                $"aExpecteds : ARRAY[0..1,0..1,0..1] OF {typeName} := [1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0];\n" +
                $"aActuals : ARRAY[0..1,0..1,0..1] OF {typeName} := [1.05, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0];\n" +
                "END_VAR";

            var engine = NewSuiteEngine(
                declarationText,
                "TEST('t');\n" +
                "AssertArray3dEquals_" + typeName + "(Expecteds := aExpecteds, Actuals := aActuals, Delta := 0.1, Message := 'ok');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Theory]
        [MemberData(nameof(FloatArrayTypes))]
        public void RunSuite_AssertArray3dEquals_OutsideDelta_ReportsIndexAndValues(string typeName)
        {
            var declarationText =
                $"VAR\n" +
                $"aExpecteds : ARRAY[0..1,0..1,0..1] OF {typeName} := [1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0];\n" +
                $"aActuals : ARRAY[0..1,0..1,0..1] OF {typeName} := [1.0, 2.0, 3.0, 4.0, 5.0, 6.5, 7.0, 8.0];\n" +
                "END_VAR";

            var engine = NewSuiteEngine(
                declarationText,
                "TEST('t');\n" +
                "AssertArray3dEquals_" + typeName + "(Expecteds := aExpecteds, Actuals := aActuals, Delta := 0.1, Message := 'mismatch');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            // Flat index 5 in a [0..1,0..1,0..1] row-major layout unflattens
            // to [1,0,1].
            Assert.Contains("EXP: ARRAY[1,0,1] = 6, ACT: ARRAY[1,0,1] = 6.5", result.Failures[0].Message);
        }

        [Theory]
        [MemberData(nameof(FloatArrayTypes))]
        public void RunSuite_AssertArray3dEquals_ShapeMismatch_ReportsSizeFailure(string typeName)
        {
            var declarationText =
                $"VAR\n" +
                $"aExpecteds : ARRAY[0..1,0..1,0..1] OF {typeName} := [1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0];\n" +
                $"aActuals : ARRAY[0..1,0..1,0..2] OF {typeName} := [1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0, 11.0, 12.0];\n" +
                "END_VAR";

            var engine = NewSuiteEngine(
                declarationText,
                "TEST('t');\n" +
                "AssertArray3dEquals_" + typeName + "(Expecteds := aExpecteds, Actuals := aActuals, Delta := 0.1, Message := 'mismatch');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            Assert.Contains("EXP: SIZE = 2x2x2, ACT: SIZE = 2x2x3", result.Failures[0].Message);
        }
    }
}
