using System;

namespace xStunit.Interpreter
{
    // Native-stub boundary for TON/TOF/TP (FB_Pulse) and their 64-bit LTIME
    // siblings LTON/LTOF/LTP (TcXunit-w5x.15.7 / T3 design, extended by
    // TcXunit-x5pt): same precedent as TcUnitSuiteHost, but Q/ET publish into
    // the instance's own IN/PT/Q/ET Cell fields (set up in Engine.NewInstance)
    // instead of being collected once - the host itself only holds internal
    // bookkeeping (last-observed clock total, edge-detect state) that ST code
    // never sees.
    //
    // The ms and ns families differ ONLY in how their PT/ET fields are
    // encoded, never in behavior: LTON is TON with PT/ET in 64-bit ns instead
    // of 32-bit ms (Beckhoff Tc2_Standard, LTON/LTOF/LTP). So the timing math
    // below runs entirely in the clock's own nanoseconds and a DurationField
    // handed to each host at construction does the two conversions at the
    // boundary - three behaviors x two widths, not six behavior classes.
    public abstract class TimerHost
    {
        private readonly DurationField _durationField;
        private long _lastClockTotalNs;
        private bool _initialized;

        protected TimerHost(DurationField durationField)
        {
            _durationField = durationField;
        }

        // TcXunit-nch: matched case-insensitively (typeName.ToUpperInvariant()),
        // same decision as the NativeTimerTypes lookup in Engine.NativeHost.cs
        // that decides to call Create in the first place - a lowercase/mixed-case
        // spelling that passes that lookup must not then throw
        // NotSupportedException here.
        public static TimerHost Create(string typeName) => typeName?.ToUpperInvariant() switch
        {
            "TON" => new OnDelayTimerHost(DurationField.Time32),
            "TOF" => new OffDelayTimerHost(DurationField.Time32),
            // TcXunit-tzeg.1: TP is the IEC 61131-3 name for the pulse timer;
            // FB_Pulse is only our own alias for it, so both spellings must
            // land on the same host.
            "TP" => new PulseTimerHost(DurationField.Time32),
            "FB_PULSE" => new PulseTimerHost(DurationField.Time32),
            // TcXunit-x5pt: the LTIME trio, identical behavior at ns width.
            "LTON" => new OnDelayTimerHost(DurationField.LongTime64),
            "LTOF" => new OffDelayTimerHost(DurationField.LongTime64),
            "LTP" => new PulseTimerHost(DurationField.LongTime64),
            _ => throw new NotSupportedException($"Unknown native timer type '{typeName}'"),
        };

        // The boxed zero this host's PT/ET fields must be seeded with: 0u for
        // a TIME timer, 0ul for an LTIME one. Engine.NativeHost's classifier
        // reads it off the host rather than re-deciding the width there, the
        // same "one spelling, one owner" precedent as RS/SR's input names.
        public object ZeroDuration => _durationField.Box(0);

        public void Update(FbInstance instance, long clockTotalNs)
        {
            if (!_initialized)
            {
                _lastClockTotalNs = clockTotalNs;
                _initialized = true;
            }

            var deltaNs = clockTotalNs - _lastClockTotalNs;
            _lastClockTotalNs = clockTotalNs;

            var input = (bool)instance.Fields["IN"].Value;
            var ptNs = _durationField.ToNanoseconds(instance.Fields["PT"].Value);

            UpdateCore(instance, input, ptNs, deltaNs);
        }

        protected abstract void UpdateCore(FbInstance instance, bool input, long ptNs, long deltaNs);

        // Raw elapsed accumulates unclamped and drives the firing check;
        // published ET clamps to min(rawElapsed, PT) matching TON/TOF/TP's
        // documented contract (Q is TRUE iff ET has reached PT).
        protected void Publish(FbInstance instance, bool q, long rawElapsedNs, long ptNs)
        {
            instance.Fields["Q"].Value = q;
            instance.Fields["ET"].Value = _durationField.Box(Math.Min(rawElapsedNs, ptNs));
        }
    }

