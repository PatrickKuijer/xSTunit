using System;
using System.Collections.Generic;

namespace TcXunit.Interpreter
{
    // Which native (compiled-only, never-interpreted) stub backs an
    // FbInstance - TcXunit-j98's single discriminator.
    //
    // An FbInstance can be backed by at most one native host, so the four
    // mutually exclusive nullable host fields FbInstance used to carry are
    // really one tagged union wearing four hats: every reader had to
    // re-derive the tag by null-checking the fields in the right order, which
    // spread the same classification across the construction site
    // (Engine.NewInstance) and the bare-invoke dispatch site
    // (Engine.Invocation). One kind + one host object states it once, so both
    // sites ask the question in the same vocabulary and a fifth native stub
    // only ever has to be taught to ClassifyNativeHost below.
    public enum NativeHostKind
    {
        // Ordinary interpreted FB/PROGRAM: its whole ancestry resolves inside
        // the TypeRegistry, so no native stub stands behind it.
        None = 0,

        // TON/TOF/FB_Pulse (TcXunit-w5x.15.7).
        Timer,

        // R_TRIG/F_TRIG (TcXunit-f6b).
        Edge,

        // Loopback's fault-injection stub (TcXunit-w5x.15.5).
        Loopback,

        // Anything whose ancestry leaves the registry without naming a known
        // native FB - i.e. TcUnit.FB_TestSuite (TcXunit-w5x.7).
        Suite,
    }

    public sealed partial class Engine
    {
        // TcXunit-nch: IEC 61131-3 identifiers are case-insensitive (same
        // decision as TcXunit-fzm's elementary-type lookups), so these two
        // native-FB base-type sets are keyed with OrdinalIgnoreCase - a
        // lowercase/mixed-case base type (e.g. 'EXTENDS ton') must still be
        // recognized as a native timer/edge-trigger stub instead of falling
        // through to NativeHostKind.Suite. Engine.Defaults' "is this type
        // name instantiable at all" check reads the same two sets, so they
        // live with the classifier that owns their meaning rather than in
        // Engine.cs.
        private static readonly HashSet<string> NativeTimerTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TON", "TOF", "TP", "FB_Pulse" };
        private static readonly HashSet<string> NativeEdgeTriggerTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "R_TRIG", "F_TRIG" };

        // Single-member "set" for symmetry with the two above; matched
        // OrdinalIgnoreCase for the same TcXunit-nch reason.
        private const string NativeLoopbackType = "Loopback";

        // One native classification: the kind, the host instance that backs
        // it, and the instance fields that host expects to already exist
        // before it is first updated. Host and DefaultFields are deliberately
        // carried together (rather than derived by two switches on the same
        // kind, which is what NewInstance used to do inline) - a native stub
        // that publishes into Fields it never declared would fail with a
        // KeyNotFoundException at its first Update, far from the branch that
        // forgot to seed it.
        private readonly struct NativeHostBinding
        {
            private static readonly KeyValuePair<string, object>[] NoFields = Array.Empty<KeyValuePair<string, object>>();

            public static readonly NativeHostBinding NotNative =
                new NativeHostBinding(NativeHostKind.None, null, NoFields);

            public NativeHostBinding(NativeHostKind kind, object host, IReadOnlyList<KeyValuePair<string, object>> defaultFields)
            {
                Kind = kind;
                Host = host;
                DefaultFields = defaultFields;
            }

            public NativeHostKind Kind { get; }

            // Boxed as object because the four host types share no base type -
            // FbInstance's typed properties do the (checked-by-Kind) cast back.
            public object Host { get; }

            // Applied to FbInstance.Fields as plain Cells; declaration order
            // is the IEC parameter order of the stub's own VAR_INPUT/
            // VAR_OUTPUT for readability only, nothing depends on it.
            public IReadOnlyList<KeyValuePair<string, object>> DefaultFields { get; }

            public static NativeHostBinding Of(NativeHostKind kind, object host, params KeyValuePair<string, object>[] defaultFields) =>
                new NativeHostBinding(kind, host, defaultFields);
        }

        private static KeyValuePair<string, object> Field(string name, object value) =>
            new KeyValuePair<string, object>(name, value);

        // Classifies the type an FbInstance's base-type ancestry chain
        // terminates on and produces everything that classification implies.
        //
        // nativeBaseTypeName is the walk's *unresolved* tail: the base type
        // named by the last registry-known POU in the chain but not itself
        // registered (Engine.NewInstance's `current` at the point its walk
        // breaks). null means the chain ran to its end inside the registry -
        // an ordinary interpreted FB with no native stub behind it.
        //
        // The trailing else is deliberately Suite rather than an error: an
        // unregistered base type is how a suite reaches TcUnit.FB_TestSuite,
        // whose source is never parsed, and the same fallthrough covers any
        // other compiled-only base a fixture happens to extend.
        private static NativeHostBinding ClassifyNativeHost(string nativeBaseTypeName)
        {
            if (nativeBaseTypeName == null)
                return NativeHostBinding.NotNative;

            if (NativeTimerTypes.Contains(nativeBaseTypeName))
                return NativeHostBinding.Of(
                    NativeHostKind.Timer,
                    TimerHost.Create(nativeBaseTypeName),
                    Field("IN", false),
                    Field("PT", 0u),
                    Field("Q", false),
                    Field("ET", 0u));

            if (string.Equals(nativeBaseTypeName, NativeLoopbackType, StringComparison.OrdinalIgnoreCase))
                return NativeHostBinding.Of(
                    NativeHostKind.Loopback,
                    new LoopbackHost(),
                    Field("LinkUp", true),
                    Field("LastUpdateTime", 0L));

            if (NativeEdgeTriggerTypes.Contains(nativeBaseTypeName))
                return NativeHostBinding.Of(
                    NativeHostKind.Edge,
                    EdgeTriggerHost.Create(nativeBaseTypeName),
                    Field("CLK", false),
                    Field("Q", false));

            return NativeHostBinding.Of(NativeHostKind.Suite, new TcUnitSuiteHost());
        }

        // Whether typeName names one of the native FB stubs this engine can
        // instantiate directly - i.e. whether a VAR declared of that type gets
        // an FbInstance rather than an elementary-type default value
        // (Engine.Defaults' only caller).
        //
        // TcXunit-d6qq: deliberately NOT ClassifyNativeHost(typeName).Kind !=
        // None. That would answer a different question: ClassifyNativeHost's
        // trailing else is Suite, so *every* non-null name it sees classifies
        // as native - correct for "which host kind backs this ancestry tail?",
        // wrong for "is this type name instantiable as a native FB stub?".
        // The two questions stay separate; what they share is this one list of
        // native base-type names, which is what the duplication was really
        // about (Engine.Defaults used to re-spell all three checks inline,
        // Loopback as a bare string literal).
        private static bool IsNativeFbTypeName(string typeName) =>
            NativeTimerTypes.Contains(typeName)
            || NativeEdgeTriggerTypes.Contains(typeName)
            || string.Equals(typeName, NativeLoopbackType, StringComparison.OrdinalIgnoreCase);
    }
}
