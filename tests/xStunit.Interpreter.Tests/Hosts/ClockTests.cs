using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // Simulated time is a running total advanced explicitly, entirely separate
    // from StepCycles: a test can advance the clock without stepping, or step
    // without advancing.
    public class ClockTests
    {
        [Fact]
        public void AdvanceMs_AccumulatesAcrossMultipleCalls()
        {
            var engine = new Engine(new TypeRegistry(System.Array.Empty<PouAst>()));

            engine.Clock.AdvanceMs(500);
            engine.Clock.AdvanceMs(250);

            Assert.Equal(750, engine.Clock.TotalMs);
        }

        // ns is the base unit (LTON/LTOF/LTP count in it) and ms the derived
        // view, so both units accumulate into one total and TotalMs truncates
        // the sub-ms remainder rather than rounding it or tracking it apart.
        [Fact]
        public void AdvanceNs_AccumulatesIntoTheSameTotalAsAdvanceMs()
        {
            var engine = new Engine(new TypeRegistry(System.Array.Empty<PouAst>()));

            engine.Clock.AdvanceMs(2);
            engine.Clock.AdvanceNs(1_500);

            Assert.Equal(2_001_500, engine.Clock.TotalNs);
            Assert.Equal(2, engine.Clock.TotalMs);
        }
    }
}
