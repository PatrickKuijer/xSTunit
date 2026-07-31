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

        // The four typed views over that pair. Each yields its host only when
        // NativeKind agrees, so a caller that reaches for the wrong one gets
        // null rather than an InvalidCastException - identical to the
        // null-valued field it used to read.

        // Non-null when ActualTypeName's ancestry reaches TcUnit.FB_TestSuite -
        // the native C# stub instance backing TEST()/AssertEquals_INT()/etc for
        // this instance (TcXunit-w5x.7's native-stub boundary).
        public TcUnitSuiteHost NativeSuiteHost
        {
            get => NativeKind == NativeHostKind.Suite ? (TcUnitSuiteHost)NativeHost : null;
            set => SetNativeHost(NativeHostKind.Suite, value);
        }

        // Non-null when ActualTypeName is a native timer type (TON/TOF/
        // FB_Pulse) - the native host backing this instance's IN/PT->Q/ET
        // behavior (TcXunit-w5x.15.7's native-stub boundary).
        public TimerHost NativeTimerHost
        {
            get => NativeKind == NativeHostKind.Timer ? (TimerHost)NativeHost : null;
            set => SetNativeHost(NativeHostKind.Timer, value);
        }

        // Non-null when ActualTypeName is the native Loopback type - the
        // native host backing this instance's Transmit(source, sink) call
        // (TcXunit-w5x.15.5's native-stub boundary).
        public LoopbackHost NativeLoopbackHost
        {
            get => NativeKind == NativeHostKind.Loopback ? (LoopbackHost)NativeHost : null;
            set => SetNativeHost(NativeHostKind.Loopback, value);
        }

        // Non-null when ActualTypeName is a native edge-trigger type (R_TRIG/
        // F_TRIG) - the native host backing this instance's CLK->Q behavior
        // (TcXunit-f6b's native-stub boundary).
        public EdgeTriggerHost NativeEdgeTriggerHost
        {
            get => NativeKind == NativeHostKind.Edge ? (EdgeTriggerHost)NativeHost : null;
            set => SetNativeHost(NativeHostKind.Edge, value);
        }

        // Assigning a host adopts its kind; assigning null clears back to
        // None, so `inst.NativeTimerHost = null` still leaves every typed
        // view reading null exactly as it did when they were four fields.
        private void SetNativeHost(NativeHostKind kind, object host)
        {
            NativeKind = host == null ? NativeHostKind.None : kind;
            NativeHost = host;
        }

        public FbInstance(string actualTypeName)
        {
            ActualTypeName = actualTypeName;
        }
    }
}
