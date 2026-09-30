using System.Collections.Generic;
using System.Linq;
using xStunit.Parser;
using xStunit.Runner;
using xStunit.Runner.TcUnitStub;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // S= and R= are the IEC 61131-3 set/reset assignments: the RHS decides
    // WHETHER to write, and the operator decides WHAT - TRUE for S=, FALSE for
    // R=. A FALSE RHS must leave the target exactly as it was.
    public class SetResetAssignmentTests
    {
        private static readonly PouAst Owner = new PouAst(
            "FB_Owner",
            null,
            "FUNCTION_BLOCK FB_Owner\nVAR\n\tbFlag : BOOL;\nEND_VAR",
            "",
            new List<MethodAst>());

        private static readonly StructAst Flags = new StructAst(
            "ST_Flags",
            new[] { new VarDecl("bFlag", "BOOL", null, VarSection.Local) });

        private static IReadOnlyList<TestCaseResult> Run(params string[] testBodies)
        {
            var methods = testBodies
                .Select((body, i) => new MethodAst(
                    "M_Case" + i,
                    "METHOD PRIVATE M_Case" + i,
                    "TEST('M_Case" + i + "');\n" + body + "\nTEST_FINISHED();"))
                .ToList();
            var suite = new PouAst(
                "FB_LatchTests",
                "TcUnit.FB_TestSuite",
                "FUNCTION_BLOCK FB_LatchTests EXTENDS TcUnit.FB_TestSuite\nVAR\n" +
                "\tbLatch : BOOL;\n\tbIn : BOOL;\n\tfbOwner : FB_Owner;\n\tstFlags : ST_Flags;\n" +
                "\taFlags : ARRAY[0..2] OF BOOL;\n\tpFlag : POINTER TO BOOL;\n\tnCount : INT;\nEND_VAR",
                string.Join("\n", methods.Select(m => m.Name + "();")),
                methods);
            var registry = new TypeRegistry(new[] { Owner, suite }, new[] { Flags });
            return new Engine(registry).RunSuite("FB_LatchTests");
        }

        private static void AssertPasses(string testBody)
        {
            var result = Assert.Single(Run(testBody));
            Assert.True(result.Passed, result.ToString());
        }

        [Fact]
        public void ParseStatements_SetAssignment_ProducesSetResetStmt()
        {
            var stmt = Assert.IsType<SetResetAssignStmt>(Assert.Single(Parser.ParseStatements("bLatch S= bIn;")));

            Assert.True(stmt.IsSet);
            Assert.Equal("bLatch", Assert.IsType<IdentifierExpr>(stmt.Target).Name);
            Assert.Equal("bIn", Assert.IsType<IdentifierExpr>(stmt.Value).Name);
        }

        // The operator letter is an identifier as far as the lexer knows, so
        // it must match regardless of case the way every keyword does.
        [Fact]
        public void ParseStatements_LowerCaseResetAssignment_ProducesResetStmt()
        {
            var stmt = Assert.IsType<SetResetAssignStmt>(Assert.Single(Parser.ParseStatements("bLatch r= bIn;")));

            Assert.False(stmt.IsSet);
        }

        // S and R are ordinary identifiers everywhere else: a variable named
        // S compared with = inside an expression must not turn into a set.
        [Fact]
        public void ParseStatements_VariableNamedSInAComparison_StaysAnEqualityTest()
        {
            var assign = Assert.IsType<AssignStmt>(Assert.Single(Parser.ParseStatements("b := S = 1;")));

            Assert.Equal("=", Assert.IsType<BinaryExpr>(assign.Value).Op);
        }

        [Fact]
        public void RunSuite_SetWithTrueRhs_LatchesTheTarget()
        {
            AssertPasses("bIn := TRUE;\nbLatch S= bIn;\nAssertTrue(bLatch, 'S= with TRUE sets');");
        }

        [Fact]
        public void RunSuite_SetWithFalseRhs_LeavesAClearTargetClear()
        {
            AssertPasses("bLatch S= FALSE;\nAssertFalse(bLatch, 'S= with FALSE does not set');");
        }

        [Fact]
        public void RunSuite_SetWithFalseRhs_LeavesALatchedTargetLatched()
        {
            AssertPasses("bLatch := TRUE;\nbLatch S= FALSE;\nAssertTrue(bLatch, 'S= with FALSE does not clear');");
        }

        [Fact]
        public void RunSuite_ResetWithTrueRhs_ClearsTheTarget()
        {
            AssertPasses("bLatch := TRUE;\nbLatch R= TRUE;\nAssertFalse(bLatch, 'R= with TRUE clears');");
        }

        [Fact]
        public void RunSuite_ResetWithFalseRhs_LeavesALatchedTargetLatched()
        {
            AssertPasses("bLatch := TRUE;\nbLatch R= FALSE;\nAssertTrue(bLatch, 'R= with FALSE does not clear');");
        }

        [Fact]
        public void RunSuite_SetAndResetOnAnFbMember_WriteThroughTheMember()
        {
            AssertPasses(
                "fbOwner.bFlag S= TRUE;\nAssertTrue(fbOwner.bFlag, 'set');\n" +
                "fbOwner.bFlag R= TRUE;\nAssertFalse(fbOwner.bFlag, 'reset');");
        }

        [Fact]
        public void RunSuite_SetAndResetOnAStructMember_WriteThroughTheMember()
        {
            AssertPasses(
                "stFlags.bFlag S= TRUE;\nAssertTrue(stFlags.bFlag, 'set');\n" +
                "stFlags.bFlag R= TRUE;\nAssertFalse(stFlags.bFlag, 'reset');");
        }

        [Fact]
        public void RunSuite_SetAndResetOnAnArrayElement_WriteOnlyThatElement()
        {
            AssertPasses(
                "aFlags[1] S= TRUE;\nAssertTrue(aFlags[1], 'set');\nAssertFalse(aFlags[0], 'neighbour untouched');\n" +
                "aFlags[1] R= TRUE;\nAssertFalse(aFlags[1], 'reset');");
        }

        // S=/R= write a BOOL, so a target of any other type is a defect in the
        // code under test - even when the RHS is FALSE and nothing would be
        // written - rather than a silent store of 1 or 0.
        [Theory]
        [InlineData("TRUE")]
        [InlineData("FALSE")]
        public void RunSuite_NonBoolTarget_FailsThatTest(string rhs)
        {
            var result = Assert.Single(Run("nCount S= " + rhs + ";"));

            var failure = Assert.Single(result.Failures);
            Assert.Equal(FailureKind.PlcFault, failure.Kind);
            Assert.Contains("S=", failure.Message);
        }

        [Fact]
        public void RunSuite_NonBoolRhs_FailsThatTestNamingTheOperator()
        {
            var result = Assert.Single(Run("bLatch R= 1;"));

            var failure = Assert.Single(result.Failures);
            Assert.Equal(FailureKind.PlcFault, failure.Kind);
            Assert.Contains("R=", failure.Message);
        }

        // A dereferenced pointer target is the one S=/R= form still beyond
        // the subset. It must surface as unsupported-construct naming the
        // operator, and at run time rather than parse time, so the rest of
        // the suite still runs.
        [Theory]
        [InlineData("S=")]
        [InlineData("R=")]
        public void RunSuite_DerefTarget_FailsOnlyThatTestAsUnsupported(string op)
        {
            var results = Run(
                "pFlag := ADR(bLatch);\npFlag^ " + op + " TRUE;",
                "AssertTrue(TRUE, 'unaffected');");

            Assert.Equal(2, results.Count);
            var failure = Assert.Single(results[0].Failures);
            Assert.Equal(FailureKind.UnsupportedConstruct, failure.Kind);
            Assert.Equal(op, failure.Construct);
            Assert.True(results[1].Passed, results[1].ToString());
        }
    }
}
