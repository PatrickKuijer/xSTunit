using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-6hg: TwinCAT ALIAS types (e.g. "TYPE T_MaxString :
    // STRING(255); END_TYPE") weren't resolved by the interpreter - a POU
    // declaring a var of an alias type fell through DefaultValue's checks
    // to the plain-scalar fallback (returning an int 0 instead of "" for a
    // STRING alias) because the alias name was never mapped to its
    // underlying type. TypeRegistry.ResolveAlias plus resolving through it
    // at every existing type-name lookup site fixes this without those
    // sites needing their own alias-aware branch.
    public class AliasTypeResolutionTests
    {
        private static Dictionary<string, string> Alias(string name, string underlying) =>
            new Dictionary<string, string> { [name] = underlying };

        [Fact]
        public void ResolveAlias_KnownAlias_ReturnsUnderlyingType()
        {
            var registry = new TypeRegistry(
                System.Array.Empty<PouAst>(), null, null, Alias("T_MaxString", "STRING(255)"));

            Assert.Equal("STRING(255)", registry.ResolveAlias("T_MaxString"));
        }

        [Fact]
        public void ResolveAlias_UnknownName_ReturnsInputUnchanged()
        {
            var registry = new TypeRegistry(System.Array.Empty<PouAst>());

            Assert.Equal("INT", registry.ResolveAlias("INT"));
        }

        [Fact]
        public void ResolveAlias_ChainedAlias_FollowsToFinalUnderlyingType()
        {
            var aliases = new Dictionary<string, string>
            {
                ["T_Inner"] = "INT",
                ["T_Outer"] = "T_Inner",
            };
            var registry = new TypeRegistry(System.Array.Empty<PouAst>(), null, null, aliases);

            Assert.Equal("INT", registry.ResolveAlias("T_Outer"));
        }

        // Alias to STRING(255): a VAR of this alias type must default to an
        // empty string, not the plain-scalar "0" fallback.
        [Fact]
        public void DefaultValue_VarOfAliasToSizedString_DefaultsToEmptyString()
        {
            var fb = new PouAst(
                "FB_Test",
                null,
                "VAR\n\tisStep : T_MaxString;\nEND_VAR",
                "",
                new List<MethodAst>());

            var registry = new TypeRegistry(
                new[] { fb }, null, null, Alias("T_MaxString", "STRING(255)"));
            var engine = new Engine(registry);

            var instance = engine.NewInstance("FB_Test");

            Assert.Equal("", instance.Fields["isStep"].Value);
        }

        // Alias to a scalar (INT): a VAR of this alias type must default to
        // 0 and behave like a plain INT for read/write.
        [Fact]
        public void DefaultValue_VarOfAliasToScalar_DefaultsToZeroAndIsAssignable()
        {
            var fb = new PouAst(
                "FB_Test",
                null,
                "VAR\n\tcounter : T_Counter;\nEND_VAR",
                "counter := counter + 1;",
                new List<MethodAst>());

            var registry = new TypeRegistry(
                new[] { fb }, null, null, Alias("T_Counter", "INT"));
            var engine = new Engine(registry);

            var instance = engine.NewInstance("FB_Test");
            Assert.Equal(0, instance.Fields["counter"].Value);

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);
            Assert.Equal(1, instance.Fields["counter"].Value);
        }

        // Alias used as a STRUCT field: the field must default the same way
        // a directly-declared STRING(255)/INT field would.
        [Fact]
        public void DefaultValue_StructFieldOfAliasType_ResolvesUnderlyingType()
        {
            var structAst = StructDeclParser.Parse(@"TYPE ST_Step :
STRUCT
	isStep : T_MaxString;
	count : T_Counter;
END_STRUCT
END_TYPE");

            var aliases = new Dictionary<string, string>
            {
                ["T_MaxString"] = "STRING(255)",
                ["T_Counter"] = "INT",
            };

            var fb = new PouAst(
                "FB_Test",
                null,
                "VAR\n\tstep : ST_Step;\nEND_VAR",
                "",
                new List<MethodAst>());

            var registry = new TypeRegistry(new[] { fb }, new[] { structAst }, null, aliases);
            var engine = new Engine(registry);

            var instance = engine.NewInstance("FB_Test");
            var step = Assert.IsType<StructInstance>(instance.Fields["step"].Value);

            Assert.Equal("", step.Fields["isStep"].Value);
            Assert.Equal(0, step.Fields["count"].Value);
        }

        [Fact]
        public void StructBoundaryBuilder_AliasToSizedString_BoundaryValuesUseUnderlyingLength()
        {
            var structAst = StructDeclParser.Parse(@"TYPE ST_Step :
STRUCT
	isStep : T_MaxString;
END_STRUCT
END_TYPE");

            var registry = new TypeRegistry(
                System.Array.Empty<PouAst>(), new[] { structAst }, null, Alias("T_MaxString", "STRING(255)"));
            var builder = new StructBoundaryBuilder(registry);

            var min = builder.Build("ST_Step", ("isStep", Boundary.Min));
            var max = builder.Build("ST_Step", ("isStep", Boundary.Max));

            Assert.Equal("", min.Fields["isStep"].Value);
            Assert.Equal(new string('X', 255), max.Fields["isStep"].Value);
        }

        [Fact]
        public void DefaultValue_ArrayOfAliasToSizedString_DefaultsEachElementToEmptyStringAndIsAssignable()
        {
            var fb = new PouAst(
                "FB_Test",
                null,
                "VAR\n\tsaLowBound : ARRAY[1..4] OF T_SampleValueString;\nEND_VAR",
                "saLowBound[1] := 'x';",
                new List<MethodAst>());

            var registry = new TypeRegistry(
                new[] { fb }, null, null, Alias("T_SampleValueString", "STRING(80)"));
            var engine = new Engine(registry);

            var instance = engine.NewInstance("FB_Test");
            var arr = Assert.IsType<ArrayValue>(instance.Fields["saLowBound"].Value);
            Assert.All(arr.Elements, e => Assert.Equal("", e));

            var frame = new Frame(instance, "FB_Test");
            engine.ExecuteStatements(Parser.ParseStatements(fb.ImplementationText), frame);

            Assert.Equal("x", arr.Elements[0]);
        }

        [Fact]
        public void StructBoundaryBuilder_AliasToScalar_BoundaryValuesUseUnderlyingBounds()
        {
            var structAst = StructDeclParser.Parse(@"TYPE ST_Step :
STRUCT
	count : T_Counter;
END_STRUCT
END_TYPE");

            var registry = new TypeRegistry(
                System.Array.Empty<PouAst>(), new[] { structAst }, null, Alias("T_Counter", "INT"));
            var builder = new StructBoundaryBuilder(registry);

            var min = builder.Build("ST_Step", ("count", Boundary.Min));
            var max = builder.Build("ST_Step", ("count", Boundary.Max));

            Assert.Equal(-32768, min.Fields["count"].Value);
            Assert.Equal(32767, max.Fields["count"].Value);
        }
    }
}
