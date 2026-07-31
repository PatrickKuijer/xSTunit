using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcXunit-3g7: VAR_TEMP wasn't recognized by VarBlockParser's
    // section-header switch, so its declarations were silently dropped and
    // the engine threw "Unknown variable" the first time the body read one.
    // Covers the end-to-end fix: VAR_TEMP locals are declared, and (mirroring
    // real IEC 61131-3 semantics) are re-initialized to their default value
    // on every call rather than persisting like a FB-instance VAR field.
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
