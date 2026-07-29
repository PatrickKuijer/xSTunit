using System;
using TcXunit.Interpreter;
using TcXunit.Runner;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    public class ParserTests
    {
        // TcXunit-3tx.5: x^ := ... is valid IEC 61131-3 (pointer-dereference
        // assignment) that this v1-subset parser does not implement yet - it
        // must report as unsupported-construct, not plc-fault.
        [Fact]
        public void ParseStatements_DerefAssignmentTarget_ThrowsUnsupportedConstructException()
        {
            var ex = Assert.Throws<UnsupportedConstructException>(
                () => Parser.ParseStatements("pFloor^ := 5.0;"));

            Assert.Equal("x^ :=", ex.Construct);
        }

        // A genuinely malformed assignment target (not an lvalue shape at
        // all) must still be an ordinary FormatException/plc-fault.
        [Fact]
        public void ParseStatements_LiteralAssignmentTarget_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => Parser.ParseStatements("5 := x;"));
        }

        // '**' (EXPT) is a real IEC 61131-3 operator this parser does not
        // implement - unsupported-construct, not a syntax error.
        [Fact]
        public void ParseExpression_Exponentiation_ThrowsUnsupportedConstructException()
        {
            var ex = Assert.Throws<UnsupportedConstructException>(
                () => Parser.ParseExpression("a ** b"));

            Assert.Equal("**", ex.Construct);
        }

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
            Assert.Equal("refCeiling", Assert.IsType<IdentifierExpr>(refAssign.Target).Name);
            Assert.IsType<IdentifierExpr>(refAssign.Value);
        }

        [Fact]
        public void ParseStatements_RefAssignFieldAccessTarget_ProducesRefAssignStmtWithFieldAccessTarget()
        {
            // TcXunit-6t0: REF= must accept the same lvalue shapes as := -
            // a struct/FB member target (stWidget.IpHandler REF= fbHandler),
            // not just a plain identifier.
            var stmts = Parser.ParseStatements("stWidget.IpHandler REF= fbHandler;");

            var refAssign = Assert.IsType<RefAssignStmt>(Assert.Single(stmts));
            var fieldTarget = Assert.IsType<FieldAccessExpr>(refAssign.Target);
            Assert.Equal("IpHandler", fieldTarget.FieldName);
            Assert.Equal("stWidget", Assert.IsType<IdentifierExpr>(fieldTarget.Receiver).Name);
        }

        [Fact]
        public void ParseStatements_RefAssignIndexTarget_ProducesRefAssignStmtWithIndexTarget()
        {
            // TcXunit-6t0: REF= must also accept an array-index target
            // (aRefs[1] REF= x).
            var stmts = Parser.ParseStatements("aRefs[1] REF= x;");

            var refAssign = Assert.IsType<RefAssignStmt>(Assert.Single(stmts));
            var indexTarget = Assert.IsType<IndexExpr>(refAssign.Target);
            Assert.Equal("aRefs", Assert.IsType<IdentifierExpr>(indexTarget.Receiver).Name);
            Assert.Single(indexTarget.Indices);
        }

        [Fact]
        public void ParseExpression_Multiply_ProducesBinaryExprWithMultiplyOp()
        {
            var expr = Parser.ParseExpression("a * b");

            var binary = Assert.IsType<BinaryExpr>(expr);
            Assert.Equal("*", binary.Op);
            Assert.Equal("a", Assert.IsType<IdentifierExpr>(binary.Left).Name);
            Assert.Equal("b", Assert.IsType<IdentifierExpr>(binary.Right).Name);
        }

        [Fact]
        public void ParseExpression_Divide_ProducesBinaryExprWithDivideOp()
        {
            var expr = Parser.ParseExpression("a / b");

            var binary = Assert.IsType<BinaryExpr>(expr);
            Assert.Equal("/", binary.Op);
        }

        [Fact]
        public void ParseExpression_MultiplyBindsTighterThanAdditive_ProducesExpectedTree()
        {
            // a + b * c should parse as a + (b * c)
            var expr = Parser.ParseExpression("a + b * c");

            var add = Assert.IsType<BinaryExpr>(expr);
            Assert.Equal("+", add.Op);
            Assert.Equal("a", Assert.IsType<IdentifierExpr>(add.Left).Name);
            var mul = Assert.IsType<BinaryExpr>(add.Right);
            Assert.Equal("*", mul.Op);
        }

        [Fact]
        public void ParseExpression_MultiplyAndModSamePrecedence_ProcessedLeftToRight()
        {
            // a * b MOD c should parse as (a * b) MOD c
            var expr = Parser.ParseExpression("a * b MOD c");

            var mod = Assert.IsType<BinaryExpr>(expr);
            Assert.Equal("MOD", mod.Op);
            var mul = Assert.IsType<BinaryExpr>(mod.Left);
            Assert.Equal("*", mul.Op);
        }

        [Fact]
        public void ParseExpression_CallMultipliedByField_ParsesWithoutError()
        {
            var expr = Parser.ParseExpression(
                "LWORD_TO_LREAL(FinishedAt - StartedAt) * GVL_TcUnit.HundredNanosecondToSecond");

            var mul = Assert.IsType<BinaryExpr>(expr);
            Assert.Equal("*", mul.Op);
            Assert.IsType<CallExpr>(mul.Left);
            Assert.IsType<FieldAccessExpr>(mul.Right);
        }

        [Fact]
        public void ParseStatements_CallWithOutputArg_ProducesNamedArgWithoutError()
        {
            var stmts = Parser.ParseStatements(
                "AssertResults.ReportResult(AlreadyReported => AlreadyReported);");

            var exprStmt = Assert.IsType<ExprStmt>(Assert.Single(stmts));
            var namedArg = Assert.Single(exprStmt.Call.NamedArgs);
            Assert.Equal("AlreadyReported", namedArg.Name);
            Assert.True(namedArg.IsOutput);
            Assert.Equal("AlreadyReported", Assert.IsType<IdentifierExpr>(namedArg.Value).Name);
        }

        [Fact]
        public void ParseStatements_CallMixingInputAndOutputArgs_ProducesBothNamedArgKinds()
        {
            var stmts = Parser.ParseStatements(
                "AssertResults.ReportResult(TestName := testName, AlreadyReported => AlreadyReported);");

            var exprStmt = Assert.IsType<ExprStmt>(Assert.Single(stmts));
            Assert.Equal(2, exprStmt.Call.NamedArgs.Count);
            Assert.False(exprStmt.Call.NamedArgs[0].IsOutput);
            Assert.True(exprStmt.Call.NamedArgs[1].IsOutput);
        }
    }
}
