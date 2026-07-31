using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // An unqualified call inside a METHOD may name a plain global FUNCTION, so
    // resolution cannot stop at the FB's own ancestry: exhausting the method
    // tables has to fall back to a top-level FUNCTION POU of that name.
    public class GlobalFunctionCallTests
    {
        [Fact]
        public void CallMethod_UnqualifiedGlobalFunctionCallFromMethodBody_Resolves()
        {
            var function = new PouAst(
                "F_Double",
                null,
                "FUNCTION F_Double : INT\nVAR_INPUT\n\tnIn : INT;\nEND_VAR",
                "F_Double := nIn * 2;",
                new List<MethodAst>());

            var caller = new MethodAst(
                "bAdd",
                "METHOD bAdd : BOOL",
                "nResult := F_Double(nValue);");

            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tnValue : INT := 21;\n\tnResult : INT;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var engine = new Engine(new TypeRegistry(new[] { fb, function }));
            var instance = engine.NewInstance("FB_Widget");

            engine.CallMethod(instance, "bAdd", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(42, instance.Fields["nResult"].Value);
        }

        [Fact]
        public void CallMethod_GlobalFunctionDeclarationLeadsWithBlockComment_StillResolves()
        {
            // A header comment above the FUNCTION keyword is the house
            // convention in PLC source, so recognizing a global FUNCTION
            // declaration cannot depend on that keyword coming first.
            var function = new PouAst(
                "F_Double",
                null,
                "(*\nPurpose comment describing F_Double.\n*)\nFUNCTION F_Double : INT\nVAR_INPUT\n\tnIn : INT;\nEND_VAR",
                "F_Double := nIn * 2;",
                new List<MethodAst>());

            var caller = new MethodAst(
                "bAdd",
                "METHOD bAdd : BOOL",
                "nResult := F_Double(nValue);");

            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tnValue : INT := 21;\n\tnResult : INT;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var engine = new Engine(new TypeRegistry(new[] { fb, function }));
            var instance = engine.NewInstance("FB_Widget");

            engine.CallMethod(instance, "bAdd", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(42, instance.Fields["nResult"].Value);
        }
    }
}