    // How a timer's PT/ET Cells encode a duration: TIME as uint milliseconds,
    // LTIME as ulong nanoseconds (the same two Cell representations
    // TimeLiteral/IecElementaryDefault already produce for T#/LTIME#
    // literals - a timer must read and write exactly what ST assigns).
    public sealed class DurationField
    {
        public static readonly DurationField Time32 =
            new DurationField(Clock.NanosecondsPerMillisecond, ns => (uint)ns);

        public static readonly DurationField LongTime64 =
            new DurationField(1L, ns => (ulong)ns);

        private readonly long _nsPerUnit;
        private readonly Func<long, object> _box;

        private DurationField(long nsPerUnit, Func<long, object> box)
        {
            _nsPerUnit = nsPerUnit;
            _box = box;
        }

        // Field units -> ns. uint/ulong are the two boxings that actually
        // occur (TIME/LTIME); int and the Convert fallback cover a PT that ST
        // assigned from an integer expression rather than a duration literal.
        public long ToNanoseconds(object value)
        {
            var units = value switch
            {
                uint u => u,
                ulong ul => (long)ul,
                int i => i,
                long l => l,
                _ => Convert.ToInt64(value),
            };
            return units * _nsPerUnit;
        }

        // ns -> boxed field units. TIME truncates a sub-millisecond remainder
        // (a 32-bit ms field cannot represent it); LTIME divides by 1 and so
        // keeps full ns precision, which is the whole point of the LTIME trio.
        public object Box(long nanoseconds) => _box(nanoseconds / _nsPerUnit);
    }

    // On-delay (TON/LTON): Q follows IN with a PT delay. Falling edge resets
    // immediately (no delay on the way down).
    internal sealed class OnDelayTimerHost : TimerHost
    {
        private bool _lastIn;
        private long _rawElapsedNs;

        public OnDelayTimerHost(DurationField durationField)
            : base(durationField)
        {
        }

        protected override void UpdateCore(FbInstance instance, bool input, long ptNs, long deltaNs)
        {
            if (input)
            {
                if (!_lastIn)
                    _rawElapsedNs = 0;
                _rawElapsedNs += deltaNs;
            }
            else
            {
                _rawElapsedNs = 0;
            }
            _lastIn = input;

            Publish(instance, input && _rawElapsedNs >= ptNs, _rawElapsedNs, ptNs);
        }
    }

    // Off-delay (TOF/LTOF): Q follows IN immediately on rising edge, but stays
    // TRUE for PT after IN's falling edge.
    internal sealed class OffDelayTimerHost : TimerHost
    {
        private bool _lastIn;
        private long _rawElapsedNs;

        public OffDelayTimerHost(DurationField durationField)
            : base(durationField)
        {
        }

        protected override void UpdateCore(FbInstance instance, bool input, long ptNs, long deltaNs)
        {
            if (!input)
            {
                if (_lastIn)
                    _rawElapsedNs = 0;
                _rawElapsedNs += deltaNs;
            }
            else
            {
                _rawElapsedNs = 0;
            }
            _lastIn = input;

            Publish(instance, input || _rawElapsedNs < ptNs, _rawElapsedNs, ptNs);
        }
    }

    // Pulse (TP/FB_Pulse/LTP): rising edge of IN (while not already running)
    // starts a fixed-width PT pulse on Q, independent of what IN does for the
    // rest of the pulse. Re-triggering requires a fresh rising edge after the
    // pulse ends.
    internal sealed class PulseTimerHost : TimerHost
    {
        private bool _lastIn;
        private bool _running;
        private long _rawElapsedNs;

        public PulseTimerHost(DurationField durationField)
            : base(durationField)
        {
        }

        protected override void UpdateCore(FbInstance instance, bool input, long ptNs, long deltaNs)
        {
            var risingEdge = input && !_lastIn;
            _lastIn = input;

            if (risingEdge && !_running)
            {
                _running = true;
                _rawElapsedNs = 0;
            }

            if (_running)
            {
                _rawElapsedNs += deltaNs;
                if (_rawElapsedNs >= ptNs)
                    _running = false;
            }

            Publish(instance, _running, _rawElapsedNs, ptNs);
        }
    }
}
