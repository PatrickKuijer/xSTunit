using System;

namespace TcXunit.Interpreter
{
    // Native-stub boundary for R_TRIG/F_TRIG (TcXunit-f6b): same precedent as
    // TimerHost, but no clock dependency - Q fires for exactly one call on the
    // relevant CLK transition, driven purely by the previous-CLK bookkeeping
    // this host holds.
    public abstract class EdgeTriggerHost
    {
        private bool _lastClk;
        private bool _initialized;

        public static EdgeTriggerHost Create(string typeName) => typeName switch
        {
            "R_TRIG" => new RTrigHost(),
            "F_TRIG" => new FTrigHost(),
            _ => throw new NotSupportedException($"Unknown native edge-trigger type '{typeName}'"),
        };

        public void Update(FbInstance instance)
        {
            var clk = (bool)instance.Fields["CLK"].Value;

            // First call has no prior CLK to compare against - no edge, just
            // record the baseline (mirrors TimerHost's _initialized guard).
            var q = _initialized && IsEdge(_lastClk, clk);
            _lastClk = clk;
            _initialized = true;

            instance.Fields["Q"].Value = q;
        }

        protected abstract bool IsEdge(bool lastClk, bool clk);
    }

    // Rising-edge detector: Q true for one call on FALSE->TRUE transition.
    internal sealed class RTrigHost : EdgeTriggerHost
    {
        protected override bool IsEdge(bool lastClk, bool clk) => clk && !lastClk;
    }

    // Falling-edge detector: Q true for one call on TRUE->FALSE transition.
    internal sealed class FTrigHost : EdgeTriggerHost
    {
        protected override bool IsEdge(bool lastClk, bool clk) => !clk && lastClk;
    }
}
