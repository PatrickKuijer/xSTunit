using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Interpreter.Extensibility;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-kuc: a call mixing a named argument with a trailing positional
    // argument - e.g. F_FindSubstring(sSearchText := buffer, '[') - real
    // TwinCAT/IEC 61131-3 binds the positional value to the next unfilled
    // formal parameter in declaration order (skipping the one already given
    // by name). BindParams (Engine.Invocation.cs, via the shared
    // ArgBinder.TryResolveArg) already got this right for FUNCTION/METHOD
    // calls. The bug lived one layer over, in
    // NativeCallContext.TryGetArg (Extensibility/NativeCallContext.cs): a
    // native-function plugin's `position` argument is always the parameter's
    // declared index in the signature (see every ITcXunitNativeFunction
    // under samples/), but TryGetArg indexed straight into PositionalArgs by
    // that raw declared index - so once a named argument occupied an earlier
    // declared position, every later positional lookup missed by exactly the
    // number of named arguments that preceded it.
    public class MixedNamedPositionalArgTests
    {
        private sealed class StubFunction : ITcXunitNativeFunction
        {
            private readonly System.Func<NativeCallContext, object> _body;
            public StubFunction(string name, System.Func<NativeCallContext, object> body) { Name = name; _body = body; }
            public string Name { get; }
            public object Invoke(NativeCallContext context) => _body(context);
        }

        private static NativeFunctionRegistry RegistryWith(params ITcXunitNativeFunction[] functions)
        {
            var registry = new NativeFunctionRegistry();
            registry.RegisterAll(functions);
            return registry;
        }

        [Fact]
        public void CallMethod_NativeFunction_NamedArgFollowedByPositionalArg_Binds()
        {
            // Repro shape from the ticket: name the first declared param,
            // then supply a trailing positional arg that must fill the next
            // unfilled param (nB) - not throw "missing required argument".
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
            // The named argument doesn't have to be the first declared
            // param: naming the middle one of three still leaves the two
            // trailing positional args filling the two unfilled params
            // (nA, nC) in declaration order.
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
            // No-regression check: with no trailing positional arg supplied
            // at all, resolution should still fail, and the message should
            // still name the missing parameter and explain the shortfall in
            // terms of the preceding named argument, not a raw count that
            // reads the same for an ordinary missing argument.
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
            // A call with nothing named at all still gets the original
            // "got N positional and M named" phrasing - the new "preceding
            // named argument" wording is specific to the mixed case.
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
            // The ticket's literal repro shape: a plain user-defined FUNCTION
            // (not a native-function plugin) called with a named first
            // argument and a trailing positional second argument. This path
            // (CallGlobalFunction -> BindParams -> ArgBinder.TryResolveArg,
            // Engine.Invocation.cs) already binds this correctly - kept here
            // as a lock-in regression alongside the native-function fix
            // above, so the two call-binding paths this ticket touches on
            // both stay covered.
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
