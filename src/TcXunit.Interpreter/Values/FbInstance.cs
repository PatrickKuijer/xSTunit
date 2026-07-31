using System.Collections.Generic;

namespace TcXunit.Interpreter
{
    public sealed class FbInstance
    {
        public string ActualTypeName { get; }
        public Dictionary<string, Cell> Fields { get; } = new Dictionary<string, Cell>();

        // Declared IEC type text (e.g. "REFERENCE TO INT") for each entry in
        // Fields, indexed by name - populated once at NewInstance time and
        // never touched afterward. Fields itself gets its entry *replaced*
        // wholesale by a REF= binding (Engine.ExecuteStatement's
        // RefAssignStmt case aliases the field directly onto the target's
        // Cell, TcXunit-t6p), which would otherwise erase the field's own
        // declared type in favor of whatever it now points at. __ISVALIDREF
        // (TcXunit-6lh) needs the former, not the latter, to validate that
        // the *name being asked about* was actually declared REFERENCE TO/
        // POINTER TO - so this side table is the source of truth instead of
        // Cell.DeclaredTypeName for instance fields.
        public Dictionary<string, string> FieldTypeNames { get; } = new Dictionary<string, string>();

        // Which native (compiled-only) stub backs this instance, and the host
        // object itself - TcXunit-j98's single discriminator, assigned as a
        // pair by Engine's native-host classifier (Engine.NativeHost.cs)
        // during NewInstance. An instance can be backed by at most one native
        // host, so one kind + one slot says exactly as much as the four
        // mutually exclusive nullable fields this replaced, without letting
        // callers re-derive the kind by null-checking in their own order.
        public NativeHostKind NativeKind { get; set; }

        public object NativeHost { get; set; }

        // Read-only typed views over that pair, for the callers that actually
        // work the host's own API (TimerHost.Update, LoopbackHost.Transmit,
        // TcUnitSuiteHost.Collect/EnterNativeCall) and would otherwise cast
        // NativeHost by hand. Each yields its host only when NativeKind
        // agrees, so a caller that reaches for the wrong one gets null rather
        // than an InvalidCastException.
        //
        // They are deliberately NOT the way to ask "what kind is this?" -
        // that is NativeKind's job, and null-checking these instead is the
        // duplication TcXunit-j98/kwv6/fvp6 removed. Setters are gone with it:
        // kind and host are only ever assigned together, by the classifier.
        //
        // TcXunit-d6qq/6a09 ratified KEEPING all four, so the next reader does
        // not re-litigate them as trivial pass-throughs. They are not
        // pass-throughs: NativeHost is typed object precisely because the four
        // host types share no base class, so each getter is a *checked* cast -
        // and the null it returns on mismatch is load-bearing, not incidental.
        // Callers with no switch in hand depend on it, e.g. the suite-API
        // precedence gate documented at NativeMethodBridge.CanInvoke, which
        // only works because a non-suite receiver's NativeSuiteHost is null.
        // Retiring them would mean ~12 hand-written unchecked casts at the
        // Engine.Invocation call sites. The residue where a caller has already
        // switched on Kind and the getter compares it a second time (e.g.
        // Engine.Invocation.cs's 'case NativeHostKind.Timer:') is accepted:
        // that compare provably cannot fail there, and costs one enum compare
        // on a cold path.

        // Non-null when ActualTypeName's ancestry reaches TcUnit.FB_TestSuite -
        // the native C# stub instance backing TEST()/AssertEquals_INT()/etc for
        // this instance (TcXunit-w5x.7's native-stub boundary).
        public TcUnitSuiteHost NativeSuiteHost =>
            NativeKind == NativeHostKind.Suite ? (TcUnitSuiteHost)NativeHost : null;

        // Non-null when ActualTypeName is a native timer type (TON/TOF/TP/
        // FB_Pulse, or their LTIME siblings LTON/LTOF/LTP) - the native host
        // backing this instance's IN/PT->Q/ET behavior (TcXunit-w5x.15.7's
        // native-stub boundary, extended by TcXunit-x5pt).
        public TimerHost NativeTimerHost =>
            NativeKind == NativeHostKind.Timer ? (TimerHost)NativeHost : null;

        // Non-null when ActualTypeName is the native Loopback type - the
        // native host backing this instance's Transmit(source, sink) call
        // (TcXunit-w5x.15.5's native-stub boundary).
        public LoopbackHost NativeLoopbackHost =>
            NativeKind == NativeHostKind.Loopback ? (LoopbackHost)NativeHost : null;

        // Non-null when ActualTypeName is a native bistable latch type (RS/SR) -
        // the native host backing this instance's SET/RESET->Q1 behavior
        // (TcXunit-ejjl's native-stub boundary).
        public BistableLatchHost NativeBistableLatchHost =>
            NativeKind == NativeHostKind.BistableLatch ? (BistableLatchHost)NativeHost : null;

        // Non-null when ActualTypeName is a native counter type (CTU/CTD/
        // CTUD) - the native host backing this instance's counting behavior
        // (TcXunit-l64b's native-stub boundary).
        public CounterHost NativeCounterHost =>
            NativeKind == NativeHostKind.Counter ? (CounterHost)NativeHost : null;

        // Non-null when ActualTypeName is a native edge-trigger type (R_TRIG/
        // F_TRIG) - the native host backing this instance's CLK->Q behavior
        // (TcXunit-f6b's native-stub boundary).
        public EdgeTriggerHost NativeEdgeTriggerHost =>
            NativeKind == NativeHostKind.Edge ? (EdgeTriggerHost)NativeHost : null;

        public FbInstance(string actualTypeName)
        {
            ActualTypeName = actualTypeName;
        }
    }
}
