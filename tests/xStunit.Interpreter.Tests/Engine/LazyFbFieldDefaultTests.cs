using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcXunit-mxx: Engine.NewInstance's Fields-materialization loop used to
    // call DefaultValue for every declared field of an FB type eagerly -
    // including every FB-typed field's own fields, transitively, with no
    // regard for whether the calling code ever reads that field. Declaring
    // a METHOD-local VAR of an FB type whose field graph contains an
    // unsupported construct several types away (e.g. an ARRAY[*] open-array
    // VAR_IN_OUT bound, which ResolveArrayBound evaluates by literally
    // parsing the bound text "*" as an expression) used to fail the whole
    // call, even when the test never touched that field. These tests pin
    // the fix: FB-typed field construction is deferred (LazyCell) until the
    // field is actually dereferenced, so an unused/unreachable field's
    // construction fault never surfaces.
    public class LazyFbFieldDefaultTests
    {
        // FB_Inner has one field, aItems, whose declared type
        // (ARRAY[*] OF INT - an open-array bound) is unsupported by
        // ArrayTypeInfo's bound parser: constructing its default value
        // throws "Unexpected token Asterisk:* at index 0" the moment
        // something tries to build it.
        private static PouAst InnerFb() =>
            new PouAst(
                "FB_Inner",
                null,
                "VAR\n\taItems : ARRAY[*] OF INT;\nEND_VAR",
                "",
                new List<MethodAst>());

        private static PouAst MiddleFb() =>
            new PouAst(
                "FB_Middle",
                null,
                "VAR\n\tsfbInner : FB_Inner;\nEND_VAR",
                "",
                new List<MethodAst>());

        private static PouAst WidgetPairTestsSuite() =>
            new PouAst(
                "FB_WidgetPairTests",
                "TcUnit.FB_TestSuite",
                "",
                "M_SomeTest();",
                new List<MethodAst>
                {
                    new MethodAst(
                        "M_SomeTest",
                        "METHOD PRIVATE M_SomeTest\nVAR\n\tsfbMiddle : FB_Middle;\nEND_VAR",
                        "TEST('M_SomeTest');\nAssertTrue(TRUE, 'never reached');\nTEST_FINISHED();"),
                });

        // Sanity check the fixture actually reproduces the reported crash
        // pre-fix: directly forcing construction of the unreachable field
        // (by reading through the full sfbMiddle.sfbInner.aItems chain) must
        // still fail - the fix defers the fault, it doesn't hide it from
        // code that genuinely dereferences the field.
        [Fact]
        public void DirectlyReadingTheUnreachableField_StillFaults()
        {
            var registry = new TypeRegistry(new[] { InnerFb(), MiddleFb() });
            var engine = new Engine(registry);

            var ex = Assert.ThrowsAny<System.Exception>(() =>
            {
                var middle = engine.NewInstance("FB_Middle");
                // Force materialization of the lazily-deferred nested field.
                var inner = ((FbInstance)middle.Fields["sfbInner"].Value);
                // Force materialization of its own unsupported array field.
                _ = inner.Fields["aItems"].Value;
            });

            Assert.Contains("Asterisk", ex.Message);
        }

        // TcXunit-mxx core regression: a suite whose test declares a
        // METHOD-local FB-typed var (sfbMiddle) that is NEVER read must run
        // to completion and pass, even though sfbMiddle's field graph
        // (sfbMiddle.sfbInner.aItems) contains a construct the interpreter
        // can't build a default value for.
        [Fact]
        public void RunSuite_MethodLocalFbVarWithUnreachableBrokenField_TestStillPasses()
        {
            var registry = new TypeRegistry(new[] { InnerFb(), MiddleFb(), WidgetPairTestsSuite() });
            var engine = new Engine(registry);

            var results = engine.RunSuite("FB_WidgetPairTests");

            var result = Assert.Single(results);
            Assert.True(result.Passed, result.ToString());
        }

        // Acceptance criterion 2: adding a completely unrelated POU (with
        // an unsupported construct in a field no test touches) to the
        // loaded registry must not change an unrelated suite's outcome.
        // Compares the same simple, unrelated suite's result with and
        // without FB_Inner/FB_Middle present in the registry.
        [Fact]
        public void RunSuite_UnrelatedSuite_UnaffectedByAddingOrRemovingBrokenUnusedPou()
        {
            var unrelatedSuite = new PouAst(
                "FB_UnrelatedSuite",
                "TcUnit.FB_TestSuite",
                "",
                "TEST('t');\nAssertTrue(TRUE, 'ok');\nTEST_FINISHED();",
                new List<MethodAst>());

            var withoutUnrelatedPous = new Engine(new TypeRegistry(new[] { unrelatedSuite }));
            var withUnrelatedPous = new Engine(new TypeRegistry(new[] { InnerFb(), MiddleFb(), unrelatedSuite }));

            var before = Assert.Single(withoutUnrelatedPous.RunSuite("FB_UnrelatedSuite"));
            var after = Assert.Single(withUnrelatedPous.RunSuite("FB_UnrelatedSuite"));

            Assert.True(before.Passed);
            Assert.True(after.Passed);
            Assert.Equal(before.Passed, after.Passed);
            Assert.Equal(before.Failures.Count, after.Failures.Count);
        }

        // Declaring the local (NewInstance("FB_Middle") via the suite/
        // METHOD path) must not itself force construction of the nested
        // sfbInner field - only actually reading it should.
        [Fact]
        public void NewInstance_DoesNotEagerlyMaterializeNestedFbTypedField()
        {
            var registry = new TypeRegistry(new[] { InnerFb(), MiddleFb() });
            var engine = new Engine(registry);

            // Must not throw even though FB_Inner's own field graph is
            // broken - sfbInner is never read here.
            var middle = engine.NewInstance("FB_Middle");

            Assert.IsType<LazyCell>(middle.Fields["sfbInner"]);
        }
    }
}
