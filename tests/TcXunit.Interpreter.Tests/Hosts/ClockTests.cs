using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-w5x.15.7: Engine.Clock.Advance(dt) is a shared, monotonic,
    // process-wide running total - a separate primitive from StepCycles.
    public class ClockTests
    {
        [Fact]
        public void Advance_AccumulatesAcrossMultipleCalls()
        {
            var engine = new Engine(new TypeRegistry(System.Array.Empty<PouAst>()));

            engine.Clock.Advance(500);
            engine.Clock.Advance(250);

            Assert.Equal(750, engine.Clock.TotalMs);
        }

        // TcXunit-x5pt: ns is the clock's base unit (the LTIME timers count in
        // ns), and ms is the derived view - so the two units accumulate into
        // the same total and TotalMs truncates any sub-ms remainder rather
        // than rounding or tracking it separately.
        [Fact]
        public void AdvanceNs_AccumulatesIntoTheSameTotalAsAdvance()
        {
            var engine = new Engine(new TypeRegistry(System.Array.Empty<PouAst>()));

            engine.Clock.Advance(2);
            engine.Clock.AdvanceNs(1_500);

            Assert.Equal(2_001_500, engine.Clock.TotalNs);
            Assert.Equal(2, engine.Clock.TotalMs);
        }
    }
}
