namespace xStunit.Interpreter
{
    // Process-wide shared simulated clock (TcXunit-w5x.15.7 / T3 design):
    // monotonic absolute running total, not a one-shot delta - each
    // TimerHost keeps its own last-observed total, so caller-controlled
    // multi-instance stepping order can't let one instance "steal" a delta
    // meant for another.
    //
    // TcXunit-x5pt: the stored total is NANOSECONDS, not milliseconds. The
    // LTIME timers (LTON/LTOF/LTP) count in ns, so a ms-only clock could not
    // express a sub-millisecond PT at all; ms is now the derived view. long ns
    // still removes overflow risk on any plausible suite (~292 years of
    // simulated time) and stays independent of TIME's 32-bit / LTIME's 64-bit
    // field widths.
    public sealed class Clock
    {
        public const long NanosecondsPerMillisecond = 1_000_000L;

        public long TotalNs { get; private set; }

        // Truncating ms view of the same total. Callers that only ever
        // AdvanceMs see exactly the value they always did; a caller mixing
        // in AdvanceNs sees whole elapsed milliseconds, sub-ms remainder
        // excluded (it is still counted in TotalNs).
        public long TotalMs => TotalNs / NanosecondsPerMillisecond;

        public void AdvanceMs(long deltaMs) => TotalNs += deltaMs * NanosecondsPerMillisecond;

        public void AdvanceNs(long deltaNs) => TotalNs += deltaNs;
    }
}
