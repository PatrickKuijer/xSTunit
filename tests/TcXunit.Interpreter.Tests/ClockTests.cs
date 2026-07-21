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
    }
}
