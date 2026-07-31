using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // An FB-typed field is constructed lazily, only once it is dereferenced.
    // Materializing a whole field graph eagerly would mean one unsupported
    // construct several types away - reachable from a declared local that no
    // test ever reads - failing the entire call.
    public class LazyFbFieldDefaultTests
    {
        // aItems' open-array bound is unsupported: the bound parser is handed
        // the text "*" as an expression and throws the moment anything tries to
        // build this field's default.
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

        // Laziness defers the fault, it does not swallow it: code that
        // genuinely dereferences the field still gets the failure.
        [Fact]
        public void DirectlyReadingTheUnreachableField_StillFaults()
        {
            var registry = new TypeRegistry(new[] { InnerFb(), MiddleFb() });
            var engine = new Engine(registry);

            var ex = Assert.ThrowsAny<System.Exception>(() =>
            {
                var middle = engine.NewInstance("FB_Middle");
                // Reading a cell's Value is what forces the deferred
                // construction, one level at a time.
                var inner = ((FbInstance)middle.Fields["sfbInner"].Value);
                _ = inner.Fields["aItems"].Value;
            });

            Assert.Contains("Asterisk", ex.Message);
        }

        // Declaring a METHOD-local FB var and never reading it must cost
        // nothing, even when its field graph reaches a construct the
        // interpreter cannot build a default value for.
        [Fact]
        public void RunSuite_MethodLocalFbVarWithUnreachableBrokenField_TestStillPasses()
        {
            var registry = new TypeRegistry(new[] { InnerFb(), MiddleFb(), WidgetPairTestsSuite() });
            var engine = new Engine(registry);

            var results = engine.RunSuite("FB_WidgetPairTests");

            var result = Assert.Single(results);
            Assert.True(result.Passed, result.ToString());
        }

        // Merely loading a POU that contains an unsupported construct must not
        // change the outcome of suites that never reference it - otherwise one
        // bad file in a directory can taint an entire run.
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

        [Fact]
        public void NewInstance_DoesNotEagerlyMaterializeNestedFbTypedField()
        {
            var registry = new TypeRegistry(new[] { InnerFb(), MiddleFb() });
            var engine = new Engine(registry);

            // Constructing the outer instance must not throw: sfbInner's own
            // field graph is broken, but nothing reads it here.
            var middle = engine.NewInstance("FB_Middle");

            Assert.IsType<LazyCell>(middle.Fields["sfbInner"]);
        }
    }
}
