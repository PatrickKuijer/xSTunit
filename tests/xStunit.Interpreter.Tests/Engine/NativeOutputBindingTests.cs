using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Interpreter.Extensibility;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A compiled-only library function or FB method with a VAR_OUTPUT is called
    // as 'F(nIn := x, nOut => y)', and the caller reads y afterwards. Without an
    // output channel on the plugin contract y silently kept its old value, and
    // the '=>' target was handed to the plugin as if it were an input. These pin
    // that the plugin surfaces now behave like an interpreted FUNCTION/METHOD:
    // outputs reach their '=>' targets, and the target is never an input.
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

        private static FbInstance RunFunctionCall(
            string declarations,
            string body,
            Func<NativeCallContext, object> plugin)
        {
            var caller = new MethodAst("bDoWork", "METHOD bDoWork : BOOL", body);
            var fb = new PouAst("FB_Widget", null, declarations, "", new List<MethodAst> { caller });

            var functions = new NativeFunctionRegistry();
            functions.Register(new StubFunction("F_Split", plugin));

            var engine = new Engine(new TypeRegistry(new[] { fb }), functions);
            var instance = engine.NewInstance("FB_Widget");
            engine.CallMethod(instance, "bDoWork", new Expr[0], new NamedArg[0], null, null);
            return instance;
        }

        [Fact]
        public void FunctionCall_OutputSetByThePlugin_IsWrittenToItsTarget()
        {
            var instance = RunFunctionCall(
                "VAR\n\tnResult : INT;\n\tnRemainder : INT;\nEND_VAR",
                "nResult := F_Split(nIn := 17, nRemainder => nRemainder);",
                ctx =>
                {
                    var input = ctx.RequireInt32("nIn", 0);
                    ctx.SetOutput("nRemainder", input % 5);
                    return input / 5;
                });

            Assert.Equal(3, instance.Fields["nResult"].Value);
            Assert.Equal(2, instance.Fields["nRemainder"].Value);
        }

        // IEC identifiers are case-insensitive, and the plugin sets the name it
        // declares while the caller may spell it any way. A case-sensitive match
        // would make the call fault as if the output had never been set.
        [Fact]
        public void FunctionCall_OutputNameSpelledInADifferentCase_StillBindsAndWritesBack()
        {
            var sawBinding = false;
            var instance = RunFunctionCall(
                "VAR\n\tnResult : INT;\n\tnRemainder : INT;\nEND_VAR",
                "nResult := F_Split(NIN := 17, NREMAINDER => nRemainder);",
                ctx =>
                {
                    sawBinding = ctx.IsOutputBound("nRemainder");
                    ctx.SetOutput("nRemainder", ctx.RequireInt32("nIn", 0) % 5);
                    return 0;
                });

            Assert.True(sawBinding);
            Assert.Equal(2, instance.Fields["nRemainder"].Value);
        }

        // The '=>' target's current value is caller state, not an argument. A
        // plugin that could read it by the output's name would take a stale
        // value for an input, which is exactly the confusion the native FB
        // hosts had with a counter's CV.
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
                    ctx.SetOutput("nRemainder", 0);
                    return 0;
                });

            Assert.False(visibleAsInput);
            Assert.False(visibleByTryGetArg);
        }

        // A plugin declares no output types, so for an output the caller bound
        // but the plugin forgot there is no default the interpreter could
        // honestly write. Silently leaving the target alone is the bug this
        // contract exists to remove.
        [Fact]
        public void FunctionCall_BoundOutputThePluginNeverSets_FaultsNamingFunctionAndOutput()
        {
            var ex = Assert.ThrowsAny<Exception>(() => RunFunctionCall(
                "VAR\n\tnResult : INT;\n\tnRemainder : INT;\nEND_VAR",
                "nResult := F_Split(nIn := 17, nRemainder => nRemainder);",
                _ => 3));

            Assert.Contains("F_Split", ex.ToString());
            Assert.Contains("nRemainder", ex.ToString());
        }

        // The fault is raised before any output is written, so a failing call
        // does not leave the caller with some targets updated and others not.
        [Fact]
        public void FunctionCall_OneBoundOutputMissing_WritesNoneOfTheOthers()
        {
            var caller = new MethodAst(
                "bDoWork",
                "METHOD bDoWork : BOOL",
                "nResult := F_Split(nIn := 17, nQuotient => nQuotient, nRemainder => nRemainder);");
            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tnResult : INT;\n\tnQuotient : INT := -1;\n\tnRemainder : INT;\nEND_VAR",
                "",
                new List<MethodAst> { caller });

            var functions = new NativeFunctionRegistry();
            functions.Register(new StubFunction("F_Split", ctx =>
            {
                ctx.SetOutput("nQuotient", 3);
                return 0;
            }));

            var engine = new Engine(new TypeRegistry(new[] { fb }), functions);
            var instance = engine.NewInstance("FB_Widget");

            Assert.ThrowsAny<Exception>(() =>
                engine.CallMethod(instance, "bDoWork", new Expr[0], new NamedArg[0], null, null));
            Assert.Equal(-1, instance.Fields["nQuotient"].Value);
        }

        // TwinCAT lets a caller omit any output, so a plugin has to be free to
        // set every output it declares without first checking which ones this
        // particular call bound.
        [Fact]
        public void FunctionCall_OutputSetButNotBoundByTheCaller_IsIgnored()
        {
            var instance = RunFunctionCall(
                "VAR\n\tnResult : INT;\nEND_VAR",
                "nResult := F_Split(nIn := 17);",
                ctx =>
                {
                    ctx.SetOutput("nRemainder", 2);
                    return 3;
                });

            Assert.Equal(3, instance.Fields["nResult"].Value);
        }

        // 'name =>' names an output without binding it. It must neither count as
        // bound (which would fault a plugin that leaves it unset) nor be
        // evaluated (there is no target expression to evaluate).
        [Fact]
        public void FunctionCall_OutputListedWithNoTarget_IsNotBoundAndDoesNotFault()
        {
            var sawBinding = true;
            var instance = RunFunctionCall(
                "VAR\n\tnResult : INT;\nEND_VAR",
                "nResult := F_Split(nIn := 17, nRemainder => );",
                ctx =>
                {
                    sawBinding = ctx.IsOutputBound("nRemainder");
                    return 3;
                });

            Assert.False(sawBinding);
            Assert.Equal(3, instance.Fields["nResult"].Value);
        }

        // Write-back is an assignment into the target, so the target's declared
        // type governs the stored value just as it would for 'f := 2;'. A raw
        // store would leave an int boxed in an LREAL cell, and a later
        // AssertEquals_LREAL or arithmetic on it would misbehave.
        [Fact]
        public void FunctionCall_OutputValue_IsCoercedToTheTargetsDeclaredType()
        {
            var instance = RunFunctionCall(
                "VAR\n\tnResult : INT;\n\tfRemainder : LREAL;\nEND_VAR",
                "nResult := F_Split(nIn := 17, nRemainder => fRemainder);",
                ctx =>
                {
                    ctx.SetOutput("nRemainder", 2);
                    return 3;
                });

            Assert.Equal(2.0, instance.Fields["fRemainder"].Value);
        }

        // A method-shaped vendor FB (Enter/Leave, Read with a bytes-read output)
        // reaches the plugin through the function-block surface, not the
        // function one, so it has to write its outputs back on its own path.
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

        private static FbInstance RunMethodCall(
            string declarations,
            string body,
            Func<NativeCallContext, object> method)
        {
            var wrapper = new PouAst("FB_Wrapper", null, declarations, body, new List<MethodAst>());

            var blocks = new NativeFunctionBlockRegistry();
            blocks.Register(new MethodBlock(method));

            var engine = new Engine(new TypeRegistry(new[] { wrapper }), new NativePlugins(null, blocks));
            var instance = engine.NewInstance("FB_Wrapper");
            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);
            return instance;
        }

        [Fact]
        public void FunctionBlockMethodCall_OutputSetByThePlugin_IsWrittenToItsTarget()
        {
            var instance = RunMethodCall(
                "VAR\n\tfbSplit : FB_Splitter;\n\tnResult : INT;\n\tnRemainder : INT;\nEND_VAR",
                "nResult := fbSplit.Split(nIn := 17, nRemainder => nRemainder);",
                ctx =>
                {
                    var input = ctx.RequireInt32("nIn", 0);
                    ctx.SetOutput("nRemainder", input % 5);
                    return input / 5;
                });

            Assert.Equal(3, instance.Fields["nResult"].Value);
            Assert.Equal(2, instance.Fields["nRemainder"].Value);
        }

        [Fact]
        public void FunctionBlockMethodCall_BoundOutputThePluginNeverSets_Faults()
        {
            var ex = Assert.ThrowsAny<Exception>(() => RunMethodCall(
                "VAR\n\tfbSplit : FB_Splitter;\n\tnResult : INT;\n\tnRemainder : INT;\nEND_VAR",
                "nResult := fbSplit.Split(nIn := 17, nRemainder => nRemainder);",
                _ => 3));

            Assert.Contains("Split", ex.ToString());
            Assert.Contains("nRemainder", ex.ToString());
        }

        // A bare 'fb(...)' call on a plugin FB publishes its outputs through the
        // instance's fields, not the call context, and its '=>' targets are
        // written from there. That path must keep working alongside the
        // context-based one the method surface now uses.
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
            var wrapper = new PouAst(
                "FB_Wrapper",
                null,
                "VAR\n\tfbSplit : FB_SplitCycle;\n\tnRemainder : INT := 99;\nEND_VAR",
                "fbSplit(nIn := 17, nRemainder => nRemainder);",
                new List<MethodAst>());

            var blocks = new NativeFunctionBlockRegistry();
            blocks.Register(new FieldBlock());

            var engine = new Engine(new TypeRegistry(new[] { wrapper }), new NativePlugins(null, blocks));
            var instance = engine.NewInstance("FB_Wrapper");
            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            Assert.Equal(2, instance.Fields["nRemainder"].Value);
        }
    }
}
