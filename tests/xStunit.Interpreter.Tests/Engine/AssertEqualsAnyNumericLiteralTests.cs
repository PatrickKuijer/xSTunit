using System.Collections.Generic;
using xStunit.Parser;
using xStunit.Runner;
using xStunit.Runner.TcUnitStub;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A numeric literal has no width of its own - 8 is an INT against an INT
    // field and a DINT against a DINT one - so AssertEquals(ANY) types it by
    // adopting the other argument's type class, bounded to the literal's own
    // family. Before these tests every non-string literal resolved to no type
    // at all and the assert failed on the type class without ever comparing
    // the values.
    //
    // The self-typed literal kinds below (BOOL and the TIME/DATE family) never
    // adopt: each has one unambiguous class of its own, and letting T#1s take
    // an integer class would turn a genuine mismatch into a pass.
    public class AssertEqualsAnyNumericLiteralTests
    {
        private static TestCaseResult RunAssert(string declaration, string assertion)
        {
            var suite = new PouAst(
                "FB_MySuite",
                "TcUnit.FB_TestSuite",
                "VAR\n" + declaration + "\nEND_VAR",
                "TEST('t');\n" + assertion + "\nTEST_FINISHED();",
                new List<MethodAst>());

            return Assert.Single(new Engine(new TypeRegistry(new[] { suite })).RunSuite("FB_MySuite"));
        }

        [Theory]
        [InlineData("SINT")]
        [InlineData("USINT")]
        [InlineData("BYTE")]
        [InlineData("INT")]
        [InlineData("UINT")]
        [InlineData("WORD")]
        [InlineData("DINT")]
        [InlineData("DWORD")]
        [InlineData("UDINT")]
        [InlineData("LINT")]
        [InlineData("LWORD")]
        [InlineData("ULINT")]
        public void RunSuite_AssertEqualsAny_IntegerLiteralAgainstIntegerDeclaration_PassesInEitherOrder(string typeName)
        {
            Assert.True(RunAssert($"nActual : {typeName} := 8;", "AssertEquals(8, nActual, 'literal first');").Passed);
            Assert.True(RunAssert($"nActual : {typeName} := 8;", "AssertEquals(nActual, 8, 'literal second');").Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsAny_RealLiteralAgainstRealDeclaration_PassesInEitherOrder()
        {
            Assert.True(RunAssert("rActual : REAL := 1.5;", "AssertEquals(1.5, rActual, 'literal first');").Passed);
            Assert.True(RunAssert("rActual : REAL := 1.5;", "AssertEquals(rActual, 1.5, 'literal second');").Passed);
        }

        // A leading minus parses as a UnaryExpr wrapping the literal rather
        // than folding into it, so a negative expected value is typed only if
        // the sign is unwrapped before the literal is classified.
        [Fact]
        public void RunSuite_AssertEqualsAny_NegativeLiteral_IsTypedLikeItsUnsignedForm()
        {
            Assert.True(RunAssert("nActual : INT := -5;", "AssertEquals(-5, nActual, 'literal first');").Passed);
            Assert.True(RunAssert("nActual : INT := -5;", "AssertEquals(nActual, -5, 'literal second');").Passed);
        }

        // DT and TOD are accepted as declaration text but are not type classes
        // of their own, so a DT# literal opposite a var declared DT has to
        // canonicalise to the same class or the new self-typing would turn a
        // working declaration into a mismatch.
        [Theory]
        [InlineData("BOOL", "TRUE")]
        [InlineData("TIME", "T#1s")]
        [InlineData("LTIME", "LTIME#1s")]
        [InlineData("DATE", "D#2024-01-01")]
        [InlineData("DATE_AND_TIME", "DT#2024-01-01-10:00:00")]
        [InlineData("DT", "DT#2024-01-01-10:00:00")]
        [InlineData("TIME_OF_DAY", "TOD#10:00:00")]
        [InlineData("TOD", "TOD#10:00:00")]
        public void RunSuite_AssertEqualsAny_SelfTypedLiteralAgainstItsOwnDeclaration_PassesInEitherOrder(
            string typeName, string literal)
        {
            Assert.True(RunAssert($"xActual : {typeName} := {literal};", $"AssertEquals({literal}, xActual, 'first');").Passed);
            Assert.True(RunAssert($"xActual : {typeName} := {literal};", $"AssertEquals(xActual, {literal}, 'second');").Passed);
        }

        // Both sides self-type, so these reach the runner fully typed instead
        // of as two nulls that no comparison can be made from.
        [Theory]
        [InlineData("AssertEquals(TRUE, TRUE, 'bool');")]
        [InlineData("AssertEquals(T#1s, T#1s, 'time');")]
        public void RunSuite_AssertEqualsAny_TwoSelfTypedLiterals_Passes(string assertion)
        {
            Assert.True(RunAssert("xUnused : BOOL;", assertion).Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsAny_IntVariableAgainstRealLiteral_StillFailsOnTheTypeClass()
        {
            var result = RunAssert("nActual : INT := 8;", "AssertEquals(8.0, nActual, 'a REAL literal is not an INT');");

            Assert.False(result.Passed);
            Assert.Contains("EXP: (Type class = UNKNOWN)", result.Failures[0].Message);
            Assert.Contains("ACT: (Type class = INT)", result.Failures[0].Message);
        }

        // The symmetric half of the case above: an integer literal does not
        // widen into a REAL either. A suite that wants a REAL comparison
        // writes 8.0, and the rule then reads the same in both directions.
        [Fact]
        public void RunSuite_AssertEqualsAny_RealVariableAgainstIntegerLiteral_StillFailsOnTheTypeClass()
        {
            var result = RunAssert("rActual : REAL := 8.0;", "AssertEquals(8, rActual, 'an INT literal is not a REAL');");

            Assert.False(result.Passed);
            Assert.Contains("EXP: (Type class = UNKNOWN)", result.Failures[0].Message);
            Assert.Contains("ACT: (Type class = REAL)", result.Failures[0].Message);
        }

        // An unsuffixed decimal is lexed as a 32-bit float, so it has already
        // lost the mantissa an LREAL comparison would need; adopting LREAL
        // would fail on the VALUE while both sides looked right, which reads
        // as a far more confusing defect than a type-class mismatch. An LREAL
        // comparison needs an LREAL variable on both sides until the literal
        // keeps its full precision.
        [Fact]
        public void RunSuite_AssertEqualsAny_LrealVariableAgainstRealLiteral_StillFailsOnTheTypeClass()
        {
            var result = RunAssert("lrActual : LREAL := 0.1;", "AssertEquals(0.1, lrActual, 'a real literal is a REAL');");

            Assert.False(result.Passed);
            Assert.Contains("EXP: (Type class = UNKNOWN)", result.Failures[0].Message);
            Assert.Contains("ACT: (Type class = LREAL)", result.Failures[0].Message);
        }

        [Fact]
        public void RunSuite_AssertEqualsAny_TimeVariableAgainstIntegerLiteral_StillFailsOnTheTypeClass()
        {
            var result = RunAssert("tActual : TIME := T#1s;", "AssertEquals(1000, tActual, 'TIME is not ANY_INT');");

            Assert.False(result.Passed);
            Assert.Contains("EXP: (Type class = UNKNOWN)", result.Failures[0].Message);
            Assert.Contains("ACT: (Type class = TIME)", result.Failures[0].Message);
        }

        [Fact]
        public void RunSuite_AssertEqualsAny_StringVariableAgainstIntegerLiteral_StillFailsOnTheTypeClass()
        {
            var result = RunAssert("sActual : STRING(32) := '5';", "AssertEquals(5, sActual, 'a number is not a string');");

            Assert.False(result.Passed);
            Assert.Contains("EXP: (Type class = UNKNOWN)", result.Failures[0].Message);
            Assert.Contains("ACT: (Type class = STRING)", result.Failures[0].Message);
        }

        // TwinCAT rejects an out-of-range literal at compile time and this
        // interpreter has no compile step, so the range bound is what stops
        // 70000 from adopting INT and then wrapping to 4464 on the compare -
        // a silent pass on two values that are not equal.
        [Fact]
        public void RunSuite_AssertEqualsAny_OutOfRangeIntegerLiteral_DoesNotAdoptTheNarrowerClass()
        {
            var result = RunAssert("nActual : INT := 4464;", "AssertEquals(70000, nActual, 'out of INT range');");

            Assert.False(result.Passed);
            Assert.Contains("EXP: (Type class = UNKNOWN)", result.Failures[0].Message);
            Assert.Contains("ACT: (Type class = INT)", result.Failures[0].Message);
        }

        // Unchecked, this literal reaches a LINT compare as a negative number
        // and can collide with a value the suite never wrote.
        [Fact]
        public void RunSuite_AssertEqualsAny_UnsignedLiteralBeyondLint_DoesNotAdoptTheSignedClass()
        {
            var result = RunAssert(
                "lActual : LINT := 1;",
                "AssertEquals(18446744073709551615, lActual, 'a ULINT literal is not a LINT');");

            Assert.False(result.Passed);
            Assert.Contains("EXP: (Type class = UNKNOWN)", result.Failures[0].Message);
            Assert.Contains("ACT: (Type class = LINT)", result.Failures[0].Message);
        }

        [Fact]
        public void RunSuite_AssertEqualsAny_UnsignedLiteralBeyondLint_AdoptsUlint()
        {
            Assert.True(RunAssert(
                "ulActual : ULINT := 18446744073709551615;",
                "AssertEquals(18446744073709551615, ulActual, 'ULINT holds it');").Passed);
        }

        // Neither side can be typed from the other, so the assert says which
        // shape it cannot type instead of reaching the runner with two nulls
        // and reporting a missing type ''.
        [Fact]
        public void RunSuite_AssertEqualsAny_TwoNumericLiterals_ReportsThatNeitherSideCanBeTyped()
        {
            var result = RunAssert("xUnused : BOOL;", "AssertEquals(8, 8, 'nothing to infer from');");

            Assert.False(result.Passed);
            var failure = result.Failures[0];
            Assert.Equal(FailureKind.UnsupportedConstruct, failure.Kind);
            Assert.Equal("AssertEquals", failure.Construct);
            Assert.Contains("no width of its own", failure.Message);
        }
    }
}
