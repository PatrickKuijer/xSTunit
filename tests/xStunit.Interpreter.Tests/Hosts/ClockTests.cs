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

        // The absolute view exists for the library functions that answer "what
        // time is it?" rather than "how long since?". Its origin is a fixed
        // constant rather than the machine's clock at construction, because a
        // suite asserting an absolute timestamp has to be able to know the
        // answer - which is the entire reason those functions read this clock.
        [Fact]
        public void UtcNow_IsTheOriginPlusTheElapsedTotal()
        {
            var engine = new Engine(new TypeRegistry(System.Array.Empty<PouAst>()));
            var origin = engine.Clock.StartUtc;

            engine.Clock.AdvanceMs(1_500);

            Assert.Equal(origin.AddMilliseconds(1_500), engine.Clock.UtcNow);
        }

        [Fact]
        public void TaskStart_DoesNotMoveUntilTheNextCycleBegins()
        {
            // The distinction F_GetTaskTime rests on: advancing the clock
            // part-way through a cycle moves "now" without moving the time that
            // cycle began, so two reads within one cycle agree.
            var engine = new Engine(new TypeRegistry(System.Array.Empty<PouAst>()));

            engine.Clock.BeginCycle();
            engine.Clock.AdvanceMs(250);
            var afterAdvance = engine.Clock.TaskStartUtc;

            engine.Clock.BeginCycle();

            Assert.Equal(engine.Clock.StartUtc, afterAdvance);
            Assert.Equal(engine.Clock.StartUtc.AddMilliseconds(250), engine.Clock.TaskStartUtc);
        }
    }
}
