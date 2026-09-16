using System;

namespace xStunit.Interpreter
{
    // The shared simulated clock every timer reads. Holds a monotonic
    // absolute running total rather than a one-shot delta: each TimerHost
    // keeps its own last-observed total, so no stepping order the caller
    // chooses can let one instance consume a delta meant for another.
    //
    // The total is NANOSECONDS, with milliseconds as the derived view,
    // because the LTIME timers (LTON/LTOF/LTP) count in ns and a ms-only
    // clock could not express a sub-millisecond PT at all. long ns leaves
    // ~292 years of simulated time before overflow and stays independent of
    // TIME's 32-bit / LTIME's 64-bit field widths.
    public sealed class Clock
    {
        public const long NanosecondsPerMillisecond = 1_000_000L;

        public long TotalNs { get; private set; }

        // What simulated t=0 corresponds to in wall-clock terms, for the
        // library functions that answer "what time is it?" rather than "how
        // long since?". Fixed and settable rather than taken from the real
        // clock at construction: a suite asserting an absolute timestamp has to
        // be able to know the answer, which is the whole reason those functions
        // read this clock instead of the machine's.
        public DateTime StartUtc { get; set; } = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // The running total as of the start of the current PLC cycle, which is
        // where it stays while a body advances the clock mid-cycle. A task's
        // start time is a real distinction in TwinCAT - two reads in one cycle
        // return the same task time and, if time moved, different system times.
        public long TaskStartNs { get; private set; }

        // Absolute simulated time now, and at the start of the current cycle. A
        // DateTime tick IS 100 ns, so no scaling is lost either way.
        public DateTime UtcNow => StartUtc.AddTicks(TotalNs / 100);

        public DateTime TaskStartUtc => StartUtc.AddTicks(TaskStartNs / 100);

        // Latches the task start. Called once per PLC cycle by the cycle loop,
        // never by a host: a native stub that latched it would move the task
        // start of whatever cycle it happened to be invoked in.
        public void BeginCycle() => TaskStartNs = TotalNs;

        // Whole elapsed milliseconds; a sub-millisecond remainder is
        // truncated here but still counted in TotalNs.
        public long TotalMs => TotalNs / NanosecondsPerMillisecond;

        public void AdvanceMs(long deltaMs) => TotalNs += deltaMs * NanosecondsPerMillisecond;

        public void AdvanceNs(long deltaNs) => TotalNs += deltaNs;
    }
}
