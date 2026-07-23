using TcXunit.Interpreter;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    public class ParserTests
    {
        [Fact]
        public void ParseStatements_SimpleAssignment_ProducesAssignStmtWithBinaryExpr()
        {
            var stmts = Parser.ParseStatements("value := value + delta;");

            var assign = Assert.IsType<AssignStmt>(Assert.Single(stmts));
            Assert.Equal("value", Assert.IsType<IdentifierExpr>(assign.Target).Name);
            var binary = Assert.IsType<BinaryExpr>(assign.Value);
            Assert.Equal("+", binary.Op);
        }

        [Fact]
        public void ParseStatements_IfElse_ProducesIfStmtWithBothBranches()
        {
            var stmts = Parser.ParseStatements(
                "IF value - delta < pFloor^ THEN\n" +
                "\tvalue := pFloor^;\n" +
                "ELSE\n" +
                "\tvalue := value - delta;\n" +
                "END_IF");

            var ifStmt = Assert.IsType<IfStmt>(Assert.Single(stmts));
            Assert.IsType<BinaryExpr>(ifStmt.Condition);
            Assert.Single(ifStmt.Then);
            Assert.Single(ifStmt.Else);
        }

        [Fact]
        public void ParseStatements_IfElsif_ProducesNestedIfStmtChain()
        {
            var stmts = Parser.ParseStatements(
                "IF a THEN\n" +
                "\tx := 1;\n" +
                "ELSIF b THEN\n" +
                "\tx := 2;\n" +
                "ELSIF c THEN\n" +
                "\tx := 3;\n" +
                "ELSE\n" +
                "\tx := 4;\n" +
                "END_IF");

            var outer = Assert.IsType<IfStmt>(Assert.Single(stmts));
            Assert.Equal("a", Assert.IsType<IdentifierExpr>(outer.Condition).Name);
            Assert.Single(outer.Then);

            var elsif1 = Assert.IsType<IfStmt>(Assert.Single(outer.Else));
            Assert.Equal("b", Assert.IsType<IdentifierExpr>(elsif1.Condition).Name);
            Assert.Single(elsif1.Then);

            var elsif2 = Assert.IsType<IfStmt>(Assert.Single(elsif1.Else));
            Assert.Equal("c", Assert.IsType<IdentifierExpr>(elsif2.Condition).Name);
            Assert.Single(elsif2.Then);
            Assert.Single(elsif2.Else);
        }

        [Fact]
        public void ParseStatements_SuperInitCall_ProducesExprStmtCallingFbInit()
        {
            var stmts = Parser.ParseStatements(
                "SUPER^(bInitRetains := bInitRetains, bInCopyCode := bInCopyCode, startValue := startValue);");

            var exprStmt = Assert.IsType<ExprStmt>(Assert.Single(stmts));
            Assert.IsType<SuperRefExpr>(exprStmt.Call.Receiver);
            Assert.Equal("FB_init", exprStmt.Call.MethodName);
            Assert.Equal(3, exprStmt.Call.NamedArgs.Count);
        }

        [Fact]
        public void ParseStatements_ThisAndSuperMethodCalls_ProduceQualifiedCallExprs()
        {
            var stmts = Parser.ParseStatements(
                "candidate := THIS^.GetValue() + delta;\n" +
                "SUPER^.Increment(delta := candidate - THIS^.GetValue());");

            Assert.Equal(2, stmts.Count);

            var assign = Assert.IsType<AssignStmt>(stmts[0]);
            var binary = Assert.IsType<BinaryExpr>(assign.Value);
            var thisCall = Assert.IsType<CallExpr>(binary.Left);
            Assert.IsType<ThisRefExpr>(thisCall.Receiver);
            Assert.Equal("GetValue", thisCall.MethodName);

            var superCallStmt = Assert.IsType<ExprStmt>(stmts[1]);
            Assert.IsType<SuperRefExpr>(superCallStmt.Call.Receiver);
            Assert.Equal("Increment", superCallStmt.Call.MethodName);
        }

        [Fact]
        public void ParseStatements_AdrPointerDeref_ProducesCallAndDerefExprs()
        {
            var stmts = Parser.ParseStatements(
                "pFloor := ADR(floor);\n" +
                "refCeiling REF= ceiling;");

            var adrAssign = Assert.IsType<AssignStmt>(stmts[0]);
            var adrCall = Assert.IsType<CallExpr>(adrAssign.Value);
            Assert.Null(adrCall.Receiver);
            Assert.Equal("ADR", adrCall.MethodName);

            var refAssign = Assert.IsType<RefAssignStmt>(stmts[1]);
            Assert.Equal("refCeiling", refAssign.TargetName);
            Assert.IsType<IdentifierExpr>(refAssign.Value);
        }
    }
}
