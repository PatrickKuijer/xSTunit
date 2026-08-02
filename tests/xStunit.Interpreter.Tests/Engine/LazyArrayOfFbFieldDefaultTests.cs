using System.Collections.Generic;
using System.Linq;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // An ARRAY OF an FB type is constructed lazily as one whole array, only
    // once the field is dereferenced. Building every element eagerly would put
    // the entire element type's field graph - repeated per element - into the
    // cost and the risk of merely declaring the array, so one unsupported
    // construct several types away sinks a test that never indexes it.
    public class LazyArrayOfFbFieldDefaultTests
    {
        // aItems' open-array bound is unsupported: the bound parser is handed
        // the text "*" as an expression and throws the moment anything builds
        // this field's default.
        private static PouAst InnerFb() =>
            new PouAst(
                "FB_Inner",
                null,
                "VAR\n\taItems : ARRAY[*] OF INT;\nEND_VAR",
                "",
                new List<MethodAst>());

        private static PouAst FleetFb() =>
            new PouAst(
                "FB_Fleet",
                null,
                "VAR\n\taInners : ARRAY[1..3] OF FB_Inner;\n\taCounts : ARRAY[1..3] OF INT;\nEND_VAR",
                "",
                new List<MethodAst>());

        private static PouAst FleetTestsSuite() =>
            new PouAst(
                "FB_FleetTests",
                "TcUnit.FB_TestSuite",
                "",
                "M_SomeTest();",
                new List<MethodAst>
                {
                    new MethodAst(
                        "M_SomeTest",
                        "METHOD PRIVATE M_SomeTest\nVAR\n\tsfbFleet : FB_Fleet;\nEND_VAR",
                        "TEST('M_SomeTest');\nAssertTrue(TRUE, 'never reached');\nTEST_FINISHED();"),
                });

        // The observation that matters is that no element was constructed at
        // all: FB_Inner cannot be built, so the absence of a fault while
        // constructing FB_Fleet is proof the three elements were never made.
        [Fact]
        public void NewInstance_DoesNotEagerlyMaterializeArrayOfFbTypedField()
        {
            var engine = new Engine(new TypeRegistry(new[] { InnerFb(), FleetFb() }));

            var fleet = engine.NewInstance("FB_Fleet");

            Assert.IsType<LazyCell>(fleet.Fields["aInners"]);
        }

        // Deferral is scoped to element types that would recurse back into
        // instance construction; an ARRAY OF an elementary type costs O(n)
        // boxed zeroes and stays eager.
        [Fact]
        public void NewInstance_ArrayOfElementaryTypeStaysEager()
        {
            var engine = new Engine(new TypeRegistry(new[] { InnerFb(), FleetFb() }));

            var fleet = engine.NewInstance("FB_Fleet");

            Assert.IsNotType<LazyCell>(fleet.Fields["aCounts"]);
        }

        // Declaring an FB whose array-of-FB field is never indexed must cost
        // nothing, even when the element type's own field graph reaches a
        // construct the interpreter cannot build a default value for.
        [Fact]
        public void RunSuite_UnreadArrayOfBrokenFbField_TestStillPasses()
        {
            var engine = new Engine(new TypeRegistry(new[] { InnerFb(), FleetFb(), FleetTestsSuite() }));

            var results = engine.RunSuite("FB_FleetTests");

            var result = Assert.Single(results);
            Assert.True(result.Passed, result.ToString());
        }

        // Laziness defers the fault, it does not swallow it: reading the array
        // builds every element and still surfaces the failure.
        [Fact]
        public void ReadingTheArrayOfBrokenFbField_StillFaults()
        {
            var engine = new Engine(new TypeRegistry(new[] { InnerFb(), FleetFb() }));
            var fleet = engine.NewInstance("FB_Fleet");

            var ex = Assert.ThrowsAny<System.Exception>(() => _ = fleet.Fields["aInners"].Value);

            Assert.Contains("Asterisk", ex.Message);
        }

        // Deferring the whole array must not collapse it into one shared
        // element: each index still holds its own instance, with its own state.
        [Fact]
        public void ReadingTheArrayField_YieldsOneDistinctInstancePerElement()
        {
            var engine = new Engine(new TypeRegistry(new[] { CounterFb(), PoolFb() }));

            var pool = engine.NewInstance("FB_Pool");
            var counters = (ArrayValue)pool.Fields["aCounters"].Value;

            Assert.Equal(3, counters.Elements.Length);
            Assert.Equal(3, counters.Elements.Cast<FbInstance>().Distinct().Count());
        }

        // The materialized array is cached like any other LazyCell value, so
        // element state written after one read is still there on the next.
        [Fact]
        public void ReadingTheArrayFieldTwice_YieldsTheSameArray()
        {
            var engine = new Engine(new TypeRegistry(new[] { CounterFb(), PoolFb() }));
            var pool = engine.NewInstance("FB_Pool");

            Assert.Same(pool.Fields["aCounters"].Value, pool.Fields["aCounters"].Value);
        }

        private static PouAst CounterFb() =>
            new PouAst(
                "FB_Counter",
                null,
                "VAR\n\tnCount : INT;\nEND_VAR",
                "",
                new List<MethodAst>());

        private static PouAst PoolFb() =>
            new PouAst(
                "FB_Pool",
                null,
                "VAR\n\taCounters : ARRAY[1..3] OF FB_Counter;\nEND_VAR",
                "",
                new List<MethodAst>());
    }
}
