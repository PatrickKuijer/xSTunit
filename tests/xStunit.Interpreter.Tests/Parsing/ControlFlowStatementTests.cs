using System;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    public class ControlFlowStatementTests
    {
        private static Engine NewEngine() => new Engine(new TypeRegistry(Array.Empty<PouAst>()));

        private static Frame NewFrame() => new Frame(new FbInstance("Test"), "Test");

        private static int RunAndReadInt(string body, string variable)
        {
            var engine = NewEngine();
            var frame = NewFrame();
            engine.ExecuteStatements(Parser.ParseStatements(body), frame);
            return (int)frame.Locals[variable].Value;
        }

        // --- Parser shape ---------------------------------------------------

        [Fact]
        public void ParseStatements_For_ProducesForStmt()
        {
            var stmts = Parser.ParseStatements("FOR i := 1 TO 5 DO\n\tsum := sum + i;\nEND_FOR");

            var forStmt = Assert.IsType<ForStmt>(Assert.Single(stmts));
            var loopVar = Assert.IsType<IdentifierExpr>(forStmt.Var);
            Assert.Equal("i", loopVar.Name);
            Assert.Null(forStmt.Step);
            Assert.Single(forStmt.Body);
        }

        [Fact]
        public void ParseStatements_ForWithBy_ProducesStepExpr()
        {
            var stmts = Parser.ParseStatements("FOR i := 10 TO 0 BY -2 DO\n\tsum := sum + i;\nEND_FOR");

            var forStmt = Assert.IsType<ForStmt>(Assert.Single(stmts));
            Assert.NotNull(forStmt.Step);
        }

        [Fact]
        public void ParseStatements_ForWithFieldAccessControlVariable_ProducesForStmtWithFieldAccessVar()
        {
            var stmts = Parser.ParseStatements(
                "FOR tPage.nPageIndex := 1 TO 5 DO\n\tsum := sum + 1;\nEND_FOR");

            var forStmt = Assert.IsType<ForStmt>(Assert.Single(stmts));
            var loopVar = Assert.IsType<FieldAccessExpr>(forStmt.Var);
            Assert.Equal("nPageIndex", loopVar.FieldName);
            Assert.Equal("tPage", Assert.IsType<IdentifierExpr>(loopVar.Receiver).Name);
        }

        [Fact]
        public void ParseStatements_While_ProducesWhileStmt()
        {
            var stmts = Parser.ParseStatements("WHILE i < 5 DO\n\ti := i + 1;\nEND_WHILE");

            var whileStmt = Assert.IsType<WhileStmt>(Assert.Single(stmts));
            Assert.IsType<BinaryExpr>(whileStmt.Condition);
            Assert.Single(whileStmt.Body);
        }

        [Fact]
        public void ParseStatements_Repeat_ProducesRepeatStmt()
        {
            var stmts = Parser.ParseStatements("REPEAT\n\ti := i + 1;\nUNTIL i >= 5\nEND_REPEAT");

            var repeatStmt = Assert.IsType<RepeatStmt>(Assert.Single(stmts));
            Assert.Single(repeatStmt.Body);
            Assert.IsType<BinaryExpr>(repeatStmt.Until);
        }

        [Fact]
        public void ParseStatements_CaseWithLabelListRangeAndElse_ProducesCaseStmt()
        {
            var stmts = Parser.ParseStatements(
                "CASE selector OF\n" +
                "1: result := 1;\n" +
                "2, 3: result := 2;\n" +
                "4..6: result := 3;\n" +
                "ELSE\n" +
                "result := -1;\n" +
                "END_CASE");

            var caseStmt = Assert.IsType<CaseStmt>(Assert.Single(stmts));
            Assert.Equal(3, caseStmt.Arms.Count);
            Assert.Single(caseStmt.Arms[0].Labels);
            Assert.Equal(2, caseStmt.Arms[1].Labels.Count);
            Assert.True(caseStmt.Arms[2].Labels[0].IsRange);
            Assert.Single(caseStmt.ElseBody);
        }

        [Fact]
        public void ParseStatements_CaseWithQualifiedEnumLabels_ProducesCaseStmt()
        {
            // EnumType.Member: is an ordinary CASE-label idiom, so a qualified
            // name must still read as an arm boundary - otherwise the label is
            // swallowed into the previous arm's body as a statement.
            var stmts = Parser.ParseStatements(
                "CASE eOpcode OF\n" +
                "eWidgetOpcode.Add:\n" +
                "\tresult := 1;\n" +
                "eWidgetOpcode.Remove:\n" +
                "\tresult := 2;\n" +
                "END_CASE");

            var caseStmt = Assert.IsType<CaseStmt>(Assert.Single(stmts));
            Assert.Equal(2, caseStmt.Arms.Count);
            var firstLabel = Assert.IsType<FieldAccessExpr>(caseStmt.Arms[0].Labels[0].From);
            Assert.Equal("Add", firstLabel.FieldName);
        }

        [Fact]
        public void ParseStatements_ExitInsideWhile_ProducesExitStmt()
        {
            var stmts = Parser.ParseStatements("WHILE TRUE DO\n\tEXIT;\nEND_WHILE");

            var whileStmt = Assert.IsType<WhileStmt>(Assert.Single(stmts));
            Assert.IsType<ExitStmt>(Assert.Single(whileStmt.Body));
        }

        // --- optional trailing semicolon after END_* keywords ----------------

        [Fact]
        public void ParseStatements_EndIfWithTrailingSemicolon_ParsesSameAsWithout()
        {
            var stmts = Parser.ParseStatements("IF a THEN\n\tx := 1;\nEND_IF;");

            Assert.IsType<IfStmt>(Assert.Single(stmts));
        }

        [Fact]
        public void ParseStatements_EndForWithTrailingSemicolon_ParsesSameAsWithout()
        {
            var stmts = Parser.ParseStatements("FOR i := 1 TO 5 DO\n\tsum := sum + i;\nEND_FOR;");

            Assert.IsType<ForStmt>(Assert.Single(stmts));
        }

        [Fact]
        public void ParseStatements_EndWhileWithTrailingSemicolon_ParsesSameAsWithout()
        {
            var stmts = Parser.ParseStatements("WHILE i < 5 DO\n\ti := i + 1;\nEND_WHILE;");

            Assert.IsType<WhileStmt>(Assert.Single(stmts));
        }

        [Fact]
        public void ParseStatements_EndRepeatWithTrailingSemicolon_ParsesSameAsWithout()
        {
            var stmts = Parser.ParseStatements("REPEAT\n\ti := i + 1;\nUNTIL i >= 5\nEND_REPEAT;");

            Assert.IsType<RepeatStmt>(Assert.Single(stmts));
        }

        [Fact]
        public void ParseStatements_EndCaseWithTrailingSemicolon_ParsesSameAsWithout()
        {
            var stmts = Parser.ParseStatements("CASE selector OF\n1: result := 1;\nEND_CASE;");

            Assert.IsType<CaseStmt>(Assert.Single(stmts));
        }

        [Fact]
        public void ParseStatements_TrailingSemicolonFollowedByMoreStatements_ParsesBoth()
        {
            var stmts = Parser.ParseStatements("IF a THEN\n\tx := 1;\nEND_IF;\ny := 2;");

            Assert.Equal(2, stmts.Count);
            Assert.IsType<IfStmt>(stmts[0]);
            Assert.IsType<AssignStmt>(stmts[1]);
        }

        [Fact]
        public void ExecuteNestedForIf_WithTrailingSemicolonsAfterEndIfAndEndFor_ExitsAtExpectedValue()
        {
            // The trailing semicolons are the point: nesting an EXIT-carrying
            // IF inside a FOR when both closers end in ';' is the shape real
            // bodies are written in, and the one the cases above miss.
            var result = RunAndReadInt(
                "result := 0;\n" +
                "FOR i := 1 TO 3 DO\n" +
                "\tIF i = 2 THEN\n" +
                "\t\tresult := i;\n" +
                "\t\tEXIT;\n" +
                "\tEND_IF;\n" +
                "END_FOR;", "result");

            Assert.Equal(2, result);
        }

        // --- FOR execution ---------------------------------------------------

        [Fact]
        public void ExecuteFor_Ascending_SumsInclusiveRange()
        {
            var sum = RunAndReadInt("sum := 0;\nFOR i := 1 TO 5 DO\n\tsum := sum + i;\nEND_FOR", "sum");
            Assert.Equal(15, sum);
        }

        [Fact]
        public void ExecuteFor_WithNegativeStep_CountsDown()
        {
            var count = RunAndReadInt(
                "count := 0;\nFOR i := 10 TO 0 BY -2 DO\n\tcount := count + 1;\nEND_FOR", "count");
            Assert.Equal(6, count);
        }

        [Fact]
        public void ExecuteFor_LoBoundGreaterThanHiBound_RunsZeroIterations()
        {
            var count = RunAndReadInt("count := 0;\nFOR i := 5 TO 1 DO\n\tcount := count + 1;\nEND_FOR", "count");
            Assert.Equal(0, count);
        }

        [Fact]
        public void ExecuteFor_FieldAccessControlVariable_MutatesStructFieldAcrossIterations()
        {
            var stPage = StructDeclParser.Parse(
                "TYPE ST_Page :\nSTRUCT\n\tnPageIndex : INT;\nEND_STRUCT\nEND_TYPE");

            var method = new MethodAst("Run", "METHOD Run", "FOR tPage.nPageIndex := 1 TO 3 DO\nEND_FOR");

            var pou = new PouAst(
                "FB_PageFixture",
                null,
                "VAR\n\ttPage : ST_Page;\nEND_VAR",
                "",
                new System.Collections.Generic.List<MethodAst> { method });

            var engine = new Engine(new TypeRegistry(new[] { pou }, new[] { stPage }));
            var instance = engine.NewInstance("FB_PageFixture");

            engine.CallMethod(instance, "Run", new Expr[0], new NamedArg[0], null, null);

            var tPage = Assert.IsType<StructInstance>(instance.Fields["tPage"].Value);
            Assert.Equal(3, tPage.Fields["nPageIndex"].Value); // the last iterated value, not one past the bound
        }

        [Fact]
        public void ExecuteFor_LoopVariable_RemainsReadableAfterLoop()
        {
            var lastValue = RunAndReadInt("FOR i := 1 TO 3 DO\nEND_FOR\nlastValue := i;", "lastValue");
            Assert.Equal(3, lastValue); // the last iterated value, not one past the bound
        }

        // --- WHILE execution --------------------------------------------------

        [Fact]
        public void ExecuteWhile_ConditionInitiallyFalse_RunsZeroIterations()
        {
            var count = RunAndReadInt("count := 0;\nWHILE count > 0 DO\n\tcount := count + 1;\nEND_WHILE", "count");
            Assert.Equal(0, count);
        }

        [Fact]
        public void ExecuteWhile_MultiIteration_StopsWhenConditionFalse()
        {
            var count = RunAndReadInt("count := 0;\nWHILE count < 4 DO\n\tcount := count + 1;\nEND_WHILE", "count");
            Assert.Equal(4, count);
        }

        // --- REPEAT execution ---------------------------------------------------

        [Fact]
        public void ExecuteRepeat_ConditionInitiallyTrue_RunsAtLeastOnce()
        {
            var count = RunAndReadInt(
                "count := 0;\nREPEAT\n\tcount := count + 1;\nUNTIL count >= 0\nEND_REPEAT", "count");
            Assert.Equal(1, count);
        }

        // --- CASE execution ---------------------------------------------------

        [Theory]
        [InlineData(1, 10)]
        [InlineData(2, 20)]
        [InlineData(3, 20)]
        [InlineData(5, 30)]
        [InlineData(6, 30)]
        [InlineData(99, -1)]
        public void ExecuteCase_DispatchesOnLabelForm(int selector, int expected)
        {
            var body =
                $"selector := {selector};\n" +
                "CASE selector OF\n" +
                "1: result := 10;\n" +
                "2, 3: result := 20;\n" +
                "4..6: result := 30;\n" +
                "ELSE\n" +
                "result := -1;\n" +
                "END_CASE";

            var result = RunAndReadInt(body, "result");
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ExecuteCase_NoMatchNoElse_DoesNothing()
        {
            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["result"] = new Cell { Value = 0 };

            engine.ExecuteStatements(Parser.ParseStatements(
                "selector := 99;\nCASE selector OF\n1: result := 10;\nEND_CASE"), frame);

            Assert.Equal(0, frame.Locals["result"].Value);
        }

        // --- EXIT execution ---------------------------------------------------

        [Fact]
        public void ExecuteExit_UnnestedWhile_StopsLoopImmediately()
        {
            var count = RunAndReadInt(
                "count := 0;\n" +
                "WHILE TRUE DO\n" +
                "\tcount := count + 1;\n" +
                "\tIF count = 3 THEN\n" +
                "\t\tEXIT;\n" +
                "\tEND_IF\n" +
                "END_WHILE", "count");
            Assert.Equal(3, count);
        }

        [Fact]
        public void ExecuteExit_NestedLoop_ExitsOnlyInnermost()
        {
            var body =
                "outerCount := 0;\n" +
                "innerTotal := 0;\n" +
                "FOR o := 1 TO 3 DO\n" +
                "\touterCount := outerCount + 1;\n" +
                "\tFOR i := 1 TO 10 DO\n" +
                "\t\tIF i = 2 THEN\n" +
                "\t\t\tEXIT;\n" +
                "\t\tEND_IF\n" +
                "\t\tinnerTotal := innerTotal + 1;\n" +
                "\tEND_FOR\n" +
                "END_FOR";

            var engine = NewEngine();
            var frame = NewFrame();
            engine.ExecuteStatements(Parser.ParseStatements(body), frame);

            Assert.Equal(3, frame.Locals["outerCount"].Value);
            Assert.Equal(3, frame.Locals["innerTotal"].Value); // one inner iteration per outer pass
        }

        [Fact]
        public void ExecuteExit_ReachedViaThenBranchInsideLoop_ExitsEnclosingLoop()
        {
            var count = RunAndReadInt(
                "count := 0;\n" +
                "FOR i := 1 TO 10 DO\n" +
                "\tcount := count + 1;\n" +
                "\tIF i >= 4 THEN\n" +
                "\t\tEXIT;\n" +
                "\tEND_IF\n" +
                "END_FOR", "count");
            Assert.Equal(4, count);
        }

        // --- IF/ELSIF execution -------------------------------------------------

        private const string ElsifChainBody =
            "branch := 0;\n" +
            "IF a THEN\n" +
            "\tbranch := 1;\n" +
            "ELSIF b THEN\n" +
            "\tbranch := 2;\n" +
            "ELSIF c THEN\n" +
            "\tbranch := 3;\n" +
            "ELSE\n" +
            "\tbranch := 4;\n" +
            "END_IF";

        [Theory]
        [InlineData(true, true, true, 1)]
        [InlineData(false, true, true, 2)]
        [InlineData(false, false, true, 3)]
        [InlineData(false, false, false, 4)]
        public void ExecuteIfElsif_ChainWithElse_RunsOnlyFirstTrueBranch(bool a, bool b, bool c, int expectedBranch)
        {
            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["a"] = new Cell { Value = a };
            frame.Locals["b"] = new Cell { Value = b };
            frame.Locals["c"] = new Cell { Value = c };

            engine.ExecuteStatements(Parser.ParseStatements(ElsifChainBody), frame);

            Assert.Equal(expectedBranch, frame.Locals["branch"].Value);
        }

        [Fact]
        public void ExecuteIfElsif_NoMatchNoTrailingElse_DoesNothing()
        {
            var body =
                "branch := 0;\n" +
                "IF a THEN\n" +
                "\tbranch := 1;\n" +
                "ELSIF b THEN\n" +
                "\tbranch := 2;\n" +
                "ELSIF c THEN\n" +
                "\tbranch := 3;\n" +
                "END_IF";

            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["a"] = new Cell { Value = false };
            frame.Locals["b"] = new Cell { Value = false };
            frame.Locals["c"] = new Cell { Value = false };

            engine.ExecuteStatements(Parser.ParseStatements(body), frame);

            Assert.Equal(0, frame.Locals["branch"].Value);
        }

        [Fact]
        public void ExecuteIfElsif_OnlySelectedBranchSideEffectRuns()
        {
            var body =
                "aRan := 0;\n" +
                "bRan := 0;\n" +
                "cRan := 0;\n" +
                "elseRan := 0;\n" +
                "IF a THEN\n" +
                "\taRan := aRan + 1;\n" +
                "ELSIF b THEN\n" +
                "\tbRan := bRan + 1;\n" +
                "ELSIF c THEN\n" +
                "\tcRan := cRan + 1;\n" +
                "ELSE\n" +
                "\telseRan := elseRan + 1;\n" +
                "END_IF";

            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["a"] = new Cell { Value = false };
            frame.Locals["b"] = new Cell { Value = false };
            frame.Locals["c"] = new Cell { Value = true };

            engine.ExecuteStatements(Parser.ParseStatements(body), frame);

            Assert.Equal(0, frame.Locals["aRan"].Value);
            Assert.Equal(0, frame.Locals["bRan"].Value);
            Assert.Equal(1, frame.Locals["cRan"].Value);
            Assert.Equal(0, frame.Locals["elseRan"].Value);
        }
    }
}
