using System.Collections.Generic;
using System.Linq;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcXunit-3tx.4: not a coverage percentage or a CI badge - a next-task list
    // for an agent. "F_ComputeChecksum: (none)" is directly usable as a prompt.
    public class SuiteCoverageTests
    {
        [Fact]
        public void Analyze_PouReferencedBySuiteBody_IsCoveredByThatSuite()
        {
            var coverage = Analyze(
                Pou("FB_Counter"),
                Suite("FB_CounterTests", "VAR\n\tcounter : FB_Counter;\nEND_VAR", "CounterStartsAtZero();"));

            var counter = Single(coverage, "FB_Counter");
            Assert.Equal(new[] { "FB_CounterTests" }, counter.SuiteTypeNames.ToArray());
            Assert.True(counter.IsCovered);
        }

        [Fact]
        public void Analyze_PouNoSuiteMentions_IsReportedUncovered()
        {
            var coverage = Analyze(
                Pou("F_ComputeChecksum"),
                Suite("FB_CounterTests", "VAR\n\tcounter : FB_Counter;\nEND_VAR", "CounterStartsAtZero();"));

            var checksum = Single(coverage, "F_ComputeChecksum");
            Assert.Empty(checksum.SuiteTypeNames);
            Assert.False(checksum.IsCovered);
        }

        // A reference from a test METHOD's body counts too - that is where most
        // real suites exercise the code under test, not the suite's own body.
        [Fact]
        public void Analyze_PouReferencedOnlyFromATestMethodBody_IsCovered()
        {
            var suite = new PouAst(
                "FB_WidgetTests",
                "TcUnit.FB_TestSuite",
                "FUNCTION_BLOCK FB_WidgetTests EXTENDS TcUnit.FB_TestSuite",
                "ChecksumWorks();",
                new List<MethodAst>
                {
                    new MethodAst("ChecksumWorks", "METHOD PRIVATE ChecksumWorks", "nResult := F_ComputeChecksum(1);"),
                });

            var coverage = Analyze(Pou("F_ComputeChecksum"), suite);

            Assert.True(Single(coverage, "F_ComputeChecksum").IsCovered);
        }

        // IEC 61131-3 identifiers are case-insensitive, and so is every other
        // type lookup in this interpreter.
        [Fact]
        public void Analyze_ReferenceDifferingOnlyInCase_StillCounts()
        {
            var coverage = Analyze(
                Pou("FB_Counter"),
                Suite("FB_CounterTests", "VAR\n\tcounter : fb_counter;\nEND_VAR", ""));

            Assert.True(Single(coverage, "FB_Counter").IsCovered);
        }

        // "FB_Counter" must not be matched inside "FB_CounterExtended" - a
        // substring hit would silently report a POU as covered by a suite that
        // never mentions it.
        [Fact]
        public void Analyze_NameAppearingOnlyAsASubstringOfALongerName_DoesNotCount()
        {
            var coverage = Analyze(
                new[] { Pou("FB_Counter"), Pou("FB_CounterExtended") },
                Suite("FB_WidgetTests", "VAR\n\tx : FB_CounterExtended;\nEND_VAR", ""));

            Assert.False(Single(coverage, "FB_Counter").IsCovered);
            Assert.True(Single(coverage, "FB_CounterExtended").IsCovered);
        }

        // TcXunit-2o9.2: a type named only inside a (* ... *) comment is not a
        // real reference - counting it as covered would hide a POU that no
        // suite actually exercises.
        [Fact]
        public void Analyze_PouMentionedOnlyInABlockComment_IsNotCovered()
        {
            var coverage = Analyze(
                Pou("FB_Counter"),
                Suite("FB_WidgetTests", "(* uses FB_Counter internally *)\nVAR\nEND_VAR", ""));

            var counter = Single(coverage, "FB_Counter");
            Assert.Empty(counter.SuiteTypeNames);
            Assert.False(counter.IsCovered);
        }

        // Same as above but a // line comment, and in the implementation
        // text rather than the declaration.
        [Fact]
        public void Analyze_PouMentionedOnlyInALineComment_IsNotCovered()
        {
            var coverage = Analyze(
                Pou("FB_Counter"),
                Suite("FB_WidgetTests", "VAR\nEND_VAR", "// TODO: test FB_Counter\n"));

            Assert.False(Single(coverage, "FB_Counter").IsCovered);
        }

        // Regression guard: a genuine VAR declaration reference must still
        // count as covered even when the same suite ALSO mentions the type
        // in a comment - the comment-stripping fix must not eat real code.
        [Fact]
        public void Analyze_PouReferencedInVarDeclAndMentionedInComment_IsStillCovered()
        {
            var coverage = Analyze(
                Pou("FB_Counter"),
                Suite(
                    "FB_CounterTests",
                    "(* FB_Counter is the type under test *)\nVAR\n\tcounter : FB_Counter;\nEND_VAR",
                    "CounterStartsAtZero(); // exercises FB_Counter"));

            var counter = Single(coverage, "FB_Counter");
            Assert.Equal(new[] { "FB_CounterTests" }, counter.SuiteTypeNames.ToArray());
            Assert.True(counter.IsCovered);
        }

        // The suites are the test code, not the code under test - listing them
        // as uncovered POUs would make the work list mostly noise.
        [Fact]
        public void Analyze_SuitesThemselves_AreNotListedAsCoverableTargets()
        {
            var coverage = Analyze(
                Pou("FB_Counter"),
                Suite("FB_CounterTests", "VAR\n\tcounter : FB_Counter;\nEND_VAR", ""));

            Assert.DoesNotContain(coverage, c => c.PouTypeName == "FB_CounterTests");
        }

        [Fact]
        public void Analyze_PouReferencedByTwoSuites_ListsBothInDiscoveryOrder()
        {
            var coverage = Analyze(
                new[] { Pou("FB_Counter") },
                Suite("FB_ATests", "VAR\n\ta : FB_Counter;\nEND_VAR", ""),
                Suite("FB_BTests", "VAR\n\tb : FB_Counter;\nEND_VAR", ""));

            Assert.Equal(new[] { "FB_ATests", "FB_BTests" }, Single(coverage, "FB_Counter").SuiteTypeNames.ToArray());
        }

        private static PouCoverage Single(IReadOnlyList<PouCoverage> coverage, string pouTypeName) =>
            coverage.Single(c => c.PouTypeName == pouTypeName);

        private static IReadOnlyList<PouCoverage> Analyze(PouAst pou, params PouAst[] suites) =>
            Analyze(new[] { pou }, suites);

        private static IReadOnlyList<PouCoverage> Analyze(PouAst[] pous, params PouAst[] suites) =>
            SuiteCoverage.Analyze(pous.Concat(suites).ToList(), suites.Select(s => s.Name).ToList());

        private static PouAst Pou(string name) =>
            new PouAst(name, null, $"FUNCTION_BLOCK {name}", "", new List<MethodAst>());

        private static PouAst Suite(string name, string declBody, string implementation) =>
            new PouAst(
                name,
                "TcUnit.FB_TestSuite",
                $"FUNCTION_BLOCK {name} EXTENDS TcUnit.FB_TestSuite\n{declBody}",
                implementation,
                new List<MethodAst>());
    }
}
