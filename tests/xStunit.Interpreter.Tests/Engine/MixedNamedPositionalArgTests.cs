using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Interpreter.Extensibility;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // IEC 61131-3 lets a call mix named and positional arguments - e.g.
    // F_FindSubstring(sSearchText := buffer, '[') - and binds each positional
    // value to the next formal parameter not already filled by name, in
    // declaration order. The trap is that a native function's `position` is
    // always the parameter's DECLARED index, so indexing straight into the
    // positional list misses by the number of named arguments ahead of it.
    public class MixedNamedPositionalArgTests
    {
        private sealed class StubFunction : IXstunitNativeFunction
        {
            private readonly System.Func<NativeCallContext, object> _body;
            public StubFunction(string name, System.Func<NativeCallContext, object> body) { Name = name; _body = body; }
            public string Name { get; }
            public object Invoke(NativeCallContext context) => _body(context);
        }

        private static NativeFunctionRegistry RegistryWith(params IXstunitNativeFunction[] functions)
        {
            var registry = new NativeFunctionRegistry();
            registry.RegisterAll(functions);
            return registry;
        }

        [Fact]
        public void CallMethod_NativeFunction_NamedArgFollowedByPositionalArg_Binds()
        {
            var caller = new MethodAst(
                "bDoWork",
                "METHOD bDoWork : BOOL",
                "nResult := F_Sum(nA := 2, 40);");

            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tnResult : INT;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var engine = new Engine(
                new TypeRegistry(new[] { fb }),
                RegistryWith(new StubFunction(
                    "F_Sum",
                    ctx => ctx.RequireInt32("nA", 0) + ctx.RequireInt32("nB", 1))));

            var instance = engine.NewInstance("FB_Widget");
            engine.CallMethod(instance, "bDoWork", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(42, instance.Fields["nResult"].Value);
        }

        [Fact]
        public void CallMethod_NativeFunction_NamedArgForMiddleParamFollowedByTwoPositionalArgs_Binds()
        {
            var caller = new MethodAst(
                "bDoWork",
                "METHOD bDoWork : BOOL",
                "nResult := F_Sum3(nB := 5, 10, 20);");

            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tnResult : INT;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var engine = new Engine(
                new TypeRegistry(new[] { fb }),
                RegistryWith(new StubFunction(
                    "F_Sum3",
                    ctx => ctx.RequireInt32("nA", 0) + ctx.RequireInt32("nB", 1) + ctx.RequireInt32("nC", 2))));

            var instance = engine.NewInstance("FB_Widget");
            engine.CallMethod(instance, "bDoWork", new Expr[0], new NamedArg[0], null, null);

            // nA=10 (first unfilled), nB=5 (named), nC=20 (second unfilled).
            Assert.Equal(35, instance.Fields["nResult"].Value);
        }

        [Fact]
        public void CallMethod_NativeFunction_GenuinelyMissingPositionalArg_ThrowsUsefulMessage()
        {
            // Skipping over named arguments must not turn a genuinely absent
            // one into a silent bind: it still fails, and the message points at
            // the named argument that shifted the positions.
            var caller = new MethodAst(
                "bDoWork",
                "METHOD bDoWork : BOOL",
                "nResult := F_Sum(nA := 2);");

            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tnResult : INT;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var engine = new Engine(
                new TypeRegistry(new[] { fb }),
                RegistryWith(new StubFunction(
                    "F_Sum",
                    ctx => ctx.RequireInt32("nA", 0) + ctx.RequireInt32("nB", 1))));

            var instance = engine.NewInstance("FB_Widget");

            var ex = Assert.Throws<System.InvalidOperationException>(() =>
                engine.CallMethod(instance, "bDoWork", new Expr[0], new NamedArg[0], null, null));

            Assert.Contains("nB", ex.Message);
            Assert.Contains("missing required argument", ex.Message);
            Assert.Contains("preceding named argument", ex.Message);
        }

        [Fact]
        public void CallMethod_NativeFunction_NoArgumentsAtAllStillReportsPlainMissingMessage()
        {
            // The "preceding named argument" wording is specific to the mixed
            // case; a plain call with nothing named keeps the raw-count
            // phrasing, which is the more useful diagnosis there.
            var caller = new MethodAst(
                "bDoWork",
                "METHOD bDoWork : BOOL",
                "nResult := F_Sum();");

            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tnResult : INT;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var engine = new Engine(
                new TypeRegistry(new[] { fb }),
                RegistryWith(new StubFunction(
                    "F_Sum",
                    ctx => ctx.RequireInt32("nA", 0) + ctx.RequireInt32("nB", 1))));

            var instance = engine.NewInstance("FB_Widget");

            var ex = Assert.Throws<System.InvalidOperationException>(() =>
                engine.CallMethod(instance, "bDoWork", new Expr[0], new NamedArg[0], null, null));

            Assert.Contains("nA", ex.Message);
            Assert.Contains("got 0 positional and 0 named argument(s)", ex.Message);
        }

        [Fact]
        public void CallGlobalFunction_NamedArgFollowedByPositionalArg_Binds()
        {
            // A user-defined FUNCTION binds its arguments through a different
            // path than a native-function plugin does; both must agree on the
            // mixed named/positional rule.
            var function = new PouAst(
                "F_FindSubstring",
                null,
                "FUNCTION F_FindSubstring : INT\nVAR_INPUT\n\tsSearchText : STRING;\n\tsPattern : STRING;\nEND_VAR",
                "IF sPattern = '[' THEN\n\tF_FindSubstring := 1;\nELSE\n\tF_FindSubstring := 0;\nEND_IF",
                new List<MethodAst>());

            var caller = new MethodAst(
                "bRun",
                "METHOD bRun : BOOL",
                "nPos := F_FindSubstring(sSearchText := sBuffer, '[');");

            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tsBuffer : STRING := 'data[structure';\n\tnPos : INT;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var engine = new Engine(new TypeRegistry(new[] { fb, function }));
            var instance = engine.NewInstance("FB_Widget");

            engine.CallMethod(instance, "bRun", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(1, instance.Fields["nPos"].Value);
        }
    }
}
