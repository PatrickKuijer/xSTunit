namespace TcXunit.Interpreter
{
    // Process-wide shared simulated clock (TcXunit-w5x.15.7 / T3 design):
    // monotonic absolute running total in ms, not a one-shot delta - each
    // TimerHost keeps its own last-observed total, so caller-controlled
    // multi-instance stepping order can't let one instance "steal" a delta
    // meant for another. long ms internally to remove overflow risk on
    // long-running suites, independent of TIME's 32-bit width.
    public sealed class Clock
    {
        public long TotalMs { get; private set; }

        public void Advance(long deltaMs) => TotalMs += deltaMs;
    }
}
