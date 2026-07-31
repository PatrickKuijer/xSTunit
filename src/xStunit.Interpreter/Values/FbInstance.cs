using System.Collections.Generic;

namespace xStunit.Interpreter
{
    public sealed class FbInstance
    {
        public string ActualTypeName { get; }
        public Dictionary<string, Cell> Fields { get; } = new Dictionary<string, Cell>();

        // Declared IEC type text (e.g. "REFERENCE TO INT") per field,
        // populated once at NewInstance time and never touched afterward. A
        // REF= binding replaces the field's entry in Fields wholesale with the
        // target's own Cell, so Cell.DeclaredTypeName afterwards describes the
        // target rather than the field's declaration; __ISVALIDREF has to know
        // whether the *name being asked about* was declared REFERENCE TO/
        // POINTER TO, so for instance fields this table is the source of truth.
        public Dictionary<string, string> FieldTypeNames { get; } = new Dictionary<string, string>();

        // Which native (compiled-only) stub backs this instance, and the host
        // object itself - assigned as a pair by the native-host classifier in
        // Engine.NativeHost.cs during NewInstance. An instance can be backed
        // by at most one native host, so one kind plus one slot carries the
        // whole story and no caller has to re-derive the kind by null-checking
        // typed fields in its own order.
        public NativeHostKind NativeKind { get; set; }

        public object NativeHost { get; set; }

        // The typed views below are checked casts, not pass-throughs: the host
        // types share no base class, so NativeHost is typed object, and each
        // getter yields its host only when NativeKind agrees. The null on
        // mismatch is load-bearing - the suite-API precedence gate at
        // NativeMethodBridge.CanInvoke works only because a non-suite
        // receiver's NativeSuiteHost is null. Ask NativeKind what an instance
        // is, though; these are for callers that already know and want the
        // host's own API.

        // Non-null when ActualTypeName's ancestry reaches TcUnit.FB_TestSuite:
        // the native stub backing TEST()/AssertEquals_INT()/etc.
        public SuiteHost NativeSuiteHost =>
            NativeKind == NativeHostKind.Suite ? (SuiteHost)NativeHost : null;

        // Backs IN/PT -> Q/ET for the timer FBs TON/TOF/TP/FB_Pulse and their
        // LTIME siblings LTON/LTOF/LTP.
        public TimerHost NativeTimerHost =>
            NativeKind == NativeHostKind.Timer ? (TimerHost)NativeHost : null;

        // Backs Transmit(source, sink).
        public LoopbackHost NativeLoopbackHost =>
            NativeKind == NativeHostKind.Loopback ? (LoopbackHost)NativeHost : null;

        // Backs SET/RESET -> Q1 for the bistable latch FBs RS/SR.
        public BistableLatchHost NativeBistableLatchHost =>
            NativeKind == NativeHostKind.BistableLatch ? (BistableLatchHost)NativeHost : null;

        // Backs the counting behavior of CTU/CTD/CTUD.
        public CounterHost NativeCounterHost =>
            NativeKind == NativeHostKind.Counter ? (CounterHost)NativeHost : null;

        // Backs CLK -> Q for the edge-trigger FBs R_TRIG/F_TRIG.
        public EdgeTriggerHost NativeEdgeTriggerHost =>
            NativeKind == NativeHostKind.Edge ? (EdgeTriggerHost)NativeHost : null;

        public FbInstance(string actualTypeName)
        {
            ActualTypeName = actualTypeName;
        }
    }
}
