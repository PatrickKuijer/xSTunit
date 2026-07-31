using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A PROPERTY is not a field: dot-access has to run its Get/Set accessor
    // body, so nothing about it can be answered from FbInstance.Fields.
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
            // A Get accessor's implicit local is seeded from the declared
            // property type, exactly as a method's return cell is. Left to be
            // created by its first assignment, 'nGain := 0.0;' would fix the
            // cell as REAL and reject the later LREAL as implicit narrowing.
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
            // The Set accessor's incoming value must carry the property's
            // declared type on its cell, not just a boxed value: SIZEOF reads a
            // bare identifier's size from exactly that tag. Untagged,
            // SIZEOF(nGain) falls back to treating "nGain" as a type name and
            // throws instead of reporting LREAL's 8 bytes.
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
