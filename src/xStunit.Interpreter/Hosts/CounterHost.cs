using System;
using System.Collections.Generic;
using System.Linq;

namespace xStunit.Interpreter
{
    // Stands in for the Tc2_Standard counters CTU/CTD/CTUD. No clock is
    // involved; everything the caller reads back (Q/QU/QD/CV) is published
    // into the instance's own Cell fields.
    //
    // Like the latches, the field names differ per counter (CTU is
    // CU/RESET->Q, CTD is CD/LOAD->Q, CTUD is CU/CD/RESET/LOAD->QU/QD), so the
    // names live on the host and both the field seeding
    // (Engine.NativeHost.cs) and the bare-invoke argument binding
    // (Engine.Invocation.cs) read them from here rather than re-spelling them.
    //
    // Unlike the latches, a counter DOES hold state of its own: the previous
    // CU/CD level behind its rising-edge detection. That makes it cycle-
    // sensitive - a counting input is only counted once per FALSE->TRUE
    // transition, so two calls in one cycle count once and a skipped call can
    // miss an edge entirely. CV is not part of that state; it persists in the
    // instance's own CV Cell across cycles, as Q1 does for a latch.
    //
    // Semantics are the documented Tc2_Standard ones (CTU
    // infosys .../74400523.html, CTD .../74398987.html, CTUD .../74402059.html):
    //
    //   CTU : RESET -> CV := 0; else rising edge on CU -> CV := CV + 1.
    //         Q := CV >= PV.
    //   CTD : LOAD  -> CV := PV; else rising edge on CD -> CV := CV - 1
    //         "as long as CV is greater than 0". Q := CV = 0.
    //   CTUD: RESET -> CV := 0; else LOAD -> CV := PV; else rising edge on
    //         CU/CD counts. QU := CV >= PV, QD := CV = 0.
    public abstract class CounterHost
    {
        // CV and PV are WORD, and CV SATURATES at the WORD limits rather than
        // wrapping: the doc counts down only "as long as CV is greater than
        // 0", and the IEC 61131-3 counter bodies guard the increment with
        // CV < CVmax for the same reason. So this is a clamp, NOT C#'s
        // unchecked ushort arithmetic - 65535 + 1 stays 65535 and 0 - 1 stays
        // 0. WORD is boxed as a C# int in this interpreter (IecNumericType),
        // which is exactly why the clamp has to be explicit here.
        public const int MinCounterValue = ushort.MinValue;
        public const int MaxCounterValue = ushort.MaxValue;

        // The one VAR_INPUT all three share, and always their LAST positional
        // parameter - so PositionalInputNames below is derivable from the
        // per-counter BOOL inputs alone.
        public const string PresetInputName = "PV";

        // The counter variable. A VAR_OUTPUT, but it is also the counter's
        // carried-over state, so it is seeded and read back as an int Cell
        // rather than a BOOL like the other outputs.
        public const string CurrentValueOutputName = "CV";

        private IReadOnlyList<string> _positionalInputNames;

        // Matched case-insensitively, because the lookup in Engine.NativeHost
        // that decides to call this at all is case-insensitive too - a
        // lowercase or mixed-case spelling that got past that lookup must not
        // then throw here.
        public static CounterHost Create(string typeName) => typeName?.ToUpperInvariant() switch
        {
            "CTU" => new UpCounterHost(),
            "CTD" => new DownCounterHost(),
            "CTUD" => new UpDownCounterHost(),
            _ => throw new NotSupportedException($"Unknown native counter type '{typeName}'"),
        };

        // The counter's BOOL VAR_INPUTs in IEC declaration order.
        public abstract IReadOnlyList<string> BooleanInputNames { get; }

        // The counter's BOOL VAR_OUTPUTs in IEC declaration order (CV is not
        // one of them - see CurrentValueOutputName).
        public abstract IReadOnlyList<string> BooleanOutputNames { get; }

        // Full VAR_INPUT list in IEC declaration order - the positional
        // argument order for a bare invocation like fbCounter(TRUE, FALSE, 3).
        // PV is last for all three counters.
        public IReadOnlyList<string> PositionalInputNames =>
            _positionalInputNames ??
            (_positionalInputNames = BooleanInputNames.Concat(new[] { PresetInputName }).ToArray());

        public void Update(FbInstance instance)
        {
            var cv = Count(instance, ReadCounterValue(instance));

            instance.Fields[CurrentValueOutputName].Value = cv;
            PublishOutputs(instance, cv);
        }

        // Returns the new CV. Implementations MUST sample every counting input
        // through their rising-edge memory before honoring RESET/LOAD: the
        // level was still presented to the FB on this cycle, so it has to be
        // remembered even when the edge it would have produced is discarded.
        protected abstract int Count(FbInstance instance, int currentValue);

        protected abstract void PublishOutputs(FbInstance instance, int currentValue);

        protected static bool ReadBool(FbInstance instance, string fieldName) =>
            (bool)instance.Fields[fieldName].Value;

        // PV is WORD, so a preset outside the WORD range can't be loaded into
        // CV; clamped for the same reason CV is.
        protected static int ReadPreset(FbInstance instance) =>
            Clamp(Convert.ToInt32(instance.Fields[PresetInputName].Value));

        private static int ReadCounterValue(FbInstance instance) =>
            Clamp(Convert.ToInt32(instance.Fields[CurrentValueOutputName].Value));

        private static int Clamp(int value) =>
            value < MinCounterValue ? MinCounterValue : value > MaxCounterValue ? MaxCounterValue : value;

