using System.Collections.Generic;
using System.Linq;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A METHOD's VAR_INST variables live in the FB instance rather than in the
    // call: initialised once, from their declared initial value, and carried
    // from one call of that method to the next. Each is private to its method
    // and to its instance, so no other instance, no other method and no FB
    // field of the same name may ever observe or disturb it.
    public class MethodInstanceVariableTests
    {
        private static readonly MethodAst Tick = new MethodAst(
            "Tick",
            "METHOD Tick : INT\nVAR_INST\n\tnCount : INT;\nEND_VAR",
            "nCount := nCount + 1;\nTick := nCount;");

        private static PouAst Counter(params MethodAst[] methods) => new PouAst(
            "FB_Counter",
            null,
            "FUNCTION_BLOCK FB_Counter\nVAR\n\tnField : INT;\nEND_VAR",
            "",
            methods.ToList());

        private static void AssertSuitePasses(string testBody, params PouAst[] pous)
        {
            var testCase = new MethodAst(
                "InstVars",
                "METHOD PRIVATE InstVars",
                $"TEST('InstVars');\n{testBody}\nTEST_FINISHED();");

            var suite = new PouAst(
                "FB_CounterTests",
                "TcUnit.FB_TestSuite",
                "FUNCTION_BLOCK FB_CounterTests EXTENDS TcUnit.FB_TestSuite\nVAR\n\tsfbA : FB_Counter;\n\tsfbB : FB_Counter;\n\tsnResult : INT;\nEND_VAR",
                "InstVars();",
                new List<MethodAst> { testCase });

            var results = new Engine(new TypeRegistry(pous.Append(suite).ToList())).RunSuite("FB_CounterTests");

            var result = Assert.Single(results);
            Assert.True(result.Passed, string.Join("; ", result.Failures.Select(f => f.Message)));
        }

        [Fact]
        public void RunSuite_VarInstInMethod_KeepsItsValueFromOneCallToTheNext()
        {
            AssertSuitePasses(
                "sfbA.Tick();\nsfbA.Tick();\nsnResult := sfbA.Tick();\n" +
                "AssertEquals_INT( Expected := 3, Actual := snResult, Message := 'VAR_INST carried over between calls' );",
                Counter(Tick));
        }

        // The declared initial value is applied once, not on every call: a
        // re-initialising implementation returns 11 from every call.
        [Fact]
        public void RunSuite_VarInstWithInitialValue_StartsFromItOnceAndThenCarriesOver()
        {
            var tickFromTen = new MethodAst(
                "Tick",
                "METHOD Tick : INT\nVAR_INST\n\tnCount : INT := 10;\nEND_VAR",
                "nCount := nCount + 1;\nTick := nCount;");

            AssertSuitePasses(
                "snResult := sfbA.Tick();\n" +
                "AssertEquals_INT( Expected := 11, Actual := snResult, Message := 'first call starts from the initial value' );\n" +
                "snResult := sfbA.Tick();\n" +
                "AssertEquals_INT( Expected := 12, Actual := snResult, Message := 'second call carries over' );",
                Counter(tickFromTen));
        }

        [Fact]
        public void RunSuite_TwoInstancesOfOneFb_EachKeepTheirOwnVarInst()
        {
            AssertSuitePasses(
                "sfbA.Tick();\nsfbA.Tick();\n" +
                "snResult := sfbB.Tick();\n" +
                "AssertEquals_INT( Expected := 1, Actual := snResult, Message := 'sfbB unaffected by sfbA' );\n" +
                "snResult := sfbA.Tick();\n" +
                "AssertEquals_INT( Expected := 3, Actual := snResult, Message := 'sfbA unaffected by sfbB' );",
                Counter(Tick));
        }

        [Fact]
        public void RunSuite_SameNamedVarInstInTwoMethods_AreSeparateVariables()
        {
            var tock = new MethodAst(
                "Tock",
                "METHOD Tock : INT\nVAR_INST\n\tnCount : INT := 100;\nEND_VAR",
                "nCount := nCount + 1;\nTock := nCount;");

            AssertSuitePasses(
                "sfbA.Tick();\nsfbA.Tock();\n" +
                "snResult := sfbA.Tick();\n" +
                "AssertEquals_INT( Expected := 2, Actual := snResult, Message := 'Tick counts only its own calls' );\n" +
                "snResult := sfbA.Tock();\n" +
                "AssertEquals_INT( Expected := 102, Actual := snResult, Message := 'Tock counts only its own calls' );",
                Counter(Tick, tock));
        }

        // Inside the method the VAR_INST shadows a same-named FB field, and
        // the field itself is never written: dot-access from outside still
        // reads the field.
        [Fact]
        public void RunSuite_VarInstNamedLikeAnFbField_LeavesTheFieldAlone()
        {
            var bumpField = new MethodAst(
                "Tick",
                "METHOD Tick : INT\nVAR_INST\n\tnField : INT;\nEND_VAR",
                "nField := nField + 1;\nTick := nField;");

            AssertSuitePasses(
                "sfbA.nField := 50;\n" +
                "sfbA.Tick();\n" +
                "snResult := sfbA.Tick();\n" +
                "AssertEquals_INT( Expected := 2, Actual := snResult, Message := 'VAR_INST counted independently of the field' );\n" +
                "AssertEquals_INT( Expected := 50, Actual := sfbA.nField, Message := 'FB field untouched' );",
                Counter(bumpField));
        }

        // An override and the base method it overrides are two methods, so
        // SUPER^.Tick() runs against the base's own VAR_INST rather than the
        // override's same-named one.
        [Fact]
        public void RunSuite_OverrideCallingSuper_EachKeepsItsOwnVarInst()
        {
            var derivedTick = new MethodAst(
                "Tick",
                "METHOD Tick : INT\nVAR_INST\n\tnCount : INT := 1000;\nEND_VAR",
                "nCount := nCount + 1;\nnBaseResult := SUPER^.Tick();\nTick := nCount;");

            var derived = new PouAst(
                "FB_DerivedCounter",
                "FB_Counter",
                "FUNCTION_BLOCK FB_DerivedCounter EXTENDS FB_Counter\nVAR\n\tnBaseResult : INT;\nEND_VAR",
                "",
                new List<MethodAst> { derivedTick });

            var testCase = new MethodAst(
                "InstVars",
                "METHOD PRIVATE InstVars",
                "TEST('InstVars');\n" +
                "sfbDerived.Tick();\n" +
                "snResult := sfbDerived.Tick();\n" +
                "AssertEquals_INT( Expected := 1002, Actual := snResult, Message := 'override counts in its own VAR_INST' );\n" +
                "AssertEquals_INT( Expected := 2, Actual := sfbDerived.nBaseResult, Message := 'base counts in its own VAR_INST' );\n" +
                "TEST_FINISHED();");

            var suite = new PouAst(
                "FB_CounterTests",
                "TcUnit.FB_TestSuite",
                "FUNCTION_BLOCK FB_CounterTests EXTENDS TcUnit.FB_TestSuite\nVAR\n\tsfbDerived : FB_DerivedCounter;\n\tsnResult : INT;\nEND_VAR",
                "InstVars();",
                new List<MethodAst> { testCase });

            var results = new Engine(new TypeRegistry(new[] { Counter(Tick), derived, suite })).RunSuite("FB_CounterTests");

            var result = Assert.Single(results);
            Assert.True(result.Passed, string.Join("; ", result.Failures.Select(f => f.Message)));
        }

        // IEC identifiers are case-insensitive, so a call spelled differently
        // from the declaration is the same method and must reach the same
        // VAR_INST, however the body spells the variable.
        [Fact]
        public void RunSuite_MethodCalledInAnotherCase_SharesOneVarInst()
        {
            var mixedCaseBody = new MethodAst(
                "Tick",
                "METHOD Tick : INT\nVAR_INST\n\tnCount : INT;\nEND_VAR",
                "NCOUNT := ncount + 1;\nTick := nCount;");

            AssertSuitePasses(
                "sfbA.Tick();\nsfbA.TICK();\n" +
                "snResult := sfbA.tick();\n" +
                "AssertEquals_INT( Expected := 3, Actual := snResult, Message := 'one VAR_INST whatever the call spelling' );",
                Counter(mixedCaseBody));
        }
    }
}
