using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A METHOD's VAR_TEMP local is re-initialized to its default on every call,
    // never carried over the way an FB-instance VAR field is.
    public class VarTempLocalsTests
    {
        [Fact]
        public void CallMethod_VarTempLocal_IsDeclaredAndReadable()
        {
            var method = new MethodAst(
                "M_Test",
                "METHOD PRIVATE M_Test\nVAR_TEMP\n\tx : UINT;\nEND_VAR",
                "x := x + 1;\nfResult := x;");

            var pou = new PouAst(
                "FB_VarTempFixture",
                null,
                "VAR\n\tfResult : UINT;\nEND_VAR",
                "",
                new List<MethodAst> { method });

            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_VarTempFixture");

            engine.CallMethod(instance, "M_Test", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(1, instance.Fields["fResult"].Value);
        }

        [Fact]
        public void CallMethod_VarTempLocal_ResetsToDefaultOnEveryCall()
        {
            var method = new MethodAst(
                "M_Test",
                "METHOD PRIVATE M_Test\nVAR_TEMP\n\tx : UINT;\nEND_VAR",
                "x := x + 1;\nfResult := x;");

            var pou = new PouAst(
                "FB_VarTempFixture",
                null,
                "VAR\n\tfResult : UINT;\nEND_VAR",
                "",
                new List<MethodAst> { method });

            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_VarTempFixture");

            engine.CallMethod(instance, "M_Test", new Expr[0], new NamedArg[0], null, null);
            engine.CallMethod(instance, "M_Test", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(1, instance.Fields["fResult"].Value);
        }
    }
}
