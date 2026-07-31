using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcXunit-sxv: TwinCAT PROPERTY (Get/Set) members were dropped entirely
    // during POU loading - TcPouParser never looked for <Property> elements,
    // so FbInstance.Fields never gained an entry for one and dot-access threw
    // "Unknown field" instead of running the property's Get accessor.
    public class PropertyAccessorTests
    {
        [Fact]
        public void DotAccess_ReadsGetOnlyProperty_RunsGetAccessorInsteadOfThrowing()
        {
            var foo = new PouAst(
                "FB_Foo",
                null,
                "VAR\n\tsnCounter : UINT;\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    new MethodAst("bBar", "METHOD bBar : BOOL", "snCounter := snCounter + 1;\nbBar := TRUE;"),
                },
                new List<PropertyAst>
                {
                    new PropertyAst("nCounter", "PROPERTY nCounter : UINT", "nCounter := snCounter;", null),
                });

            var engine = new Engine(new TypeRegistry(new[] { foo }));
            var instance = engine.NewInstance("FB_Foo");

            engine.CallMethod(instance, "bBar", Array.Empty<Expr>(), Array.Empty<NamedArg>(), null, null);

            var result = engine.Evaluate(
                new FieldAccessExpr(new IdentifierExpr("sfbFoo"), "nCounter"),
                new Frame(WrapAsField(instance), null));

            Assert.Equal(1, result);
        }

        [Fact]
        public void DotAccess_WriteThroughSetProperty_RunsSetAccessorInsteadOfThrowing()
        {
            var foo = new PouAst(
                "FB_Foo",
                null,
                "VAR\n\tsnCounter : UINT;\nEND_VAR",
                "",
                new List<MethodAst>(),
                new List<PropertyAst>
                {
                    new PropertyAst("nCounter", "PROPERTY nCounter : UINT", null, "snCounter := nCounter;"),
                });

            var outer = new PouAst(
                "FB_Outer",
                null,
                "VAR\n\tsfbFoo : FB_Foo;\nEND_VAR",
                "sfbFoo.nCounter := 7;",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { foo, outer }));
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, Array.Empty<NamedArg>(), null, null);

            var nested = (FbInstance)instance.Fields["sfbFoo"].Value;
            Assert.Equal(7, nested.Fields["snCounter"].Value);
        }

        [Fact]
        public void DotAccess_ReadsLrealProperty_SeededByRealLiteralThenAssignedLreal_ReturnsTheLreal()
        {
            // TcXunit-8we: same defect as TcXunit-cq6, but at the PROPERTY Get
            // accessor site. InvokePropertyGet used to leave the Local named
            // after the property to be created lazily by its first
            // assignment, so 'nGain := 0.0;' (a bare decimal literal, which
            // lexes as REAL) made the cell a REAL, and the later LREAL
            // assignment was rejected as an implicit narrowing.
            var foo = new PouAst(
                "FB_Foo",
                null,
                "VAR\n\tsfGain : LREAL;\nEND_VAR",
                "",
                new List<MethodAst>(),
                new List<PropertyAst>
                {
                    new PropertyAst(
                        "nGain",
                        "PROPERTY nGain : LREAL",
                        "nGain := 0.0;\nnGain := sfGain;",
                        null),
                });

            var engine = new Engine(new TypeRegistry(new[] { foo }));
            var instance = engine.NewInstance("FB_Foo");
            instance.Fields["sfGain"].Value = 2.5d;

            var result = engine.Evaluate(
                new FieldAccessExpr(new IdentifierExpr("sfbFoo"), "nGain"),
                new Frame(WrapAsField(instance), null));

            Assert.Equal(2.5d, result);
        }

        [Fact]
        public void DotAccess_WriteThroughSetLrealProperty_SeededCellTaggedWithDeclaredType()
        {
            // TcXunit-8we: InvokePropertySet seeded 'new Cell { Value = value
            // }' with no DeclaredTypeName - same class of problem as the Get
            // side, and it left the cell untagged for anything reading
            // Cell.DeclaredTypeName. SIZEOF resolves a bare identifier's size
            // through exactly that tag (Engine.SizeOf's
            // ResolveDeclaredTypeName, via Frame.LocalTypeNames) - untagged,
            // SIZEOF(nGain) falls back to treating "nGain" itself as a type
            // name and throws; tagged, it reports LREAL's 8 bytes.
            var foo = new PouAst(
                "FB_Foo",
                null,
                "VAR\n\tsnSize : UDINT;\nEND_VAR",
                "",
                new List<MethodAst>(),
                new List<PropertyAst>
                {
                    new PropertyAst(
                        "nGain",
                        "PROPERTY nGain : LREAL",
                        null,
                        "snSize := SIZEOF(nGain);"),
                });

            var outer = new PouAst(
                "FB_Outer",
                null,
                "VAR\n\tsfbFoo : FB_Foo;\nEND_VAR",
                "sfbFoo.nGain := 1.25;",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { foo, outer }));
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, Array.Empty<NamedArg>(), null, null);

            var nested = (FbInstance)instance.Fields["sfbFoo"].Value;
            Assert.Equal(8L, nested.Fields["snSize"].Value);
        }

        [Fact]
        public void DotAccess_ReadingSetOnlyProperty_ThrowsDescriptiveError()
        {
            var foo = new PouAst(
                "FB_Foo",
                null,
                "VAR\n\tsnCounter : UINT;\nEND_VAR",
                "",
                new List<MethodAst>(),
                new List<PropertyAst>
                {
                    new PropertyAst("nCounter", "PROPERTY nCounter : UINT", null, "snCounter := nCounter;"),
                });

            var engine = new Engine(new TypeRegistry(new[] { foo }));
            var instance = engine.NewInstance("FB_Foo");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                engine.Evaluate(
                    new FieldAccessExpr(new IdentifierExpr("sfbFoo"), "nCounter"),
                    new Frame(WrapAsField(instance), null)));

            Assert.Contains("nCounter", ex.Message);
            Assert.Contains("Get", ex.Message);
        }

        // Wraps instance as the sole field of a throwaway holder frame so a
        // bare FieldAccessExpr(sfbFoo.nCounter) can be evaluated directly
        // against it without needing a full outer POU/method body.
        private static FbInstance WrapAsField(FbInstance instance)
        {
            var holder = new FbInstance("FB_Holder");
            holder.Fields["sfbFoo"] = new Cell { Value = instance };
            return holder;
        }
    }
}
