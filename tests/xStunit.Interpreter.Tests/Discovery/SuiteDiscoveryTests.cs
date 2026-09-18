using System.Collections.Generic;
using System.IO;
using System.Linq;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    public class SuiteDiscoveryTests
    {
        private static readonly string FixturePouDir = TestFixtures.FbCounterFixtureDir();

        [Fact]
        public void FindSuiteTypeNames_FixtureProject_FindsOnlyFbCounterTests()
        {
            var files = Directory.GetFiles(FixturePouDir, "*.TcPOU");
            var types = files.Select(f => TcPouParser.Parse(File.ReadAllText(f))).ToList();
            var registry = new TypeRegistry(types);

            var suites = SuiteDiscovery.FindSuiteTypeNames(registry, types.Select(t => t.Name));

            Assert.Equal(new[] { "FB_CounterTests" }, suites);
        }

        // FB_TestSuiteWithClock is a user-defined intermediate base (EXTENDS
        // TcUnit.FB_TestSuite directly), registered under its bare name. A
        // descendant that spells the EXTENDS clause with a library qualifier
        // - as real TcUnit-flavored source does - must still resolve through
        // it to the native root, not dead-end at the first ancestry step.
        [Fact]
        public void IsSuiteType_QualifiedExtendsOfUserDefinedIntermediateBase_ResolvesToNativeRoot()
        {
            var intermediateBase = new PouAst(
                "FB_TestSuiteWithClock", "TcUnit.FB_TestSuite", "", "", new List<MethodAst>());
            var descendant = new PouAst(
                "FB_ClockSuiteRepro", "TcUnit.FB_TestSuiteWithClock", "", "", new List<MethodAst>());
            var registry = new TypeRegistry(new[] { intermediateBase, descendant });

            Assert.True(SuiteDiscovery.IsSuiteType(registry, "FB_ClockSuiteRepro"));
        }

        // Real TcUnit-flavored source is not consistent about the
        // qualifier: an intermediate base's own EXTENDS clause can spell the
        // native root bare (EXTENDS FB_TestSuite) rather than library-
        // qualified. The terminal match has to tolerate that spelling too,
        // not just the canonical "TcUnit.FB_TestSuite".
        [Fact]
        public void IsSuiteType_UnqualifiedExtendsOfNativeRoot_ResolvesToNativeRoot()
        {
            var intermediateBase = new PouAst(
                "FB_TestSuiteWithClock", "FB_TestSuite", "", "", new List<MethodAst>());
            var descendant = new PouAst(
                "FB_ClockSuiteRepro", "FB_TestSuiteWithClock", "", "", new List<MethodAst>());
            var registry = new TypeRegistry(new[] { intermediateBase, descendant });

            Assert.True(SuiteDiscovery.IsSuiteType(registry, "FB_ClockSuiteRepro"));
        }
    }
}
