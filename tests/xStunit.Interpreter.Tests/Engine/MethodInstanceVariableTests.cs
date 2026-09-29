using System.Collections.Generic;
using System.Linq;
using xStunit.Parser;
using xStunit.Runner.TcUnitStub;
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
        private const string TwoCounterFields = "sfbA : FB_Counter;\n\tsfbB : FB_Counter;";

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

        private static PouAst DerivedCounter(params MethodAst[] methods) => new PouAst(
            "FB_DerivedCounter",
            "FB_Counter",
            "FUNCTION_BLOCK FB_DerivedCounter EXTENDS FB_Counter\nVAR\n\tnBaseResult : INT;\nEND_VAR",
            "",
            methods.ToList());

        private static TestCaseResult RunSingleTest(
            string suiteFields,
            string testBody,
            IEnumerable<PouAst> pous,
            IEnumerable<InterfaceAst> interfaces = null)
        {
            var testCase = new MethodAst(
                "InstVars",
                "METHOD PRIVATE InstVars",
                $"TEST('InstVars');\n{testBody}\nTEST_FINISHED();");

            var suite = new PouAst(
                "FB_CounterTests",
                "TcUnit.FB_TestSuite",
                $"FUNCTION_BLOCK FB_CounterTests EXTENDS TcUnit.FB_TestSuite\nVAR\n\t{suiteFields}\n\tsnResult : INT;\nEND_VAR",
                "InstVars();",
                new List<MethodAst> { testCase });

            var registry = new TypeRegistry(pous.Append(suite).ToList(), interfaceTypes: interfaces?.ToList());
            return Assert.Single(new Engine(registry).RunSuite("FB_CounterTests"));
        }

        private static void AssertSuitePasses(string suiteFields, string testBody, params PouAst[] pous)
        {
            var result = RunSingleTest(suiteFields, testBody, pous);
            Assert.True(result.Passed, string.Join("; ", result.Failures.Select(f => f.Message)));
        }

        [Fact]
        public void RunSuite_VarInstInMethod_KeepsItsValueFromOneCallToTheNext()
        {
            AssertSuitePasses(
                TwoCounterFields,
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
                TwoCounterFields,
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
                TwoCounterFields,
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
                TwoCounterFields,
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
                TwoCounterFields,
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

            AssertSuitePasses(
                "sfbDerived : FB_DerivedCounter;",
                "sfbDerived.Tick();\n" +
                "snResult := sfbDerived.Tick();\n" +
                "AssertEquals_INT( Expected := 1002, Actual := snResult, Message := 'override counts in its own VAR_INST' );\n" +
                "AssertEquals_INT( Expected := 2, Actual := sfbDerived.nBaseResult, Message := 'base counts in its own VAR_INST' );",
                Counter(Tick), DerivedCounter(derivedTick));
        }

        // With no override, the derived instance has exactly one Tick, so a
        // call from outside and an unqualified call from a derived method must
        // count in the same VAR_INST.
        [Fact]
        public void RunSuite_InheritedMethodWithoutOverride_KeepsOneVarInstOnTheDerivedInstance()
        {
            var tickTwice = new MethodAst(
                "TickTwice",
                "METHOD TickTwice",
                "Tick();\nTick();");

            AssertSuitePasses(
                "sfbDerived : FB_DerivedCounter;",
                "sfbDerived.TickTwice();\n" +
                "snResult := sfbDerived.Tick();\n" +
                "AssertEquals_INT( Expected := 3, Actual := snResult, Message := 'inherited Tick keeps one VAR_INST' );",
                Counter(Tick), DerivedCounter(tickTwice));
        }

        // How the receiver is reached is not part of which method runs: an
        // INTERFACE reference and a dereferenced POINTER to the same instance
        // call the same Tick, and so share its VAR_INST with a direct call.
        [Fact]
        public void RunSuite_CallsThroughInterfaceAndPointer_ShareTheDirectCallsVarInst()
        {
            var counterInterface = new InterfaceAst(
                "I_Counter",
                "INTERFACE I_Counter",
                new List<MethodAst> { new MethodAst("Tick", "METHOD Tick : INT", string.Empty) },
                new List<PropertyAst>());

            var result = RunSingleTest(
                "sfbA : FB_Counter;\n\titfCounter : I_Counter;\n\tpCounter : POINTER TO FB_Counter;",
                "itfCounter := sfbA;\npCounter := ADR(sfbA);\n" +
                "sfbA.Tick();\nitfCounter.Tick();\n" +
                "snResult := pCounter^.Tick();\n" +
                "AssertEquals_INT( Expected := 3, Actual := snResult, Message := 'one VAR_INST however the receiver is reached' );",
                new[] { Counter(Tick) },
                new[] { counterInterface });

            Assert.True(result.Passed, string.Join("; ", result.Failures.Select(f => f.Message)));
        }

        // An FB-typed VAR_INST is one FB instance for the life of its owner,
        // so the state that inner FB keeps between its own invocations
        // survives from one call of the method to the next.
        [Fact]
        public void RunSuite_FbTypedVarInst_KeepsTheInnerFbsStateAcrossCalls()
        {
            var accumulator = new PouAst(
                "FB_Accumulator",
                null,
                "FUNCTION_BLOCK FB_Accumulator\nVAR_INPUT\n\tnAdd : INT;\nEND_VAR\nVAR_OUTPUT\n\tnTotal : INT;\nEND_VAR",
                "nTotal := nTotal + nAdd;",
                new List<MethodAst>());

            var add = new MethodAst(
                "Add",
                "METHOD Add : INT\nVAR_INPUT\n\tnValue : INT;\nEND_VAR\nVAR_INST\n\tfbSum : FB_Accumulator;\nEND_VAR",
                "fbSum(nAdd := nValue);\nAdd := fbSum.nTotal;");

            AssertSuitePasses(
                TwoCounterFields,
                "sfbA.Add(nValue := 2);\n" +
                "snResult := sfbA.Add(nValue := 3);\n" +
                "AssertEquals_INT( Expected := 5, Actual := snResult, Message := 'inner FB state carried over between calls' );",
                Counter(add), accumulator);
        }

        // A REF= inside the method rebinds the VAR_INST itself, so the next
        // call must find the reference still bound: the rebinding cannot be
        // confined to the call it happened in.
        [Fact]
        public void RunSuite_ReferenceVarInstRebound_StaysBoundOnTheNextCall()
        {
            var bump = new MethodAst(
                "Bump",
                "METHOD Bump\nVAR_INPUT\n\tbBind : BOOL;\nEND_VAR\nVAR_INST\n\trTarget : REFERENCE TO INT;\nEND_VAR",
                "IF bBind THEN\n\trTarget REF= nField;\nEND_IF\nrTarget := rTarget + 1;");

            AssertSuitePasses(
                TwoCounterFields,
                "sfbA.Bump(bBind := TRUE);\nsfbA.Bump(bBind := FALSE);\n" +
                "AssertEquals_INT( Expected := 2, Actual := sfbA.nField, Message := 'second call wrote through the persisted binding' );",
                Counter(bump));
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
                TwoCounterFields,
                "sfbA.Tick();\nsfbA.TICK();\n" +
                "snResult := sfbA.tick();\n" +
                "AssertEquals_INT( Expected := 3, Actual := snResult, Message := 'one VAR_INST whatever the call spelling' );",
                Counter(mixedCaseBody));
        }

        // VAR_INST has nowhere to live outside a METHOD: a FUNCTION has no
        // instance, and an FB's or PROGRAM's own VAR_INST would just be a VAR.
        // TwinCAT refuses to compile it, so it must fault by name here rather
        // than be dropped and resurface as an unknown variable at each use.
        [Theory]
        [InlineData("FUNCTION_BLOCK FB_Misplaced", "sfbMisplaced : FB_Misplaced;", "sfbMisplaced();")]
        [InlineData("PROGRAM FB_Misplaced", "sfbMisplaced : FB_Misplaced;", "sfbMisplaced();")]
        [InlineData("FUNCTION FB_Misplaced : INT", "snUnused : INT;", "snResult := FB_Misplaced();")]
        public void RunSuite_VarInstOutsideAMethod_FaultsNamingTheRule(string header, string suiteFields, string call)
        {
            var misplaced = new PouAst(
                "FB_Misplaced",
                null,
                $"{header}\nVAR_INST\n\tnCount : INT;\nEND_VAR",
                "nCount := nCount + 1;",
                new List<MethodAst>());

            var result = RunSingleTest(suiteFields, call, new[] { misplaced });

            Assert.False(result.Passed);
            Assert.Contains(
                result.Failures,
                f => f.Message.Contains("'FB_Misplaced'") && f.Message.Contains("VAR_INST is only allowed in a METHOD"));
        }
    }
}
