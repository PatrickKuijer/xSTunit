using System;
using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
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
