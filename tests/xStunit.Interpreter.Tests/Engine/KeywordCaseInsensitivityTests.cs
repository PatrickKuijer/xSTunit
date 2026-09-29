using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // IEC 61131-3 keywords are case-insensitive, and TwinCAT compiles 'if x
    // then', 'var_input' and 't#1s' exactly as it compiles their upper-case
    // spellings. Every test here writes a keyword in lower or mixed case, so a
    // parser that only knows the upper-case spelling rejects a body TwinCAT
    // accepts, or - worse for declarations - silently drops the variables a
    // section it failed to recognize was holding.
    public class KeywordCaseInsensitivityTests
    {
        private static object StepOnce(Engine engine, FbInstance instance) =>
            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

        private static FbInstance RunBody(string declaration, string body)
        {
            var fb = new PouAst("FB_Widget", null, declaration, body, new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Widget");
            StepOnce(engine, instance);
            return instance;
        }

        // Each branch writes a different value, so a mis-parsed ELSIF chain
        // that falls through to the wrong arm gives a different answer.
        [Fact]
        public void IfElsifElse_InLowerAndMixedCase_TakesTheMatchingBranch()
        {
            var instance = RunBody(
                "VAR\n\tnIn : INT := 2;\n\tnOut : INT;\nEND_VAR",
                "if nIn = 1 then\n\tnOut := 10;\nElsIf nIn = 2 Then\n\tnOut := 20;\nelse\n\tnOut := 30;\nend_if");

            Assert.Equal(20, instance.Fields["nOut"].Value);
        }

        [Fact]
        public void LoopsAndCase_InLowerCase_RunTheirBodies()
        {
            var instance = RunBody(
                "VAR\n\ti : INT;\n\tnFor : INT;\n\tnWhile : INT;\n\tnRepeat : INT;\n\tnCase : INT;\nEND_VAR",
                "for i := 1 to 10 by 2 do\n\tnFor := nFor + 1;\nend_for;\n"
                + "while nWhile < 3 do\n\tnWhile := nWhile + 1;\nend_while;\n"
                + "repeat\n\tnRepeat := nRepeat + 1;\n\tif nRepeat = 4 then exit; end_if\nuntil nRepeat >= 9\nend_repeat;\n"
                + "case nWhile of\n\t1, 2: nCase := 12;\n\t3: nCase := 3;\nelse\n\tnCase := -1;\nend_case;\n"
                + "return;\nnCase := 99;");

            Assert.Equal(5, instance.Fields["nFor"].Value);
            Assert.Equal(3, instance.Fields["nWhile"].Value);
            Assert.Equal(4, instance.Fields["nRepeat"].Value);
            Assert.Equal(3, instance.Fields["nCase"].Value);
        }

        // The operator word is carried into the evaluator as the BinaryExpr/
        // UnaryExpr op, so a spelling that parses but is not recognized there
        // would fail at evaluation rather than at parse.
        [Fact]
        public void LogicalAndArithmeticKeywordOperators_InLowerCase_Evaluate()
        {
            var instance = RunBody(
                "VAR\n\tbT : BOOL := TRUE;\n\tbF : BOOL;\n\tbAnd : BOOL;\n\tbOr : BOOL;\n\tbXor : BOOL;\n\tbNot : BOOL;\n"
                + "\tbAndThen : BOOL;\n\tbOrElse : BOOL;\n\tnMod : INT;\nEND_VAR",
                "bAnd := bT and bF;\nbOr := bT Or bF;\nbXor := bT xor bT;\nbNot := not bF;\n"
                + "bAndThen := bT and_then bT;\nbOrElse := bF or_else bT;\nnMod := 17 mod 5;");

            Assert.Equal(false, instance.Fields["bAnd"].Value);
            Assert.Equal(true, instance.Fields["bOr"].Value);
            Assert.Equal(false, instance.Fields["bXor"].Value);
            Assert.Equal(true, instance.Fields["bNot"].Value);
            Assert.Equal(true, instance.Fields["bAndThen"].Value);
            Assert.Equal(true, instance.Fields["bOrElse"].Value);
            Assert.Equal(2, instance.Fields["nMod"].Value);
        }

        [Fact]
        public void ThisAndSuper_InLowerCase_ReachTheInstanceAndTheBaseMethod()
        {
            var baseFb = new PouAst(
                "FB_Base",
                null,
                "VAR\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    new MethodAst("M_Value", "METHOD M_Value : INT", "M_Value := 4;"),
                });
            var widget = new PouAst(
                "FB_Widget",
                "FB_Base",
                "FUNCTION_BLOCK FB_Widget EXTENDS FB_Base\nVAR\n\tnOwn : INT;\n\tnInherited : INT;\nEND_VAR",
                "this^.nOwn := 7;\nnInherited := This^.M_Value();",
                new List<MethodAst>
                {
                    new MethodAst("M_Value", "METHOD M_Value : INT", "M_Value := super^.M_Value() + 1;"),
                });

            var engine = new Engine(new TypeRegistry(new[] { baseFb, widget }));
            var instance = engine.NewInstance("FB_Widget");

            StepOnce(engine, instance);

            Assert.Equal(7, instance.Fields["nOwn"].Value);
            Assert.Equal(5, instance.Fields["nInherited"].Value);
        }

        [Fact]
        public void RefAssign_InLowerCase_BindsTheReferenceToItsTarget()
        {
            var instance = RunBody(
                "VAR\n\tnTarget : INT := 3;\n\trefValue : REFERENCE TO INT;\nEND_VAR",
                "refValue ref= nTarget;");

            Assert.Same(instance.Fields["nTarget"], instance.Fields["refValue"]);
        }

        // Expected values are worked out by hand from the literal text, not
        // from the upper-case spelling's parse, so a prefix that is claimed
        // but decoded under the wrong grammar still goes red.
        public static IEnumerable<object[]> TypedLiterals() => new[]
        {
            new object[] { "t#1s500ms", new TimeLiteralExpr(1500) },
            new object[] { "Time#2m", new TimeLiteralExpr(120_000) },
            new object[] { "lt#1ms", new LtimeLiteralExpr(1_000_000) },
            new object[] { "ltime#3us", new LtimeLiteralExpr(3_000) },
            new object[] { "real#1.5", new RealLiteralExpr(1.5f) },
            new object[] { "LReal#2.25", new LrealLiteralExpr(2.25) },
            new object[] { "d#1970-01-02", new DateLiteralExpr(86_400) },
            new object[] { "date#1970-01-03", new DateLiteralExpr(172_800) },
            new object[] { "dt#1970-01-01-00:01:00", new DateAndTimeLiteralExpr(60) },
            new object[] { "date_and_time#1970-01-01-01:00:00", new DateAndTimeLiteralExpr(3_600) },
            new object[] { "tod#00:00:01", new TimeOfDayLiteralExpr(1_000) },
            new object[] { "Time_Of_Day#00:01:00", new TimeOfDayLiteralExpr(60_000) },
        };

        [Theory]
        [MemberData(nameof(TypedLiterals))]
        public void TypedLiteralPrefix_InLowerOrMixedCase_ParsesToTheSameLiteral(string text, Expr expected)
        {
            var parsed = Parser.ParseExpression(text);

            Assert.IsType(expected.GetType(), parsed);
            Assert.Equal(
                expected.GetType().GetProperty("Value").GetValue(expected),
                parsed.GetType().GetProperty("Value").GetValue(parsed));
        }

        // Keywords normalize; identifiers must not. A diagnostic that names a
        // variable has to echo the spelling the source used, or the user
        // searches their project for a name that is not there.
        [Fact]
        public void Identifier_NextToLowerCaseKeywords_KeepsItsWrittenSpelling()
        {
            var stmt = Assert.IsType<IfStmt>(Assert.Single(Parser.ParseStatements("if bReady then nCount := 1; end_if")));

            Assert.Equal("bReady", Assert.IsType<IdentifierExpr>(stmt.Condition).Name);
            var assign = Assert.IsType<AssignStmt>(Assert.Single(stmt.Then));
            Assert.Equal("nCount", Assert.IsType<IdentifierExpr>(assign.Target).Name);
        }

        // A lower-case section header must open its section: unrecognized, it
        // leaves every declaration under it outside any section, and the
        // variables are dropped without a word.
        [Fact]
        public void VarSections_InLowerCase_DeclareTheirVariables()
        {
            var fb = new PouAst(
                "FB_Calc",
                null,
                "function_block FB_Calc\nvar_input\n\tnA : INT;\nend_var\nvar_output\n\tnSum : INT;\nend_var\n"
                + "var_in_out\n\tnAcc : INT;\nend_var\nVar\n\tnLocal : INT := 2;\nEnd_Var\nvar_temp\n\tnScratch : INT;\nend_var",
                "nScratch := nA + nLocal;\nnSum := nScratch;\nnAcc := nAcc + nSum;",
                new List<MethodAst>());
            var host = new PouAst(
                "FB_Host",
                null,
                "VAR\n\tfbCalc : FB_Calc;\n\tnTotal : INT := 10;\n\tnResult : INT;\nEND_VAR",
                "fbCalc(nA := 5, nAcc := nTotal, nSum => nResult);",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { fb, host }));
            var instance = engine.NewInstance("FB_Host");

            StepOnce(engine, instance);

            // The callee's own nAcc is checked, not the caller's nTotal: a bare
            // invocation binds VAR_IN_OUT by value, so nothing flows back.
            Assert.Equal(7, instance.Fields["nResult"].Value);
            var calc = (FbInstance)instance.Fields["fbCalc"].Value;
            Assert.Equal(17, calc.Fields["nAcc"].Value);
        }
    }
}
