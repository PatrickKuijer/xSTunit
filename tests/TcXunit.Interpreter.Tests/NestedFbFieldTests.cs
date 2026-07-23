using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-0v1: VAR_INPUT/VAR_OUTPUT of a nested (non-native, non-suite) FB
    // instance must be materialized into FbInstance.Fields at NewInstance()
    // time, so dot-access and bare invocation of the FB both see the same
    // persisted cells.
    public class NestedFbFieldTests
    {
        private static Engine NewEngine(string outerImplementation)
        {
            var adder = new PouAst(
                "FB_Adder",
                null,
                "VAR_INPUT\n\tA : INT;\n\tB : INT;\nEND_VAR\nVAR_OUTPUT\n\tSum : INT;\nEND_VAR",
                "Sum := A + B;",
                new List<MethodAst>());

            var outer = new PouAst(
                "FB_Outer",
                null,
                "VAR\n\tsfbAdder : FB_Adder;\nEND_VAR",
                outerImplementation,
                new List<MethodAst>());

            return new Engine(new TypeRegistry(new[] { adder, outer }));
        }

        [Fact]
        public void NewInstance_MaterializesNestedFbVarInputAndVarOutputFields()
        {
            var engine = NewEngine("");
            var instance = engine.NewInstance("FB_Outer");

            var nested = Assert.IsType<FbInstance>(instance.Fields["sfbAdder"].Value);
            Assert.True(nested.Fields.ContainsKey("A"));
            Assert.True(nested.Fields.ContainsKey("B"));
            Assert.True(nested.Fields.ContainsKey("Sum"));
        }

        [Fact]
        public void DotAccess_WritesNestedFbVarInputsBeforeAnyInvocation()
        {
            var engine = NewEngine("sfbAdder.A := 5;\nsfbAdder.B := 3;\nsfbAdder();");
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            var nested = (FbInstance)instance.Fields["sfbAdder"].Value;
            Assert.Equal(8, nested.Fields["Sum"].Value);
        }

        [Fact]
        public void BareInvocation_BindsPositionalArgsIntoPersistedFields()
        {
            var engine = NewEngine("sfbAdder(10, 20);");
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            var nested = (FbInstance)instance.Fields["sfbAdder"].Value;
            Assert.Equal(10, nested.Fields["A"].Value);
            Assert.Equal(20, nested.Fields["B"].Value);
            Assert.Equal(30, nested.Fields["Sum"].Value);
        }

        [Fact]
        public void BareInvocation_BindsNamedArgsIntoPersistedFields()
        {
            var engine = NewEngine("sfbAdder(B := 2, A := 7);");
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            var nested = (FbInstance)instance.Fields["sfbAdder"].Value;
            Assert.Equal(9, nested.Fields["Sum"].Value);
        }

        [Fact]
        public void DotAccess_ReadsNestedFbVarOutputAfterInvocation()
        {
            var engine = NewEngine("sfbAdder(1, 1);\nsfbAdder();");
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            var nested = (FbInstance)instance.Fields["sfbAdder"].Value;
            Assert.Equal(2, nested.Fields["Sum"].Value);
        }
    }
}
