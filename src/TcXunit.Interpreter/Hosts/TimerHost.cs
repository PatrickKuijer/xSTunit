using System;

namespace TcXunit.Interpreter
{
    // Native-stub boundary for TON/TOF/FB_Pulse (TcXunit-w5x.15.7 / T3 design):
    // same precedent as TcUnitSuiteHost, but Q/ET publish into the instance's
    // own IN/PT/Q/ET Cell fields (set up in Engine.NewInstance) instead of
    // being collected once - the host itself only holds internal bookkeeping
    // (last-observed clock total, edge-detect state) that ST code never sees.
    public abstract class TimerHost
    {
        private long _lastClockTotalMs;
        private bool _initialized;

        // TcXunit-nch: matched case-insensitively (typeName.ToUpperInvariant()),
        // same decision as the NativeTimerTypes lookup in Engine.NativeHost.cs
        // that decides to call Create in the first place - a lowercase/mixed-case
        // spelling that passes that lookup must not then throw
        // NotSupportedException here.
        public static TimerHost Create(string typeName) => typeName?.ToUpperInvariant() switch
        {
            "TON" => new TonHost(),
            "TOF" => new TofHost(),
            // TcXunit-tzeg.1: TP is the IEC 61131-3 name for the pulse timer;
            // FB_Pulse is only our own alias for it, so both spellings must
            // land on the same host.
            "TP" => new PulseHost(),
            "FB_PULSE" => new PulseHost(),
            _ => throw new NotSupportedException($"Unknown native timer type '{typeName}'"),
        };

        public void Update(FbInstance instance, long clockTotalMs)
        {
            if (!_initialized)
            {
                _lastClockTotalMs = clockTotalMs;
                _initialized = true;
            }

            var deltaMs = clockTotalMs - _lastClockTotalMs;
            _lastClockTotalMs = clockTotalMs;

            var input = (bool)instance.Fields["IN"].Value;
            var pt = ToMs(instance.Fields["PT"].Value);

            UpdateCore(instance, input, pt, deltaMs);
        }

        protected abstract void UpdateCore(FbInstance instance, bool input, long pt, long deltaMs);

        // Raw elapsed accumulates unclamped and drives the firing check;
        // published ET clamps to min(rawElapsed, PT) matching TON/TOF/TP's
        // documented contract (Q is TRUE iff ET has reached PT).
        protected static void Publish(FbInstance instance, bool q, long rawElapsed, long pt)
        {
            instance.Fields["Q"].Value = q;
            instance.Fields["ET"].Value = (uint)Math.Min(rawElapsed, pt);
        }

        private static long ToMs(object value) => value switch
        {
            uint u => u,
            int i => i,
            _ => Convert.ToInt64(value),
        };
    }

    // On-delay: Q follows IN with a PT delay. Falling edge resets immediately
    // (no delay on the way down).
    internal sealed class TonHost : TimerHost
    {
        private bool _lastIn;
        private long _rawElapsed;

        protected override void UpdateCore(FbInstance instance, bool input, long pt, long deltaMs)
        {
            if (input)
            {
                if (!_lastIn)
                    _rawElapsed = 0;
                _rawElapsed += deltaMs;
            }
            else
            {
                _rawElapsed = 0;
            }
            _lastIn = input;

            Publish(instance, input && _rawElapsed >= pt, _rawElapsed, pt);
        }
    }

    // Off-delay: Q follows IN immediately on rising edge, but stays TRUE for
    // PT after IN's falling edge.
    internal sealed class TofHost : TimerHost
    {
        private bool _lastIn;
        private long _rawElapsed;

        protected override void UpdateCore(FbInstance instance, bool input, long pt, long deltaMs)
        {
            if (!input)
            {
                if (_lastIn)
                    _rawElapsed = 0;
                _rawElapsed += deltaMs;
            }
            else
            {
                _rawElapsed = 0;
            }
            _lastIn = input;

            Publish(instance, input || _rawElapsed < pt, _rawElapsed, pt);
        }
    }

    // Pulse: rising edge of IN (while not already running) starts a fixed-
    // width PT pulse on Q, independent of what IN does for the rest of the
    // pulse. Re-triggering requires a fresh rising edge after the pulse ends.
    internal sealed class PulseHost : TimerHost
    {
        private bool _lastIn;
        private bool _running;
        private long _rawElapsed;

        protected override void UpdateCore(FbInstance instance, bool input, long pt, long deltaMs)
        {
            var risingEdge = input && !_lastIn;
            _lastIn = input;

            if (risingEdge && !_running)
            {
                _running = true;
                _rawElapsed = 0;
            }

            if (_running)
            {
                _rawElapsed += deltaMs;
                if (_rawElapsed >= pt)
                    _running = false;
            }

            Publish(instance, _running, _rawElapsed, pt);
        }
    }
}
