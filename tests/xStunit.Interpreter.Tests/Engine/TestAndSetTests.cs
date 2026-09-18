using System;
using System.Collections.Generic;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TestAndSet is a compiled-only Tc2_System primitive: there is no .TcPOU
    // anywhere to parse for it, so any POU under test that guards a critical
    // section with one was unrunnable. It cannot be a native-function plugin
    // either - the plugin contract hands an implementation evaluated argument
    // VALUES, and TestAndSet's whole point is that it writes its operand back -
    // so it lives with MEMSET and the other mutating intrinsics.
    //
    // The semantics pinned here are the atomic ones: the call answers TRUE
    // when the lock was free (it just took it) and FALSE when contended,
    // always leaving the operand TRUE until the holder clears the flag.
    public class TestAndSetTests
    {
        private static Engine NewGuardEngine(string suiteBody)
        {
            var guard = new PouAst(
                "FB_AccessGuard",
                null,
                "VAR\n\tsbBlocked : BOOL;\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    new MethodAst("bTest", "METHOD bTest : BOOL", "bTest := TestAndSet(sbBlocked);"),
                    new MethodAst("Release", "METHOD Release", "sbBlocked := FALSE;"),
                });

            var suite = new PouAst(
                "FB_MySuite",
                "TcUnit.FB_TestSuite",
                "VAR\n\tguard : FB_AccessGuard;\n\tbFirst : BOOL;\n\tbSecond : BOOL;\n\tbThird : BOOL;\nEND_VAR",
                suiteBody,
                new List<MethodAst>());

            return new Engine(new TypeRegistry(new[] { guard, suite }));
        }

        [Fact]
        public void RunSuite_TestAndSetOnAClearFlag_ReturnsTrueAndTakesTheLock()
        {
            var engine = NewGuardEngine(
                "TEST('t');\n" +
                "bFirst := guard.bTest();\n" +
                "AssertTrue(bFirst, 'an uncontended lock is granted');\n" +
                "AssertTrue(guard.sbBlocked, 'and the flag is left set');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_TestAndSetOnAFlagAlreadySet_ReturnsFalseAndLeavesItSet()
        {
            var engine = NewGuardEngine(
                "TEST('t');\n" +
                "bFirst := guard.bTest();\n" +
                "bSecond := guard.bTest();\n" +
                "AssertFalse(bSecond, 'a contended lock is refused');\n" +
                "AssertTrue(guard.sbBlocked, 'and the holder still owns the flag');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void RunSuite_TestAndSetAfterTheFlagIsCleared_GrantsTheLockAgain()
        {
            var engine = NewGuardEngine(
                "TEST('t');\n" +
                "bFirst := guard.bTest();\n" +
                "guard.Release();\n" +
                "bThird := guard.bTest();\n" +
                "AssertTrue(bThird, 'a released lock can be re-acquired');\n" +
                "TEST_FINISHED();");

            Assert.True(Assert.Single(engine.RunSuite("FB_MySuite")).Passed);
        }

        [Fact]
        public void Evaluate_TestAndSetNamedArgument_BindsTheSameOperand()
        {
            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tsbBlocked : BOOL;\n\tbPrior : BOOL;\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    new MethodAst("Take", "METHOD Take", "bPrior := TestAndSet(Lock := sbBlocked);"),
                });

            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Widget");

            engine.CallMethod(instance, "Take", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(true, instance.Fields["bPrior"].Value);
            Assert.Equal(true, instance.Fields["sbBlocked"].Value);
        }

        // TestAndSet is declared over BOOL and nothing else. Converting a
        // numeric operand instead of refusing it would write a bool into a
        // numeric cell and corrupt the variable, turning a call-site mistake
        // into a wrong value several statements away.
        [Fact]
        public void Evaluate_TestAndSetOnANonBooleanOperand_ThrowsAndLeavesItAlone()
        {
            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tnCount : INT := 3;\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    new MethodAst("Take", "METHOD Take", "TestAndSet(nCount);"),
                });

            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Widget");

            var ex = Assert.Throws<InvalidOperationException>(
                () => engine.CallMethod(instance, "Take", new Expr[0], new NamedArg[0], null, null));

            Assert.Contains("TestAndSet", ex.Message);
            Assert.Equal(3, instance.Fields["nCount"].Value);
        }

        // A struct member or GVL flag is as ordinary a lock operand as a local,
        // and the write-back has to reach the same Cell the reader sees.
        [Fact]
        public void Evaluate_TestAndSetOnAStructMember_WritesBackThroughTheField()
        {
            var state = StructDeclParser.Parse("TYPE ST_Lock :\nSTRUCT\nbHeld : BOOL;\nEND_STRUCT\nEND_TYPE");
            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tstLock : ST_Lock;\n\tbPrior : BOOL;\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    new MethodAst("Take", "METHOD Take", "bPrior := TestAndSet(stLock.bHeld);"),
                });

            var engine = new Engine(new TypeRegistry(new[] { fb }, new[] { state }));
            var instance = engine.NewInstance("FB_Widget");

            engine.CallMethod(instance, "Take", new Expr[0], new NamedArg[0], null, null);

            var lockStruct = (StructInstance)instance.Fields["stLock"].Value;
            Assert.Equal(true, instance.Fields["bPrior"].Value);
            Assert.Equal(true, lockStruct.Fields["bHeld"].Value);
        }
    }
}
