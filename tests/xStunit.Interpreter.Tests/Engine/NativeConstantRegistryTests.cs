using System;
using System.Collections.Generic;
using xStunit.Interpreter.Extensibility;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // Host-supplied stand-ins for the named values a compiled-only library
    // publishes from its GVLs and ENUMs. Without these, supplying a library's
    // functions and blocks only half-resolves it: source written the way real
    // source is written - nMode := FOPEN_MODEREAD OR FOPEN_MODEBINARY - still
    // dies on an unresolved identifier.
    public class NativeConstantRegistryTests
    {
        private static Engine EngineWith(string declarations, string body, params NativeConstant[] constants)
        {
            var pou = new PouAst("FB_Widget", null, declarations, body, new List<MethodAst>());

            var registry = new NativeConstantRegistry();
            registry.RegisterAll(constants);

            return new Engine(new TypeRegistry(new[] { pou }), new NativePlugins(null, null, registry));
        }

        private static void Step(Engine engine, FbInstance instance) =>
            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

        [Fact]
        public void BareIdentifier_ResolvesToTheRegisteredConstant()
        {
            var engine = EngineWith(
                "VAR\n\tnMode : DWORD;\nEND_VAR",
                "nMode := FOPEN_MODEREAD;",
                new NativeConstant("FOPEN_MODEREAD", 1L));

            var instance = engine.NewInstance("FB_Widget");
            Step(engine, instance);

            Assert.Equal(1L, instance.Fields["nMode"].Value);
        }

        [Fact]
        public void Constants_CombineInAnExpressionLikeAnyOtherValue()
        {
            // The shape every FB_FileOpen call in real source has. A constant
            // that resolved but carried the wrong CLR width would fail here
            // rather than at the declaration.
            var engine = EngineWith(
                "VAR\n\tnMode : DWORD;\nEND_VAR",
                "nMode := FOPEN_MODEREAD OR FOPEN_MODEBINARY;",
                new NativeConstant("FOPEN_MODEREAD", 1L),
                new NativeConstant("FOPEN_MODEBINARY", 16L));

            var instance = engine.NewInstance("FB_Widget");
            Step(engine, instance);

            Assert.Equal(17L, instance.Fields["nMode"].Value);
        }

        [Fact]
        public void EnumMember_ResolvesQualifiedAndBare()
        {
            // TwinCAT accepts both spellings and real source uses both, so one
            // registration has to answer to each.
            var engine = EngineWith(
                "VAR\n\tnQualified : INT;\n\tnBare : INT;\nEND_VAR",
                "nQualified := E_OpenPath.PATH_GENERIC;\nnBare := PATH_GENERIC;",
                new NativeConstant("PATH_GENERIC", 1, "E_OpenPath"));

            var instance = engine.NewInstance("FB_Widget");
            Step(engine, instance);

            Assert.Equal(1, instance.Fields["nQualified"].Value);
            Assert.Equal(1, instance.Fields["nBare"].Value);
        }

        [Fact]
        public void Lookup_IsCaseInsensitive()
        {
            var engine = EngineWith(
                "VAR\n\tnMode : DWORD;\nEND_VAR",
                "nMode := fopen_moderead;",
                new NativeConstant("FOPEN_MODEREAD", 1L));

            var instance = engine.NewInstance("FB_Widget");
            Step(engine, instance);

            Assert.Equal(1L, instance.Fields["nMode"].Value);
        }

        [Fact]
        public void ARealVariable_WinsOverAConstantOfTheSameName()
        {
            // The precedence the whole extension point rests on: constants are
            // consulted only after every real scope has missed, so a plugin can
            // never shadow a variable that exists. A tree declaring its own
            // FOPEN_MODEREAD must use its own.
            var engine = EngineWith(
                "VAR\n\tFOPEN_MODEREAD : DWORD := 99;\n\tnMode : DWORD;\nEND_VAR",
                "nMode := FOPEN_MODEREAD;",
                new NativeConstant("FOPEN_MODEREAD", 1L));

            var instance = engine.NewInstance("FB_Widget");
            Step(engine, instance);

            Assert.Equal(99L, instance.Fields["nMode"].Value);
        }

        [Fact]
        public void UnknownIdentifier_StillFails_AndPointsAtThePluginSurface()
        {
            var engine = EngineWith(
                "VAR\n\tnMode : DWORD;\nEND_VAR",
                "nMode := FOPEN_MODEWRITE;",
                new NativeConstant("FOPEN_MODEREAD", 1L));

            var instance = engine.NewInstance("FB_Widget");

            var ex = Assert.ThrowsAny<Exception>(() => Step(engine, instance));
            Assert.Contains("FOPEN_MODEWRITE", ex.ToString());
            Assert.Contains("native-constant plugin", ex.ToString());
        }

        [Fact]
        public void Register_DuplicateName_ThrowsNamingTheFirstSource()
        {
            var registry = new NativeConstantRegistry();
            registry.Register(new NativeConstant("FOPEN_MODEREAD", 1L), "first.dll");

            var ex = Assert.Throws<InvalidOperationException>(
                () => registry.Register(new NativeConstant("FOPEN_MODEREAD", 2L), "second.dll"));

            Assert.Contains("FOPEN_MODEREAD", ex.Message);
            Assert.Contains("first.dll", ex.Message);
        }

        [Fact]
        public void Register_DuplicateQualifiedSpelling_IsAlsoAClash()
        {
            // The qualified spelling is a registration in its own right, so two
            // providers publishing E_OpenPath.PATH_GENERIC collide even if some
            // later change stopped the bare names from doing so.
            var registry = new NativeConstantRegistry();
            registry.Register(new NativeConstant("PATH_GENERIC", 1, "E_OpenPath"), "first.dll");

            var ex = Assert.Throws<InvalidOperationException>(
                () => registry.Register(new NativeConstant("PATH_GENERIC", 2, "E_OpenPath"), "second.dll"));

            Assert.Contains("PATH_GENERIC", ex.Message);
        }

        [Fact]
        public void Register_BlankName_IsRejectedAtRegistrationRatherThanAtUse()
        {
            Assert.Throws<ArgumentException>(() => new NativeConstant("  ", 1L));
        }
    }
}
