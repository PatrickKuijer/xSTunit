using System;
using System.Collections.Generic;
using xStunit.Interpreter.Extensibility;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A compiled-only library function or FB method with a VAR_OUTPUT is called
    // as 'F(nIn := x, nOut => y)', and the caller reads y afterwards. A plugin
    // must behave like an interpreted FUNCTION/METHOD there: every output it
    // sets reaches its '=>' target, and the target itself is never an input.
    public class NativeOutputBindingTests
    {
        private sealed class StubFunction : IXstunitNativeFunction
        {
            private readonly Func<NativeCallContext, object> _body;

            public StubFunction(string name, Func<NativeCallContext, object> body)
            {
                Name = name;
                _body = body;
            }

            public string Name { get; }

            public object Invoke(NativeCallContext context) => _body(context);
        }

        // A widget whose bDoWork method runs the given body against F_Split.
        private sealed class FunctionCallSite
        {
            private readonly Engine _engine;

            public FunctionCallSite(string declarations, string body, Func<NativeCallContext, object> plugin)
            {
                var caller = new MethodAst("bDoWork", "METHOD bDoWork : BOOL", body);
                var fb = new PouAst("FB_Widget", null, declarations, "", new List<MethodAst> { caller });

                var functions = new NativeFunctionRegistry();
                functions.Register(new StubFunction("F_Split", plugin));

                _engine = new Engine(new TypeRegistry(new[] { fb }), functions);
                Instance = _engine.NewInstance("FB_Widget");
            }

            public FbInstance Instance { get; }

            public object Field(string name) => Instance.Fields[name].Value;

            public void Run() =>
                _engine.CallMethod(Instance, "bDoWork", new Expr[0], new NamedArg[0], null, null);
        }

        private static FunctionCallSite RunFunctionCall(
            string declarations,
            string body,
            Func<NativeCallContext, object> plugin)
        {
            var site = new FunctionCallSite(declarations, body, plugin);
            site.Run();
            return site;
        }

        private static object SplitSeventeen(NativeCallContext ctx)
        {
            var input = ctx.RequireInt32("nIn", 0);
            ctx.SetOutput("nRemainder", input % 5);
            return input / 5;
        }

        [Fact]
        public void FunctionCall_OutputSetByThePlugin_IsWrittenToItsTarget()
        {
            var site = RunFunctionCall(
                "VAR\n\tnResult : INT;\n\tnRemainder : INT;\nEND_VAR",
                "nResult := F_Split(nIn := 17, nRemainder => nRemainder);",
                SplitSeventeen);

            Assert.Equal(3, site.Field("nResult"));
            Assert.Equal(2, site.Field("nRemainder"));
        }

        // The write-back happens inside expression evaluation, not only for a
        // call that stands alone as a statement, and the return value still
        // flows into the surrounding expression.
        [Fact]
        public void FunctionCall_InsideAnExpression_WritesItsOutputAndYieldsItsReturnValue()
        {
            var site = RunFunctionCall(
                "VAR\n\tnResult : INT;\n\tnRemainder : INT;\nEND_VAR",
                "nResult := F_Split(nIn := 17, nRemainder => nRemainder) * 10 + 1;",
                SplitSeventeen);

            Assert.Equal(31, site.Field("nResult"));
            Assert.Equal(2, site.Field("nRemainder"));
        }

        // The target is an lvalue expression, not just a name: its index is
        // evaluated at write-back, the way an assignment to it would be.
        [Fact]
        public void FunctionCall_ArrayElementTargetWithAnIndexExpression_ReceivesTheOutput()
        {
            var site = RunFunctionCall(
                "VAR\n\tnResult : INT;\n\taOut : ARRAY[0..3] OF INT;\n\tnIdx : INT := 1;\nEND_VAR",
                "nResult := F_Split(nIn := 17, nRemainder => aOut[nIdx + 1]);",
                SplitSeventeen);

            var array = Assert.IsType<ArrayValue>(site.Field("aOut"));
            Assert.Equal(new object[] { 0, 0, 2, 0 }, array.Elements);
        }

        // Inputs are evaluated before the plugin runs and outputs written after
        // it returns, so one variable used as both sees its pre-call value as the
        // input and ends up holding the output.
        [Fact]
        public void FunctionCall_SameVariableAsInputAndOutputTarget_InputSeesThePreCallValue()
        {
            var seenInput = -1;
            var site = RunFunctionCall(
                "VAR\n\tnResult : INT;\n\tnValue : INT := 17;\nEND_VAR",
                "nResult := F_Split(nIn := nValue, nRemainder => nValue);",
                ctx =>
                {
                    seenInput = ctx.RequireInt32("nIn", 0);
                    return SplitSeventeen(ctx);
                });

            Assert.Equal(17, seenInput);
            Assert.Equal(3, site.Field("nResult"));
            Assert.Equal(2, site.Field("nValue"));
        }

        // IEC identifiers are case-insensitive, and the plugin sets the name it
        // declares while the caller may spell it any way. A case-sensitive match
        // would make the call fault as if the output had never been set.
        [Fact]
        public void FunctionCall_OutputNameSpelledInADifferentCase_StillBindsAndWritesBack()
        {
            var sawBinding = false;
            var site = RunFunctionCall(
                "VAR\n\tnResult : INT;\n\tnRemainder : INT;\nEND_VAR",
                "nResult := F_Split(NIN := 17, NREMAINDER => nRemainder);",
                ctx =>
                {
                    sawBinding = ctx.IsOutputBound("nRemainder");
                    return SplitSeventeen(ctx);
                });

            Assert.True(sawBinding);
            Assert.Equal(2, site.Field("nRemainder"));
        }

        // The '=>' target's current value is caller state, not an argument. A
        // plugin that could read it by the output's name would take a stale
        // value for an input.
        [Fact]
        public void FunctionCall_OutputTarget_IsNotPassedAsANamedInput()
        {
            var visibleAsInput = true;
            var visibleByTryGetArg = true;
            RunFunctionCall(
                "VAR\n\tnResult : INT;\n\tnRemainder : INT := 99;\nEND_VAR",
                "nResult := F_Split(nIn := 17, nRemainder => nRemainder);",
                ctx =>
                {
                    visibleAsInput = ctx.NamedArgs.ContainsKey("nRemainder");
                    visibleByTryGetArg = ctx.TryGetArg("nRemainder", -1, out _);
                    return SplitSeventeen(ctx);
                });

            Assert.False(visibleAsInput);
            Assert.False(visibleByTryGetArg);
        }

        // A plugin declares no output types, so for an output the caller bound
        // but the plugin never set there is no default the interpreter could
        // honestly write; the call must fault rather than leave the target at
        // whatever it held before.
        [Fact]
        public void FunctionCall_BoundOutputThePluginNeverSets_FaultsNamingFunctionAndOutput()
        {
            var site = new FunctionCallSite(
                "VAR\n\tnResult : INT;\n\tnRemainder : INT;\nEND_VAR",
                "nResult := F_Split(nIn := 17, nRemainder => nRemainder);",
                _ => 3);

            var ex = Assert.Throws<InvalidOperationException>(site.Run);

            Assert.Contains("F_Split did not set output 'nRemainder'", ex.Message);
        }

        // The fault is raised before any output is written, so a failing call
        // does not leave the caller with some targets updated and others not.
        [Fact]
        public void FunctionCall_OneBoundOutputMissing_WritesNoneOfTheOthers()
        {
            var site = new FunctionCallSite(
                "VAR\n\tnResult : INT;\n\tnQuotient : INT := -1;\n\tnRemainder : INT;\nEND_VAR",
                "nResult := F_Split(nIn := 17, nQuotient => nQuotient, nRemainder => nRemainder);",
                ctx =>
                {
                    ctx.SetOutput("nQuotient", 3);
                    return 0;
                });

            Assert.Throws<InvalidOperationException>(site.Run);
            Assert.Equal(-1, site.Field("nQuotient"));
        }

        // A plugin that throws has not completed the call, so outputs it set
        // before throwing must not reach the caller.
        [Fact]
        public void FunctionCall_PluginThatThrows_WritesNoOutputBack()
        {
            var site = new FunctionCallSite(
                "VAR\n\tnResult : INT;\n\tnRemainder : INT := 99;\nEND_VAR",
                "nResult := F_Split(nIn := 17, nRemainder => nRemainder);",
                ctx =>
                {
                    ctx.SetOutput("nRemainder", 2);
                    throw new InvalidOperationException("F_Split rejected its input");
                });

            var ex = Assert.Throws<InvalidOperationException>(site.Run);

            Assert.Equal("F_Split rejected its input", ex.Message);
            Assert.Equal(99, site.Field("nRemainder"));
        }

        // TwinCAT lets a caller omit any output, so a plugin has to be free to
        // set every output it declares without first checking which ones this
        // particular call bound.
        [Fact]
        public void FunctionCall_OutputSetButNotBoundByTheCaller_IsIgnored()
        {
            var site = RunFunctionCall(
                "VAR\n\tnResult : INT;\nEND_VAR",
                "nResult := F_Split(nIn := 17);",
                SplitSeventeen);

            Assert.Equal(3, site.Field("nResult"));
        }

        // 'name =>' names an output without binding it. It must neither count as
        // bound (which would fault a plugin that leaves it unset) nor be
        // evaluated (there is no target expression to evaluate).
        [Fact]
        public void FunctionCall_OutputListedWithNoTarget_IsNotBoundAndDoesNotFault()
        {
            var sawBinding = true;
            var site = RunFunctionCall(
                "VAR\n\tnResult : INT;\nEND_VAR",
                "nResult := F_Split(nIn := 17, nRemainder => );",
                ctx =>
                {
                    sawBinding = ctx.IsOutputBound("nRemainder");
                    return 3;
                });

            Assert.False(sawBinding);
            Assert.Equal(3, site.Field("nResult"));
        }

        // Write-back is an assignment into the target, so the target's declared
        // type governs the stored value just as it would for 'f := 2;'. A raw
        // store would leave an int boxed in an LREAL cell, and a later
        // AssertEquals_LREAL or arithmetic on it would misbehave.
        [Fact]
        public void FunctionCall_OutputValue_IsCoercedToTheTargetsDeclaredType()
        {
            var site = RunFunctionCall(
                "VAR\n\tnResult : INT;\n\tfRemainder : LREAL;\nEND_VAR",
                "nResult := F_Split(nIn := 17, nRemainder => fRemainder);",
                SplitSeventeen);

            Assert.Equal(2.0, site.Field("fRemainder"));
        }

        // A method-shaped vendor FB (Enter/Leave, Read with a bytes-read output)
        // reaches the plugin through the function-block surface, not the
        // function one, so its outputs must be written back on that path too.
        private sealed class MethodBlock : IXstunitNativeFunctionBlock
        {
            private readonly Func<NativeCallContext, object> _method;

            public MethodBlock(Func<NativeCallContext, object> method)
            {
                _method = method;
            }

            public string TypeName => "FB_Splitter";

            public IReadOnlyList<NativeFieldDeclaration> Fields => Array.Empty<NativeFieldDeclaration>();

            public IReadOnlyList<string> PositionalInputNames => Array.Empty<string>();

            public IReadOnlyList<string> MethodNames => new[] { "Split" };

            public IXstunitNativeFunctionBlock CreateInstance() => new MethodBlock(_method);

            public object Invoke(NativeFunctionBlockCall call) => _method(call.Arguments);
        }

        private static Engine EngineWithBlock(string declarations, string body, IXstunitNativeFunctionBlock block)
        {
            var wrapper = new PouAst("FB_Wrapper", null, declarations, body, new List<MethodAst>());

            var blocks = new NativeFunctionBlockRegistry();
            blocks.Register(block);

            return new Engine(new TypeRegistry(new[] { wrapper }), new NativePlugins(null, blocks));
        }

        private static void Step(Engine engine, FbInstance instance) =>
            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

        [Fact]
        public void FunctionBlockMethodCall_OutputSetByThePlugin_IsWrittenToItsTarget()
        {
            var engine = EngineWithBlock(
                "VAR\n\tfbSplit : FB_Splitter;\n\tnResult : INT;\n\tnRemainder : INT;\nEND_VAR",
                "nResult := fbSplit.Split(nIn := 17, nRemainder => nRemainder);",
                new MethodBlock(SplitSeventeen));
            var instance = engine.NewInstance("FB_Wrapper");

            Step(engine, instance);

            Assert.Equal(3, instance.Fields["nResult"].Value);
            Assert.Equal(2, instance.Fields["nRemainder"].Value);
        }

        // A method name alone is ambiguous across FB types, so the fault names
        // the type that owns it - the plugin whose code has to change.
        [Fact]
        public void FunctionBlockMethodCall_BoundOutputThePluginNeverSets_FaultsNamingTypeAndMethod()
        {
            var engine = EngineWithBlock(
                "VAR\n\tfbSplit : FB_Splitter;\n\tnResult : INT;\n\tnRemainder : INT;\nEND_VAR",
                "nResult := fbSplit.Split(nIn := 17, nRemainder => nRemainder);",
                new MethodBlock(_ => 3));
            var instance = engine.NewInstance("FB_Wrapper");

            var ex = Assert.Throws<InvalidOperationException>(() => Step(engine, instance));

            Assert.Contains("FB_Splitter.Split did not set output 'nRemainder'", ex.Message);
        }

        // A bare 'fb(...)' call on a plugin FB publishes its outputs through the
        // instance's fields, not the call context, and its '=>' targets are
        // written from there.
        private sealed class FieldBlock : IXstunitNativeFunctionBlock
        {
            public string TypeName => "FB_SplitCycle";

            public IReadOnlyList<NativeFieldDeclaration> Fields => new[]
            {
                new NativeFieldDeclaration("nIn", 0),
                new NativeFieldDeclaration("nRemainder", 0),
            };

            public IReadOnlyList<string> PositionalInputNames => new[] { "nIn" };

            public IReadOnlyList<string> MethodNames => Array.Empty<string>();

            public IXstunitNativeFunctionBlock CreateInstance() => new FieldBlock();

            public object Invoke(NativeFunctionBlockCall call)
            {
                call.SetField("nRemainder", Convert.ToInt32(call.GetField("nIn")) % 5);
                return null;
            }
        }

        [Fact]
        public void FunctionBlockBareCall_OutputFieldIsWrittenToItsTarget()
        {
            var engine = EngineWithBlock(
                "VAR\n\tfbSplit : FB_SplitCycle;\n\tnRemainder : INT := 99;\nEND_VAR",
                "fbSplit(nIn := 17, nRemainder => nRemainder);",
                new FieldBlock());
            var instance = engine.NewInstance("FB_Wrapper");

            Step(engine, instance);

            Assert.Equal(2, instance.Fields["nRemainder"].Value);
        }
    }
}
