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

        // Whole elapsed milliseconds; a sub-millisecond remainder is
        // truncated here but still counted in TotalNs.
        public long TotalMs => TotalNs / NanosecondsPerMillisecond;

        public void AdvanceMs(long deltaMs) => TotalNs += deltaMs * NanosecondsPerMillisecond;

        public void AdvanceNs(long deltaNs) => TotalNs += deltaNs;
    }
}
