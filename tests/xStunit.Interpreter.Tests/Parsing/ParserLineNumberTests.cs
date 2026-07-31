using xStunit.Interpreter;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // Line stamping on AST nodes (TcXunit-p3t.2). Stmt.Line/Expr.Line are
    // 1-based within the ST body text, so line 1 is the body's first line.
    public class ParserLineNumberTests
    {
        [Fact]
        public void ParseStatements_TopLevelStatements_CarryTheirOwnLines()
        {
            var stmts = Parser.ParseStatements(
                "a := 1;\n" +
                "b := 2;\n" +
                "\n" +
                "c := 3;");

            Assert.Equal(1, stmts[0].Line);
            Assert.Equal(2, stmts[1].Line);
            Assert.Equal(4, stmts[2].Line);
        }

        [Fact]
        public void ParseStatements_AssignmentOperands_CarryTheStatementsLine()
        {
            var stmts = Parser.ParseStatements("\nvalue := value + delta;");

            var assign = Assert.IsType<AssignStmt>(Assert.Single(stmts));
            Assert.Equal(2, assign.Line);
            Assert.Equal(2, assign.Target.Line);
            var binary = Assert.IsType<BinaryExpr>(assign.Value);
            Assert.Equal(2, binary.Line);
            Assert.Equal(2, binary.Left.Line);
            Assert.Equal(2, binary.Right.Line);
        }

        [Fact]
        public void ParseStatements_IfBody_NestedExpressionsCarryTheirOwnLine()
        {
            var stmts = Parser.ParseStatements(
                "x := 1;\n" +
                "IF x > 0 THEN\n" +
                "\ty := x + 2;\n" +
                "END_IF");

            Assert.Equal(1, stmts[0].Line);

            var ifStmt = Assert.IsType<IfStmt>(stmts[1]);
            Assert.Equal(2, ifStmt.Line);
            var condition = Assert.IsType<BinaryExpr>(ifStmt.Condition);
            Assert.Equal(2, condition.Line);
            Assert.Equal(2, condition.Left.Line);

            var inner = Assert.IsType<AssignStmt>(Assert.Single(ifStmt.Then));
            Assert.Equal(3, inner.Line);
            var sum = Assert.IsType<BinaryExpr>(inner.Value);
            Assert.Equal(3, sum.Line);
            Assert.Equal(3, Assert.IsType<IdentifierExpr>(sum.Left).Line);
            Assert.Equal(3, Assert.IsType<IntLiteralExpr>(sum.Right).Line);
        }

        [Fact]
        public void ParseStatements_ElsifChain_NestedIfStmtCarriesElsifLine()
        {
            var stmts = Parser.ParseStatements(
                "IF a THEN\n" +
                "\tx := 1;\n" +
                "ELSIF b THEN\n" +
                "\tx := 2;\n" +
                "ELSE\n" +
                "\tx := 3;\n" +
                "END_IF");

            var outer = Assert.IsType<IfStmt>(Assert.Single(stmts));
            Assert.Equal(1, outer.Line);
            Assert.Equal(2, Assert.Single(outer.Then).Line);

            var elsif = Assert.IsType<IfStmt>(Assert.Single(outer.Else));
            Assert.Equal(3, elsif.Line);
            Assert.Equal(4, Assert.Single(elsif.Then).Line);
            Assert.Equal(6, Assert.Single(elsif.Else).Line);
        }

        [Fact]
        public void ParseStatements_CallStatement_CarriesCallLine()
        {
            var stmts = Parser.ParseStatements(
                "a := 1;\n" +
                "AssertEquals(Expected := 1, Actual := a);\n");

            var exprStmt = Assert.IsType<ExprStmt>(stmts[1]);
            Assert.Equal(2, exprStmt.Line);
            Assert.Equal(2, exprStmt.Call.Line);
        }

        [Fact]
        public void ParseStatements_QualifiedCall_CarriesReceiverLine()
        {
            var stmts = Parser.ParseStatements(
                "\n" +
                "\n" +
                "THIS^.AssertTrue(cond);");

            var exprStmt = Assert.IsType<ExprStmt>(Assert.Single(stmts));
            Assert.Equal(3, exprStmt.Line);
            Assert.Equal(3, exprStmt.Call.Line);
            Assert.Equal(3, exprStmt.Call.Receiver.Line);
        }

        [Fact]
        public void ParseStatements_StatementSpanningLines_UsesLineOfFirstToken()
        {
            var stmts = Parser.ParseStatements(
                "value :=\n" +
                "\tone +\n" +
                "\ttwo;");

            var assign = Assert.IsType<AssignStmt>(Assert.Single(stmts));
            Assert.Equal(1, assign.Line);
            var binary = Assert.IsType<BinaryExpr>(assign.Value);
            Assert.Equal(2, binary.Line);
            Assert.Equal(3, binary.Right.Line);
        }

        [Fact]
        public void ParseStatements_AfterMultiLineComment_StatementCarriesRealLine()
        {
            var stmts = Parser.ParseStatements(
                "(* header comment\n" +
                "   spanning three\n" +
                "   lines *)\n" +
                "value := 1;");

            Assert.Equal(4, Assert.Single(stmts).Line);
        }

        [Fact]
        public void ParseStatements_LoopsAndCase_CarryTheirKeywordLine()
        {
            var stmts = Parser.ParseStatements(
                "FOR i := 1 TO 3 DO\n" +
                "\tx := i;\n" +
                "END_FOR\n" +
                "WHILE x < 9 DO\n" +
                "\tx := x + 1;\n" +
                "END_WHILE\n" +
                "REPEAT\n" +
                "\tx := x - 1;\n" +
                "UNTIL x = 0\n" +
                "END_REPEAT\n" +
                "CASE x OF\n" +
                "\t1: y := 1;\n" +
                "END_CASE\n");

            Assert.Equal(1, Assert.IsType<ForStmt>(stmts[0]).Line);
            Assert.Equal(2, Assert.Single(((ForStmt)stmts[0]).Body).Line);
            Assert.Equal(4, Assert.IsType<WhileStmt>(stmts[1]).Line);
            Assert.Equal(7, Assert.IsType<RepeatStmt>(stmts[2]).Line);
            Assert.Equal(11, Assert.IsType<CaseStmt>(stmts[3]).Line);
            Assert.Equal(12, Assert.Single(((CaseStmt)stmts[3]).Arms[0].Body).Line);
        }

        [Fact]
        public void ParseStatements_ExitAndReturn_CarryTheirKeywordLine()
        {
            var stmts = Parser.ParseStatements(
                "WHILE TRUE DO\n" +
                "\tEXIT;\n" +
                "END_WHILE\n" +
                "RETURN;");

            var loop = Assert.IsType<WhileStmt>(stmts[0]);
            Assert.Equal(2, Assert.IsType<ExitStmt>(Assert.Single(loop.Body)).Line);
            Assert.Equal(4, Assert.IsType<ReturnStmt>(stmts[1]).Line);
        }

        [Fact]
        public void ParseExpression_StandaloneExpression_StartsAtLineOne()
        {
            var expr = Parser.ParseExpression("a + b");

            Assert.Equal(1, expr.Line);
        }

        [Fact]
        public void ParseStatements_HandBuiltNodes_DefaultToUnknownLineZero()
        {
            // Line 0 is the "unknown" sentinel so the hundreds of hand-built
            // AST nodes in the test suite stay valid without a line argument.
            Assert.Equal(0, new IntLiteralExpr(1).Line);
            Assert.Equal(0, new ReturnStmt().Line);
        }
    }
}
