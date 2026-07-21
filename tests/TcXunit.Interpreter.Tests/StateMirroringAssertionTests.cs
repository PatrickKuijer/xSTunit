using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-w5x.15.9 / T6 design: AssertConverges/AssertConvergesAndLatches
    // own the master-then-proxy StepCycles(1) loop internally and throw a
    // ConvergenceAssertionException with a per-field diff on failure.
    public class StateMirroringAssertionTests
    {
        private static Engine NewEngine(params PouAst[] extraTypes)
        {
            var master = new PouAst(
                "FB_Master",
                null,
                "VAR\n\tValue : INT;\nEND_VAR",
                "",
                new List<MethodAst>());

            var proxyRamp = new PouAst(
                "FB_ProxyRamp",
                null,
                "VAR\n\tValue : INT;\nEND_VAR",
                "Value := Value + 1;",
                new List<MethodAst>());

            var types = new List<PouAst> { master, proxyRamp };
            types.AddRange(extraTypes);
            return new Engine(new TypeRegistry(types));
        }

        private static void AssertConverges(Engine engine, FbInstance suite, FbInstance master, FbInstance proxy, string[] fields, int maxCycles) =>
            Call(engine, "AssertConverges", suite, master, proxy, fields, maxCycles);

        private static void AssertConvergesAndLatches(Engine engine, FbInstance suite, FbInstance master, FbInstance proxy, string[] fields, int maxCycles) =>
            Call(engine, "AssertConvergesAndLatches", suite, master, proxy, fields, maxCycles);

        private static void Call(Engine engine, string methodName, FbInstance suite, FbInstance master, FbInstance proxy, string[] fields, int maxCycles)
        {
            var frame = new Frame(suite, suite.ActualTypeName);
            frame.Locals["master"] = new Cell { Value = master };
            frame.Locals["proxy"] = new Cell { Value = proxy };

            var fieldExprs = new List<Expr>();
            foreach (var f in fields)
                fieldExprs.Add(new StringLiteralExpr(f));

            engine.CallMethod(
                suite,
                methodName,
                new Expr[]
                {
                    new IdentifierExpr("master"),
                    new IdentifierExpr("proxy"),
                    new ArrayLiteralExpr(fieldExprs),
                    new IntLiteralExpr(maxCycles),
                },
                new NamedArg[0],
                frame,
                null);
        }

        [Fact]
        public void AssertConverges_Succeeds_WhenFieldsMatchWithinMaxCycles()
        {
            var engine = NewEngine();
            var suite = engine.NewInstance("FB_Master");
            var master = engine.NewInstance("FB_Master");
            var proxy = engine.NewInstance("FB_ProxyRamp");
            master.Fields["Value"].Value = 5;

            AssertConverges(engine, suite, master, proxy, new[] { "Value" }, 10);

            Assert.Equal(5, proxy.Fields["Value"].Value);
        }

        [Fact]
        public void AssertConverges_Throws_WithPerFieldDiff_WhenNeverConverges()
        {
            var engine = NewEngine();
            var suite = engine.NewInstance("FB_Master");
            var master = engine.NewInstance("FB_Master");
            var proxy = engine.NewInstance("FB_ProxyRamp");
            master.Fields["Value"].Value = 5;

            var ex = Assert.Throws<ConvergenceAssertionException>(
                () => AssertConverges(engine, suite, master, proxy, new[] { "Value" }, 2));

            Assert.Contains("Value: master=5, proxy=2", ex.Message);
        }

        [Fact]
        public void AssertConvergesAndLatches_Succeeds_WhenFieldFlipsOnceAndStays()
        {
            var clampedRamp = new PouAst(
                "FB_ProxyClampedRamp",
                null,
                "VAR\n\tValue : INT;\nEND_VAR",
                "IF Value < 3 THEN\n\tValue := Value + 1;\nEND_IF",
                new List<MethodAst>());

            var engine = NewEngine(clampedRamp);
            var suite = engine.NewInstance("FB_Master");
            var master = engine.NewInstance("FB_Master");
            var proxy = engine.NewInstance("FB_ProxyClampedRamp");
            master.Fields["Value"].Value = 3;

            AssertConvergesAndLatches(engine, suite, master, proxy, new[] { "Value" }, 5);

            Assert.Equal(3, proxy.Fields["Value"].Value);
        }

        [Fact]
        public void AssertConvergesAndLatches_Throws_WhenFieldDivergesAgainAfterLatching()
        {
            var oscillate = new PouAst(
                "FB_ProxyOscillate",
                null,
                "VAR\n\tValue : INT;\nEND_VAR",
                "IF Value < 3 THEN\n\tValue := Value + 1;\nELSE\n\tValue := Value - 1;\nEND_IF",
                new List<MethodAst>());

            var engine = NewEngine(oscillate);
            var suite = engine.NewInstance("FB_Master");
            var master = engine.NewInstance("FB_Master");
            var proxy = engine.NewInstance("FB_ProxyOscillate");
            master.Fields["Value"].Value = 3;

            var ex = Assert.Throws<ConvergenceAssertionException>(
                () => AssertConvergesAndLatches(engine, suite, master, proxy, new[] { "Value" }, 5));

            Assert.Contains("diverged again", ex.Message);
        }

        [Fact]
        public void AssertConvergesAndLatches_Throws_WhenNeverConverges()
        {
            var engine = NewEngine();
            var suite = engine.NewInstance("FB_Master");
            var master = engine.NewInstance("FB_Master");
            var proxy = engine.NewInstance("FB_ProxyRamp");
            master.Fields["Value"].Value = 5;

            var ex = Assert.Throws<ConvergenceAssertionException>(
                () => AssertConvergesAndLatches(engine, suite, master, proxy, new[] { "Value" }, 2));

            Assert.Contains("never converged", ex.Message);
        }
    }
}
