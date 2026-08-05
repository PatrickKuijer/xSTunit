using xStunit.Interpreter;
using xStunit.Runner;
using xStunit.Runner.TcUnitStub;
using Xunit;

namespace xStunit.Cli.Tests
{
    // The rule deciding what a consumer is told to DO about a failure, pinned
    // directly rather than by scraping a run's stdout. What breaks if these go
    // red is the half of every failure message that says "fix the ST" or "stop
    // and escalate" - opposite instructions that the factual half never
    // distinguishes.
    public class FailureGuidanceTests
    {
        // Reproduced byte for byte from upstream TcUnit's
        // FB_AdsAssertMessageFormatter. Named as a constant here, rather than
        // inlined, because the exemption below is about THIS string's shape.
        private const string VerbatimTcUnitAssertLine =
            "FAILED TEST 'CounterAdds', EXP: 3, ACT: 2, MSG: sum";

        private static readonly AssertSite Site = new AssertSite("FB_CounterTests", "CounterAdds", 42, 7);

        [Fact]
        public void For_TheFormattedTcUnitAssertLine_AppendsNothingAtAll()
        {
            var failure = new AssertionFailure(
                VerbatimTcUnitAssertLine, "AssertEquals_INT", "3", "2", "sum", Site);

            // The verbatim contract: this line goes on the wire unchanged, so
            // an appended sentence - however useful elsewhere - is a break.
            Assert.Equal(VerbatimTcUnitAssertLine, FailureGuidance.For(failure));
        }

        [Fact]
        public void For_AnAssertionKindFailureWithNoExpectedValue_StillGetsGuidance()
        {
            // Same KIND as the case above and nothing else in common: this one
            // never went near FB_AdsAssertMessageFormatter, so it carries no
            // Expected/Actual and has no verbatim contract to honour. An
            // exemption keyed on the kind would silently swallow the guidance
            // here, which is what this test exists to catch.
            var failure = AssertionFailure.Fault(
                "TEST_FINISHED() called with no TEST() open", FailureKind.Assertion, null, Site, null);

            Assert.Equal(
                "TEST_FINISHED() called with no TEST() open -- " + FailureKind.Guidance(FailureKind.Assertion),
                FailureGuidance.For(failure));
        }

        [Fact]
        public void For_AFaultChargedToATest_AppendsThatKindsGuidance()
        {
            var failure = AssertionFailure.Fault(
                "FB_Counter.Add(4): division by zero", FailureKind.PlcFault, null, Site, null);

            Assert.Equal(
                "FB_Counter.Add(4): division by zero -- " + FailureKind.Guidance(FailureKind.PlcFault),
                FailureGuidance.For(failure));
        }

        [Fact]
        public void For_AVerbatimMessage_IsReturnedUnchangedWhateverTheKind()
        {
            Assert.Equal(
                VerbatimTcUnitAssertLine,
                FailureGuidance.For(VerbatimTcUnitAssertLine, FailureKind.Assertion, isVerbatim: true));
        }

        [Fact]
        public void For_AnUnknownKind_LeavesTheMessageAlone()
        {
            // Guidance() answers null for a kind it doesn't know, and a missing
            // sentence must never turn a reported failure into a second one.
            Assert.Equal(
                "something broke",
                FailureGuidance.For("something broke", "not-a-kind", isVerbatim: false));
        }

        [Fact]
        public void For_AnEmptyMessage_IsTheGuidanceAloneWithNoLeadingDelimiter()
        {
            Assert.Equal(
                FailureKind.Guidance(FailureKind.LoadError),
                FailureGuidance.For(string.Empty, FailureKind.LoadError, isVerbatim: false));
        }

        [Fact]
        public void For_AParseError_SaysTheBodyCouldNotBeReadAndCitesTheLine()
        {
            var message = FailureGuidance.For(
                "Unexpected character '@' at position 33", FailureKind.ParseError, isVerbatim: false, bodyLine: 12);

            // Both halves matter: that the body could not be READ, and that the
            // cause is one of two things the runner cannot tell apart. Neither
            // may be asserted as the other.
            Assert.Equal(
                "Unexpected character '@' at position 33 -- xStunit could not read this body at line 12 - " +
                "either it uses ST beyond xStunit's subset, or it is invalid ST. " +
                FailureKind.Guidance(FailureKind.ParseError),
                message);
        }

        [Fact]
        public void For_AParseErrorWithNoKnownLine_OmitsTheLineClauseRatherThanCitingZero()
        {
            var message = FailureGuidance.For(
                "Unexpected end of input",
                FailureKind.ParseError,
                isVerbatim: false,
                bodyLine: PlcSourceLocationException.UnknownLine);

            Assert.Contains("could not read this body - either it uses ST", message);
            Assert.DoesNotContain("at line 0", message);
        }
    }
}
