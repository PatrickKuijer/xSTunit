using System.Collections.Generic;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A bare string literal has no declaration to read a type off, so
    // AssertEquals(ANY) has to type it from the literal itself. Before these
    // tests it resolved to no type at all and every comparison against a
    // STRING(n) variable failed on the type class without ever looking at the
    // characters - in either argument order, so a suite could not assert on a
    // string it did not already hold in a second variable.
    //
    // The capacity is deliberately not part of the type class: a STRING(32) and
    // a STRING(80) are both TypeClass STRING to TwinCAT's ANY, and a literal
    // carries no capacity to match against in the first place.
    public class AssertEqualsAnyStringLiteralTests
    {
        private static Engine NewSuiteEngine(string declarationText, string implementationText)
        {
            var suite = new PouAst("FB_MySuite", "TcUnit.FB_TestSuite", declarationText, implementationText, new List<MethodAst>());
            return new Engine(new TypeRegistry(new[] { suite }));
        }

        [Theory]
        [InlineData("AssertEquals(sActual, 'Ready', 'literal second');")]
        [InlineData("AssertEquals('Ready', sActual, 'literal first');")]
        [InlineData("AssertEquals(Expected := 'Ready', Actual := sActual, Message := 'named');")]
        public void RunSuite_AssertEqualsAny_StringLiteralAgainstSizedStringVariable_Passes(string assertion)
        {
            var engine = NewSuiteEngine(
                "VAR\nsActual : STRING(32) := 'Ready';\nEND_VAR",
                "TEST('t');\n" + assertion + "\nTEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsAny_StringLiteralAgainstDifferingString_FailsOnTheValue()
        {
            var engine = NewSuiteEngine(
                "VAR\nsActual : STRING(32) := 'Busy';\nEND_VAR",
                "TEST('t');\n" +
                "AssertEquals(sActual, 'Ready', 'value mismatch');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            Assert.Contains("EXP: 'Busy'", result.Failures[0].Message);
            Assert.Contains("ACT: 'Ready'", result.Failures[0].Message);
        }

        [Fact]
        public void RunSuite_AssertEqualsAny_StringLiteralAgainstWideStringVariable_Passes()
        {
            var engine = NewSuiteEngine(
                "VAR\nwsActual : WSTRING(32) := \"Ready\";\nEND_VAR",
                "TEST('t');\n" +
                "AssertEquals(wsActual, \"Ready\", 'wide');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsAny_StringsOfDifferentCapacity_ComparesTheCharacters()
        {
            var engine = NewSuiteEngine(
                "VAR\nsExpected : STRING(32) := 'Ready';\nsActual : STRING(80) := 'Ready';\nEND_VAR",
                "TEST('t');\n" +
                "AssertEquals(sExpected, sActual, 'capacity is not the type class');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_AssertEqualsAny_StringLiteralAgainstIntVariable_StillFailsOnTheTypeClass()
        {
            var engine = NewSuiteEngine(
                "VAR\nnActual : INT := 5;\nEND_VAR",
                "TEST('t');\n" +
                "AssertEquals(nActual, '5', 'a literal is typed, not wild');\n" +
                "TEST_FINISHED();");

            var result = Assert.Single(engine.RunSuite("FB_MySuite"));

            Assert.False(result.Passed);
            Assert.Contains("EXP: (Type class = INT)", result.Failures[0].Message);
            Assert.Contains("ACT: (Type class = STRING)", result.Failures[0].Message);
        }

        // A nested struct member reaches AssertEquals through FieldAccessExpr
        // rather than a bare identifier; the literal typing is the whole cause
        // either way, and this pins that the fix covers both reach shapes.
        [Fact]
        public void RunSuite_AssertEqualsAny_StringLiteralAgainstStructMember_Passes()
        {
            var state = StructDeclParser.Parse("TYPE ST_State :\nSTRUCT\nsStateName : STRING(32);\nEND_STRUCT\nEND_TYPE");
            var suite = new PouAst(
                "FB_MySuite",
                "TcUnit.FB_TestSuite",
                "VAR\nstState : ST_State;\nEND_VAR",
                "TEST('t');\n" +
                "stState.sStateName := 'Execute';\n" +
                "AssertEquals(stState.sStateName, 'Execute', 'name must pass through unchanged');\n" +
                "TEST_FINISHED();",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { suite }, new[] { state }));

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }
    }
}
