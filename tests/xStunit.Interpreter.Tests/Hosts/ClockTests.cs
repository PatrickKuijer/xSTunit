using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcXunit-w5x.15.7: Engine.Clock.AdvanceMs(dt) is a shared, monotonic,
    // process-wide running total - a separate primitive from StepCycles.
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

        // TcXunit-x5pt: ns is the clock's base unit (the LTIME timers count in
        // ns), and ms is the derived view - so the two units accumulate into
        // the same total and TotalMs truncates any sub-ms remainder rather
        // than rounding or tracking it separately.
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
