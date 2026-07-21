namespace TcXunit.Interpreter
{
    // Native-stub boundary for Loopback (TcXunit-w5x.15.5 / T4 design): one
    // Loopback instance = one fixed link. Transmit is a discrete copy
    // (sink.Value = source.Value), called explicitly by the test author -
    // no implicit wiring, no StepCycles hook. Future fault-injection state
    // (drop/delay/corrupt, T5) lives here, keyed to nothing but this host.
    public sealed class LoopbackHost
    {
        public void Transmit(Cell source, Cell sink)
        {
            sink.Value = source.Value;
        }
    }
}
