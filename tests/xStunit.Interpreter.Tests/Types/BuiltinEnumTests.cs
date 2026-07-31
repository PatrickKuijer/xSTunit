using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcEventSeverity is library-supplied and has no DUT source in any
    // solution under test, so a POU referencing TcEventSeverity.Warning can
    // only resolve through the built-in enum table.
    public class BuiltinEnumTests
    {
        [Fact]
        public void TcEventSeverity_MemberAccess_ResolvesToUnderlyingValue()
        {
            var pou = new PouAst(
                "FB_Wrapper",
                null,
                "VAR\n\tmeasured : DINT;\nEND_VAR",
                "measured := TcEventSeverity.Warning;",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_Wrapper");
            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            Assert.Equal(3, instance.Fields["measured"].Value);
        }

        [Fact]
        public void TcEventSeverity_TypedField_DefaultsWithoutError()
        {
            var pou = new PouAst(
                "FB_Wrapper",
                null,
                "VAR\n\tseverity : TcEventSeverity;\nEND_VAR",
                "",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_Wrapper");

            Assert.Equal(0, instance.Fields["severity"].Value);
        }
    }
}
