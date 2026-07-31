using System.Collections.Generic;
using System.Linq;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcXunit-mym.2: RETURN as a statement - parses, and unwinds out of
    // arbitrarily nested IF branches back to the nearest method/suite body
    // boundary (CallMethod's or RunSuite's top-level ExecuteStatements call)
    // without disturbing the method's existing return-value convention.
    public class ReturnStatementTests
    {
        // --- Parser shape ---------------------------------------------------

        [Fact]
        public void ParseStatements_ReturnInsideIf_ProducesIfStmtWithReturnStmtThen()
        {
            var stmts = Parser.ParseStatements("IF x THEN\n\tRETURN;\nEND_IF");

            var ifStmt = Assert.IsType<IfStmt>(Assert.Single(stmts));
            Assert.IsType<ReturnStmt>(Assert.Single(ifStmt.Then));
        }

        // --- Method execution -------------------------------------------------

        private static Engine NewEngine(MethodAst method)
        {
            var pou = new PouAst(
                "FB_ReturnFixture",
                null,
                "VAR\n\tguard : BOOL;\n\tflag : BOOL;\nEND_VAR",
                "",
                new List<MethodAst> { method });

            return new Engine(new TypeRegistry(new[] { pou }));
        }

        private static bool CallGuardMethod(string body, bool guard)
        {
            var method = new MethodAst(
                "DoWork",
                "METHOD DoWork\nVAR_INPUT\n\tguard : BOOL;\nEND_VAR",
                body);
            var engine = NewEngine(method);
            var instance = engine.NewInstance("FB_ReturnFixture");

            engine.CallMethod(
                instance,
                "DoWork",
                new Expr[] { new BoolLiteralExpr(guard) },
                new NamedArg[0],
                null,
                null);

            return (bool)instance.Fields["flag"].Value;
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void CallMethod_GuardClauseReturn_SkipsOrRunsTrailingStatement(bool guard, bool expectedFlag)
        {
            var flag = CallGuardMethod(
                "IF guard THEN\n\tRETURN;\nEND_IF\nflag := TRUE;", guard);

            Assert.Equal(expectedFlag, flag);
        }

        [Fact]
        public void CallMethod_ReturnInNestedIf_StopsWholeMethodBody()
        {
            var flag = CallGuardMethod(
                "IF guard THEN\n" +
                "\tIF TRUE THEN\n" +
                "\t\tRETURN;\n" +
                "\tEND_IF\n" +
                "END_IF\n" +
                "flag := TRUE;", true);

            Assert.False(flag);
        }

        [Fact]
        public void CallMethod_ReturnAsLastStatement_IsNoOp()
        {
            var flag = CallGuardMethod(
                "flag := TRUE;\nIF guard THEN\n\tRETURN;\nEND_IF", true);

            Assert.True(flag);
        }

        [Fact]
        public void CallMethod_NeverHitsReturn_BehavesAsBeforeChange()
        {
            var flag = CallGuardMethod(
                "IF guard THEN\n\tRETURN;\nEND_IF\nflag := TRUE;", false);

            Assert.True(flag);
        }

        [Fact]
        public void CallMethod_ReturnEarly_StillReportsReturnValueSetBeforeReturn()
        {
            var method = new MethodAst(
                "ComputeValue",
                "METHOD ComputeValue : INT\nVAR_INPUT\n\tguard : BOOL;\nEND_VAR",
                "ComputeValue := 42;\n" +
                "IF guard THEN\n" +
                "\tRETURN;\n" +
                "END_IF\n" +
                "ComputeValue := 99;");

            var pou = new PouAst(
                "FB_ReturnValueFixture",
                null,
                "",
                "",
                new List<MethodAst> { method });
            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_ReturnValueFixture");

            var result = engine.CallMethod(
                instance, "ComputeValue", new Expr[] { new BoolLiteralExpr(true) }, new NamedArg[0], null, null);

            Assert.Equal(42, result);
        }

        // --- Suite top-level body execution ------------------------------------

        private static Engine NewSuiteEngine(string suiteBody)
        {
            var runOne = new MethodAst(
                "RunOne",
                "METHOD PRIVATE RunOne",
                "TEST('RunOne');\n" +
                "AssertTrue(Condition := TRUE, Message := 'ok');\n" +
                "TEST_FINISHED();");

            var suite = new PouAst(
                "FB_ReturnGuardSuite",
                "TcUnit.FB_TestSuite",
                "",
                suiteBody,
                new List<MethodAst> { runOne });

            return new Engine(new TypeRegistry(new[] { suite }));
        }

        [Fact]
        public void RunSuite_TopLevelReturnGuardTrue_SkipsRestOfSuiteBody()
        {
            var engine = NewSuiteEngine("IF TRUE THEN\n\tRETURN;\nEND_IF\nRunOne();");

            var results = engine.RunSuite("FB_ReturnGuardSuite");

            Assert.Empty(results);
        }

        [Fact]
        public void RunSuite_TopLevelReturnGuardFalse_RunsRestOfSuiteBody()
        {
            var engine = NewSuiteEngine("IF FALSE THEN\n\tRETURN;\nEND_IF\nRunOne();");

            var results = engine.RunSuite("FB_ReturnGuardSuite");

            Assert.Single(results);
            Assert.True(results.Single().Passed);
        }
    }
}
