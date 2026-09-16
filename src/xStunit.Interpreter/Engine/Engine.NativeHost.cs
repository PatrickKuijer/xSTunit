using System;
using System.Collections.Generic;
using xStunit.Interpreter.Extensibility;

namespace xStunit.Interpreter
{
    // Which native (compiled-only, never-interpreted) stub backs an FbInstance.
    //
    // An FbInstance is backed by at most one native host, so this single
    // discriminator - rather than a set of mutually exclusive nullable host
    // fields that every reader has to null-check in the right order - is what
    // lets the construction site (Engine.NewInstance) and the bare-invoke
    // dispatch site (Engine.Invocation) ask the question in the same
    // vocabulary. A further native stub need only be taught to
    // ClassifyNativeHost below.
    public enum NativeHostKind
    {
        // Ordinary interpreted FB/PROGRAM: its whole ancestry resolves inside
        // the TypeRegistry, so no native stub stands behind it.
        None = 0,

        // TON/TOF/TP/FB_Pulse and their LTIME siblings LTON/LTOF/LTP. One kind,
        // not two: the LTIME trio is backed by the same TimerHost family with
        // the same IN/PT->Q/ET contract and the same bare-invoke binding. Only
        // the PT/ET Cell encoding differs (ns vs ms), and the host owns that, so
        // a second member would buy a dispatch case that behaves identically.
        Timer,

        // R_TRIG/F_TRIG.
        Edge,

        // RS/SR bistable latches.
        BistableLatch,

        // CTU/CTD/CTUD counters.
        Counter,

        // Loopback's fault-injection stub.
        Loopback,

        // A stateful library FB supplied from outside this assembly through
        // IXstunitNativeFunctionBlock. One member for the whole extension
        // point, not one per vendor FB: which plugin backs an instance is the
        // host object's business, so a plugin assembly adds FBs without this
        // enum or the dispatch switch changing again.
        Plugin,

        // Anything whose ancestry leaves the registry without naming a known
        // native FB - i.e. TcUnit.FB_TestSuite.
        Suite,
    }

    public sealed partial class Engine
    {
        // IEC 61131-3 identifiers are case-insensitive, so these sets are keyed
        // OrdinalIgnoreCase: a lowercase or mixed-case base type ('EXTENDS ton')
        // must still be recognized as a native stub instead of falling through
        // to NativeHostKind.Suite.
        //
        // The LTIME trio (LTON/LTOF/LTP) sits in the SAME set as the TIME timers
        // rather than a parallel one - same kind, same TimerHost.Create, same
        // IN/PT inputs. The one thing that differs, the width their PT/ET Cells
        // are boxed at, is owned by TimerHost.ZeroDuration, so nothing here has
        // to know which name is which width.
        private static readonly HashSet<string> NativeTimerTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TON", "TOF", "TP", "FB_Pulse", "LTON", "LTOF", "LTP" };
        private static readonly HashSet<string> NativeEdgeTriggerTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "R_TRIG", "F_TRIG" };
        private static readonly HashSet<string> NativeBistableLatchTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RS", "SR" };
        private static readonly HashSet<string> NativeCounterTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CTU", "CTD", "CTUD" };

        // Compared OrdinalIgnoreCase for the same reason as the sets above.
        private const string NativeLoopbackType = "Loopback";

        // One native classification: the kind, the host instance backing it, and
        // the instance fields that host expects to already exist before its
        // first update. Host and DefaultFields travel together rather than being
        // derived by two switches on the same kind, because a native stub that
        // publishes into Fields nothing seeded fails with a
        // KeyNotFoundException at its first Update, far from the branch that
        // forgot it.
        private readonly struct NativeHostBinding
        {
            private static readonly NativeFieldDeclaration[] NoFields = Array.Empty<NativeFieldDeclaration>();

            public static readonly NativeHostBinding NotNative =
                new NativeHostBinding(NativeHostKind.None, null, NoFields);

            public NativeHostBinding(NativeHostKind kind, object host, IReadOnlyList<NativeFieldDeclaration> defaultFields)
            {
                Kind = kind;
                Host = host;
                DefaultFields = defaultFields;
            }

            public NativeHostKind Kind { get; }

            // Boxed as object because the host types share no base type;
            // FbInstance's typed properties do the (checked-by-Kind) cast back.
            public object Host { get; }

            // Applied to FbInstance.Fields as plain Cells; declaration order
            // is the IEC parameter order of the stub's own VAR_INPUT/
            // VAR_OUTPUT for readability only, nothing depends on it.
            public IReadOnlyList<NativeFieldDeclaration> DefaultFields { get; }

            public static NativeHostBinding Of(NativeHostKind kind, object host, params NativeFieldDeclaration[] defaultFields) =>
                new NativeHostBinding(kind, host, defaultFields);
        }

        private static NativeFieldDeclaration Field(string name, object value) =>
            new NativeFieldDeclaration(name, value);