        // Rising-edge memory for one counting input. The previous level starts
        // FALSE rather than "unknown": the IEC counter bodies keep it in an
        // ordinary BOOL that defaults to FALSE, so the very first call with the
        // input already TRUE *is* a FALSE->TRUE edge and does count. That is
        // deliberately unlike EdgeTriggerHost's _initialized baseline guard,
        // which models R_TRIG's own first-call behavior, not a counter's.
        protected sealed class RisingEdgeMemory
        {
            private bool _lastLevel;

            public bool Sample(bool level)
            {
                var isRisingEdge = level && !_lastLevel;
                _lastLevel = level;
                return isRisingEdge;
            }
        }
    }

    // CTU(CU, RESET, PV) -> Q, CV. Up counter: RESET takes precedence over
    // counting, and CV saturates at the WORD ceiling instead of wrapping.
    internal sealed class UpCounterHost : CounterHost
    {
        private const string CountUpInput = "CU";
        private const string ResetInput = "RESET";
        private const string LimitReachedOutput = "Q";

        private static readonly string[] InputNames = { CountUpInput, ResetInput };
        private static readonly string[] OutputNames = { LimitReachedOutput };

        private readonly RisingEdgeMemory _countUp = new RisingEdgeMemory();

        public override IReadOnlyList<string> BooleanInputNames => InputNames;

        public override IReadOnlyList<string> BooleanOutputNames => OutputNames;

        protected override int Count(FbInstance instance, int currentValue)
        {
            var countUp = _countUp.Sample(ReadBool(instance, CountUpInput));

            if (ReadBool(instance, ResetInput))
                return MinCounterValue;

            return countUp && currentValue < MaxCounterValue ? currentValue + 1 : currentValue;
        }

        protected override void PublishOutputs(FbInstance instance, int currentValue) =>
            instance.Fields[LimitReachedOutput].Value = currentValue >= ReadPreset(instance);
    }

    // CTD(CD, LOAD, PV) -> Q, CV. Down counter: LOAD takes precedence over
    // counting, and CV stops at 0 ("as long as CV is greater than zero")
    // instead of wrapping to 65535.
    internal sealed class DownCounterHost : CounterHost
    {
        private const string CountDownInput = "CD";
        private const string LoadInput = "LOAD";
        private const string ZeroReachedOutput = "Q";

        private static readonly string[] InputNames = { CountDownInput, LoadInput };
        private static readonly string[] OutputNames = { ZeroReachedOutput };

        private readonly RisingEdgeMemory _countDown = new RisingEdgeMemory();

        public override IReadOnlyList<string> BooleanInputNames => InputNames;

        public override IReadOnlyList<string> BooleanOutputNames => OutputNames;

        protected override int Count(FbInstance instance, int currentValue)
        {
            var countDown = _countDown.Sample(ReadBool(instance, CountDownInput));

            if (ReadBool(instance, LoadInput))
                return ReadPreset(instance);

            return countDown && currentValue > MinCounterValue ? currentValue - 1 : currentValue;
        }

        protected override void PublishOutputs(FbInstance instance, int currentValue) =>
            instance.Fields[ZeroReachedOutput].Value = currentValue <= MinCounterValue;
    }

    // CTUD(CU, CD, RESET, LOAD, PV) -> QU, QD, CV. RESET wins over LOAD, and
    // both win over counting.
    internal sealed class UpDownCounterHost : CounterHost
    {
        private const string CountUpInput = "CU";
        private const string CountDownInput = "CD";
        private const string ResetInput = "RESET";
        private const string LoadInput = "LOAD";
        private const string LimitReachedOutput = "QU";
        private const string ZeroReachedOutput = "QD";

        private static readonly string[] InputNames = { CountUpInput, CountDownInput, ResetInput, LoadInput };
        private static readonly string[] OutputNames = { LimitReachedOutput, ZeroReachedOutput };

        private readonly RisingEdgeMemory _countUp = new RisingEdgeMemory();
        private readonly RisingEdgeMemory _countDown = new RisingEdgeMemory();

        public override IReadOnlyList<string> BooleanInputNames => InputNames;

        public override IReadOnlyList<string> BooleanOutputNames => OutputNames;

        protected override int Count(FbInstance instance, int currentValue)
        {
            // Both edges are sampled every cycle, whatever happens next -
            // otherwise a CU level held across a RESET cycle would produce a
            // phantom edge on the cycle RESET drops.
            var countUp = _countUp.Sample(ReadBool(instance, CountUpInput));
            var countDown = _countDown.Sample(ReadBool(instance, CountDownInput));

            if (ReadBool(instance, ResetInput))
                return MinCounterValue;

            if (ReadBool(instance, LoadInput))
                return ReadPreset(instance);

            // Simultaneous up and down edges cancel (IEC 61131-3's
            // NOT (CU AND CD) guard on the CTUD body) rather than letting
            // whichever branch is written first silently win.
            if (countUp && countDown)
                return currentValue;

            if (countUp && currentValue < MaxCounterValue)
                return currentValue + 1;

            if (countDown && currentValue > MinCounterValue)
                return currentValue - 1;

            return currentValue;
        }

        protected override void PublishOutputs(FbInstance instance, int currentValue)
        {
            instance.Fields[LimitReachedOutput].Value = currentValue >= ReadPreset(instance);
            instance.Fields[ZeroReachedOutput].Value = currentValue <= MinCounterValue;
        }
    }
}
