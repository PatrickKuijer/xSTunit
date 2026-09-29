using System;

namespace xStunit.Interpreter
{
    // Stands in for the Tc2_Standard edge detectors R_TRIG/F_TRIG. Unlike the
    // timers these need no clock: Q is TRUE for exactly one CALL - not one
    // unit of simulated time - after the relevant CLK transition, so the only
    // state required is the previous CLK level this host remembers. That
    // makes the host cycle-driven in the strict sense: skipping a call skips
    // an edge, and calling twice in one cycle consumes it early.
    public abstract class EdgeTriggerHost
    {
        private bool _lastClk;
        private bool _initialized;

        // Matched case-insensitively, because the lookup in Engine that
        // decides to call this at all is case-insensitive too - a lowercase or
        // mixed-case spelling that got past that lookup must not then throw
        // here.
        public static EdgeTriggerHost Create(string typeName) => typeName?.ToUpperInvariant() switch
        {
            "R_TRIG" => new RTrigHost(),
            "F_TRIG" => new FTrigHost(),
            _ => throw new NotSupportedException($"Unknown native edge-trigger type '{typeName}'"),
        };

        internal EdgeTriggerHost CloneState() => (EdgeTriggerHost)MemberwiseClone();

        public void Update(FbInstance instance)
        {
            var clk = (bool)instance.Fields["CLK"].Value;

            // The first call has no prior CLK to compare against, so it only
            // records a baseline: an instance whose CLK is already TRUE on
            // call one does NOT report an edge. (CounterHost deliberately
            // decides the opposite for its own counting inputs.)
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