        // Classifies the type an FbInstance's base-type ancestry terminates on,
        // and produces everything that classification implies.
        //
        // nativeBaseTypeName is the walk's *unresolved* tail: the base type named
        // by the last registry-known POU in the chain but not itself registered
        // (Engine.NewInstance's `current` where its walk breaks). null means the
        // chain ran to its end inside the registry - an ordinary interpreted FB
        // with no native stub behind it.
        //
        // The trailing else is deliberately Suite rather than an error: an
        // unregistered base type is how a suite reaches TcUnit.FB_TestSuite,
        // whose source is never parsed, and the same fallthrough covers any other
        // compiled-only base a fixture happens to extend.
        private NativeHostBinding ClassifyNativeHost(string nativeBaseTypeName)
        {
            if (nativeBaseTypeName == null)
                return NativeHostBinding.NotNative;

            if (NativeTimerTypes.Contains(nativeBaseTypeName))
            {
                // PT/ET are seeded with the host's own zero - 0u for a TIME
                // timer, 0ul for an LTIME one - because the width belongs to the
                // host that will read those Cells back, not to a literal here
                // that would have to be kept in step with TimerHost.Create.
                var timer = TimerHost.Create(nativeBaseTypeName);
                return NativeHostBinding.Of(
                    NativeHostKind.Timer,
                    timer,
                    Field("IN", false),
                    Field("PT", timer.ZeroDuration),
                    Field("Q", false),
                    Field("ET", timer.ZeroDuration));
            }

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

            if (NativeBistableLatchTypes.Contains(nativeBaseTypeName))
            {
                // RS and SR name their inputs differently (SET/RESET1 vs
                // SET1/RESET), so the seeded fields come from the host that will
                // read them back rather than from a literal list here.
                var latch = BistableLatchHost.Create(nativeBaseTypeName);
                return NativeHostBinding.Of(
                    NativeHostKind.BistableLatch,
                    latch,
                    Field(latch.PositionalInputNames[0], false),
                    Field(latch.PositionalInputNames[1], false),
                    Field(BistableLatchHost.OutputName, false));
            }

            if (NativeCounterTypes.Contains(nativeBaseTypeName))
            {
                // Same reason as the latch above, one step further: CTU/CTD/CTUD
                // disagree on their input names (CU/RESET vs CD/LOAD vs all
                // four) AND on their BOOL outputs (Q vs QU+QD), so the seeded
                // fields are enumerated off the host. PV/CV are WORD zeros -
                // ints, per IecNumericType's boxing for WORD.
                var counter = CounterHost.Create(nativeBaseTypeName);
                var counterFields = new List<NativeFieldDeclaration>();

                foreach (var inputName in counter.BooleanInputNames)
                    counterFields.Add(Field(inputName, false));
                counterFields.Add(Field(CounterHost.PresetInputName, 0));
                foreach (var outputName in counter.BooleanOutputNames)
                    counterFields.Add(Field(outputName, false));
                counterFields.Add(Field(CounterHost.CurrentValueOutputName, 0));

                return NativeHostBinding.Of(NativeHostKind.Counter, counter, counterFields.ToArray());
            }

            // Consulted after every in-tree stub and, by construction, only
            // for a name the TypeRegistry already failed to resolve: a plugin
            // can shadow neither interpreted source nor TON/R_TRIG/RS/CTU.
            //
            // The registered object is the prototype; what an instance is
            // backed by is its own CreateInstance product, so two variables of
            // one plugin type share no state.
            if (TryGetNativeFunctionBlock(nativeBaseTypeName, out var prototype))
            {
                var fields = new List<NativeFieldDeclaration>();
                foreach (var field in prototype.Fields)
                    fields.Add(field);

                return NativeHostBinding.Of(NativeHostKind.Plugin, prototype.CreateInstance(), fields.ToArray());
            }

            return NativeHostBinding.Of(NativeHostKind.Suite, new SuiteHost());
        }

        // Whether typeName names one of the native FB stubs this engine can
        // instantiate directly - i.e. whether a VAR declared of that type gets
        // an FbInstance rather than an elementary-type default value.
        //
        // Deliberately NOT ClassifyNativeHost(typeName).Kind != None, which
        // answers a different question: that trailing else is Suite, so every
        // non-null name it sees classifies as native. Correct for "which host
        // kind backs this ancestry tail?", wrong for "is this type name
        // instantiable as a native FB stub?". The two questions share only the
        // list of native base-type names.
        private bool IsNativeFbTypeName(string typeName) =>
            NativeTimerTypes.Contains(typeName)
            || NativeEdgeTriggerTypes.Contains(typeName)
            || NativeBistableLatchTypes.Contains(typeName)
            || NativeCounterTypes.Contains(typeName)
            || string.Equals(typeName, NativeLoopbackType, StringComparison.OrdinalIgnoreCase)
            || TryGetNativeFunctionBlock(typeName, out _);

        // A library namespace qualifier is transparent: Tc2_System.FB_FileOpen
        // names the same block FB_FileOpen does, and real source writes both.
        // Tried only after the spelling as written has missed, so a plugin
        // registered under the qualified name still wins for it.
        //
        // Safe against a false match because it is reached at all only for a
        // type the TypeRegistry could not resolve: there is no interpreted
        // source of any name left for the tail to shadow.
        private bool TryGetNativeFunctionBlock(string typeName, out IXstunitNativeFunctionBlock prototype) =>
            _nativeFunctionBlocks.TryGet(typeName, out prototype)
            || _nativeFunctionBlocks.TryGet(UnqualifiedTail(typeName), out prototype);

        // The segment after the last dot, or null when the name carries no
        // qualifier. Null rather than the name itself, so a caller that already
        // tried the unqualified spelling does not try it twice.
        internal static string UnqualifiedTail(string name)
        {
            var dot = name?.LastIndexOf('.') ?? -1;
            return dot >= 0 ? name.Substring(dot + 1) : null;
        }
    }
}
