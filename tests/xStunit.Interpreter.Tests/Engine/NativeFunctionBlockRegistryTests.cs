using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Interpreter.Extensibility;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // The out-of-tree path for a STATEFUL library FB: everything the in-tree
    // stubs (TON, R_TRIG, CTU) get from a NativeHostKind member and a dispatch
    // case, a plugin assembly must get from registration alone. What these pin
    // is that the two paths behave the same - state survives cycles, outputs
    // reach ST, instances stay independent - while the plugin path costs the
    // Engine no new knowledge of any particular vendor FB.
    public class NativeFunctionBlockRegistryTests
    {
        // An accumulator, because a pure function could not be one: nAdd is
        // summed across invocations, so a plugin FB that failed to carry state
        // between cycles would read 0 every time and the totals below could not
        // come out.
        private sealed class AccumulatorBlock : IXstunitNativeFunctionBlock
        {
            private int _total;

            public string TypeName => "FB_Accumulate";

            public IReadOnlyList<NativeFieldDeclaration> Fields => new[]
            {
                new NativeFieldDeclaration("nAdd", 0),
                new NativeFieldDeclaration("nTotal", 0),
                new NativeFieldDeclaration("sLabel", "", "STRING(4)"),
            };

            public IReadOnlyList<string> PositionalInputNames => new[] { "nAdd" };

            public IReadOnlyList<string> MethodNames => new[] { "Reset" };

            public IXstunitNativeFunctionBlock CreateInstance() => new AccumulatorBlock();

            public object Invoke(NativeFunctionBlockCall call)
            {
                if (call.IsBareInvocation)
                {
                    _total += Convert.ToInt32(call.GetField("nAdd"));
                    call.SetField("nTotal", _total);
                    return null;
                }

                var previous = _total;
                _total = call.Arguments.PositionalArgs.Count > 0
                    ? call.Arguments.RequireInt32("nSeed", 0)
                    : 0;
                call.SetField("nTotal", _total);
                return previous;
            }
        }

        private static NativeFunctionBlockRegistry RegistryWith(params IXstunitNativeFunctionBlock[] blocks)
        {
            var registry = new NativeFunctionBlockRegistry();
            registry.RegisterAll(blocks);
            return registry;
        }

        private static Engine EngineWith(string declarations, string body, params MethodAst[] methods)
        {
            var pou = new PouAst("FB_Wrapper", null, declarations, body, methods.ToList());

            return new Engine(
                new TypeRegistry(new[] { pou }),
                new NativePlugins(null, RegistryWith(new AccumulatorBlock())));
        }

        private static void Step(Engine engine, FbInstance instance) =>
            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

        [Fact]
        public void BareInvoke_CarriesStateAcrossCyclesAndPublishesTheOutputToSt()
        {
            var engine = EngineWith(
                "VAR\n\tfbAcc : FB_Accumulate;\n\tnStep : INT := 3;\n\tnSeen : INT;\nEND_VAR",
                "fbAcc(nAdd := nStep);\nnSeen := fbAcc.nTotal;");

            var instance = engine.NewInstance("FB_Wrapper");

            Step(engine, instance);
            Assert.Equal(3, instance.Fields["nSeen"].Value);

            Step(engine, instance);
            Assert.Equal(6, instance.Fields["nSeen"].Value);
        }

        [Fact]
        public void BareInvoke_BindsPositionalArgumentsToTheDeclaredInputNames()
        {
            var engine = EngineWith(
                "VAR\n\tfbAcc : FB_Accumulate;\n\tnSeen : INT;\nEND_VAR",
                "fbAcc(5);\nnSeen := fbAcc.nTotal;");

            var instance = engine.NewInstance("FB_Wrapper");
            Step(engine, instance);

            Assert.Equal(5, instance.Fields["nSeen"].Value);
        }

        [Fact]
        public void TwoVariablesOfOnePluginType_DoNotShareState()
        {
            // CreateInstance, not the registered prototype, is what an instance
            // is backed by. Sharing here would make two FBs in one POU one FB,
            // which no suite could then test independently.
            var engine = EngineWith(
                "VAR\n\tfbLeft : FB_Accumulate;\n\tfbRight : FB_Accumulate;\n\tnLeft : INT;\n\tnRight : INT;\nEND_VAR",
                "fbLeft(1);\nfbRight(10);\nnLeft := fbLeft.nTotal;\nnRight := fbRight.nTotal;");

            var instance = engine.NewInstance("FB_Wrapper");
            Step(engine, instance);
            Step(engine, instance);

            Assert.Equal(2, instance.Fields["nLeft"].Value);
            Assert.Equal(20, instance.Fields["nRight"].Value);
        }

        [Fact]
        public void MethodCall_ReachesThePluginAndReturnsAValue()
        {
            var engine = EngineWith(
                "VAR\n\tfbAcc : FB_Accumulate;\n\tnBefore : INT;\n\tnSeen : INT;\nEND_VAR",
                "fbAcc(4);\nnBefore := fbAcc.Reset();\nnSeen := fbAcc.nTotal;");

            var instance = engine.NewInstance("FB_Wrapper");
            Step(engine, instance);

            Assert.Equal(4, instance.Fields["nBefore"].Value);
            Assert.Equal(0, instance.Fields["nSeen"].Value);
        }

        [Fact]
        public void MethodCall_PassesItsArgumentsThroughTheCallContext()
        {
            var engine = EngineWith(
                "VAR\n\tfbAcc : FB_Accumulate;\n\tnSeen : INT;\nEND_VAR",
                "fbAcc.Reset(7);\nnSeen := fbAcc.nTotal;");

            var instance = engine.NewInstance("FB_Wrapper");
            Step(engine, instance);

            Assert.Equal(7, instance.Fields["nSeen"].Value);
        }

        [Fact]
        public void MethodRouting_IsCaseInsensitive()
        {
            var engine = EngineWith(
                "VAR\n\tfbAcc : FB_Accumulate;\n\tnSeen : INT;\nEND_VAR",
                "fbAcc(9);\nfbAcc.RESET();\nnSeen := fbAcc.nTotal;");

            var instance = engine.NewInstance("FB_Wrapper");
            Step(engine, instance);

            Assert.Equal(0, instance.Fields["nSeen"].Value);
        }

        [Fact]
        public void TypeLookup_IsCaseInsensitive()
        {
            // Real PLC source is inconsistent about how it spells a vendor type,
            // and a declaration that missed the plugin would silently become an
            // elementary default instead of an FB instance.
            var engine = EngineWith(
                "VAR\n\tfbAcc : fb_accumulate;\n\tnSeen : INT;\nEND_VAR",
                "fbAcc(2);\nnSeen := fbAcc.nTotal;");

            var instance = engine.NewInstance("FB_Wrapper");
            Step(engine, instance);

            Assert.Equal(2, instance.Fields["nSeen"].Value);
        }

        [Fact]
        public void LibraryQualifiedBaseType_ResolvesToTheSameBlock()
        {
            // EXTENDS carries a library qualifier in real source, and the
            // qualifier names a namespace no registry could ever hold. It is
            // stripped only after the spelling as written has missed, so a
            // plugin registered under the qualified name still wins for it.
            //
            // EXTENDS rather than a VAR declaration because that is the shape
            // that reaches here today: VarBlockParser's type alternation is
            // \w+, so a dotted type on a VAR line does not match and the
            // variable is dropped before any of this runs.
            var derived = new PouAst(
                "FB_Derived",
                "SomeLibrary.FB_Accumulate",
                "VAR\n\tnOwn : INT;\nEND_VAR",
                "",
                new List<MethodAst>());

            var engine = new Engine(
                new TypeRegistry(new[] { derived }),
                new NativePlugins(null, RegistryWith(new AccumulatorBlock())));

            var instance = engine.NewInstance("FB_Derived");

            Assert.Equal(NativeHostKind.Plugin, instance.NativeKind);
            // The plugin's declared fields are seeded on the instance, which is
            // what a suite reads its outputs back through.
            Assert.True(instance.Fields.ContainsKey("nTotal"));
        }

        [Fact]
        public void DeclaredFieldType_IsHonouredLikeARealDeclaration()
        {
            // A field that names STRING(4) truncates at 4 the way the
            // declaration says, rather than growing - the same rule every
            // declared Cell follows.
            var engine = EngineWith(
                "VAR\n\tfbAcc : FB_Accumulate;\n\tsSeen : STRING(20);\nEND_VAR",
                "fbAcc.sLabel := 'abcdefg';\nsSeen := fbAcc.sLabel;");

            var instance = engine.NewInstance("FB_Wrapper");
            Step(engine, instance);

            Assert.Equal("abcd", instance.Fields["sSeen"].Value);
        }

        [Fact]
        public void UndeclaredMethodName_IsNotRoutedToThePlugin()
        {
            // The interpreter speculatively calls FB_init on every new
            // instance. A plugin that received every name would see that one
            // too, and MethodNames is what keeps the contract to what the
            // plugin actually declared.
            var engine = EngineWith(
                "VAR\n\tfbAcc : FB_Accumulate;\nEND_VAR",
                "fbAcc.Rewind();");

            var instance = engine.NewInstance("FB_Wrapper");

            var ex = Assert.ThrowsAny<Exception>(() => Step(engine, instance));
            Assert.Contains("Rewind", ex.ToString());
        }

        [Fact]
        public void InterpretedSource_WinsOverAPluginOfTheSameTypeName()
        {
            // The plugin lookup sits past the end of the registry walk, so it
            // is only ever reached for a name no source accounts for. A tree
            // that really does declare FB_Accumulate must run its own code.
            var real = new PouAst(
                "FB_Accumulate",
                null,
                "VAR_INPUT\n\tnAdd : INT;\nEND_VAR\nVAR_OUTPUT\n\tnTotal : INT;\nEND_VAR",
                "nTotal := 100;",
                new List<MethodAst>());

            var wrapper = new PouAst(
                "FB_Wrapper",
                null,
                "VAR\n\tfbAcc : FB_Accumulate;\n\tnSeen : INT;\nEND_VAR",
                "fbAcc(nAdd := 1);\nnSeen := fbAcc.nTotal;",
                new List<MethodAst>());

            var engine = new Engine(
                new TypeRegistry(new[] { real, wrapper }),
                new NativePlugins(null, RegistryWith(new AccumulatorBlock())));

            var instance = engine.NewInstance("FB_Wrapper");
            Step(engine, instance);

            Assert.Equal(100, instance.Fields["nSeen"].Value);
        }

        [Fact]
        public void Register_DuplicateTypeName_ThrowsNamingTheFirstSource()
        {
            var registry = new NativeFunctionBlockRegistry();
            registry.Register(new AccumulatorBlock(), "first.dll");

            var ex = Assert.Throws<InvalidOperationException>(
                () => registry.Register(new AccumulatorBlock(), "second.dll"));

            Assert.Contains("FB_Accumulate", ex.Message);
            Assert.Contains("first.dll", ex.Message);
            Assert.Contains("second.dll", ex.Message);
        }

        [Fact]
        public void Register_DuplicateDiffersOnlyInCasing_IsStillADuplicate()
        {
            var registry = new NativeFunctionBlockRegistry();
            registry.Register(new AccumulatorBlock());

            Assert.Throws<InvalidOperationException>(() => registry.Register(new RenamedBlock("fb_accumulate")));
        }

        [Fact]
        public void Register_BlankTypeName_IsRejectedAtRegistrationRatherThanAtUse()
        {
            var registry = new NativeFunctionBlockRegistry();

            Assert.Throws<ArgumentException>(() => registry.Register(new RenamedBlock("  ")));
        }

        [Fact]
        public void DescribeRegistrations_PairsEachTypeNameWithTheSourceThatClaimedIt()
        {
            var registry = new NativeFunctionBlockRegistry();
            registry.Register(new AccumulatorBlock(), "plugins.dll");

            Assert.Equal(new[] { "FB_Accumulate (plugins.dll)" }, registry.DescribeRegistrations());
        }

        private sealed class RenamedBlock : IXstunitNativeFunctionBlock
        {
            public RenamedBlock(string typeName)
            {
                TypeName = typeName;
            }

            public string TypeName { get; }

            public IReadOnlyList<NativeFieldDeclaration> Fields => Array.Empty<NativeFieldDeclaration>();

            public IReadOnlyList<string> PositionalInputNames => Array.Empty<string>();

            public IReadOnlyList<string> MethodNames => Array.Empty<string>();

            public IXstunitNativeFunctionBlock CreateInstance() => new RenamedBlock(TypeName);

            public object Invoke(NativeFunctionBlockCall call) => null;
        }
    }
}
