using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TcXunit.Runner.TcUnitStub;

namespace TcXunit.Interpreter
{
    // Drives instantiation, virtual/non-virtual method dispatch, and
    // statement/expression execution over a TypeRegistry built from
    // TcXunit.Parser's AST. Scoped to the FB_CounterTests fixture's needs
    // (TcXunit-w5x.8/.12) - grow-on-demand, not the full v1 grammar.
    public sealed partial class Engine
    {
        private readonly TypeRegistry _registry;

        // One Cell-backed field dictionary per registered GVL, keyed by GVL
        // name (TcXunit-71o) - built once at construction (not lazily per
        // access) since default-value construction can itself recurse into
        // NewInstance/other GVLs' struct types via _registry, same as
        // instance Fields. No TwinCAT GVL init-cycle/task-binding semantics
        // are modeled, just zero-initialized storage per declared type.
        private readonly Dictionary<string, Dictionary<string, Cell>> _globals = new Dictionary<string, Dictionary<string, Cell>>();

        // Shared process-wide simulated clock (TcXunit-w5x.15.7 / T3 design) -
        // one Clock for the whole Engine, not per-instance; TON/TOF/FB_Pulse
        // native hosts read Clock.TotalMs whenever they're invoked.
        public Clock Clock { get; } = new Clock();

        // The native-FB base-type sets (NativeTimerTypes/
        // NativeEdgeTriggerTypes/NativeLoopbackType) and the classifier that
        // reads them live in Engine.NativeHost.cs (TcXunit-j98).

        // Host-supplied stand-ins for compiled-only TwinCAT library functions
        // (TcXunit-6k2). Optional and consulted last (Engine.Invocation.cs), so
        // an Engine built without one behaves exactly as before: every
        // unresolved call stays an error. Never null past the constructor, so
        // the dispatch site doesn't need its own null check.
        private readonly Extensibility.NativeFunctionRegistry _nativeFunctions;

        public Engine(TypeRegistry registry)
            : this(registry, null)
        {
        }

        public Engine(TypeRegistry registry, Extensibility.NativeFunctionRegistry nativeFunctions)
        {
            _registry = registry;
            _nativeFunctions = nativeFunctions ?? new Extensibility.NativeFunctionRegistry();

            // Two-pass construction (TcXunit-09s): every GVL's Cells are
            // allocated and registered in _globals *before* any default
            // value is computed, so a default-value expression that refers
            // to another GVL's (or its own GVL's) member - qualified or
            // unqualified - always finds a Cell to resolve against,
            // regardless of GvlNames iteration order.
            foreach (var gvlName in _registry.GvlNames)
            {
                var fields = new Dictionary<string, Cell>();
                foreach (var decl in _registry.GetGvlDecls(gvlName))
                    fields[decl.Name] = new Cell { DeclaredTypeName = decl.TypeName };
                _globals[gvlName] = fields;
            }

            // A single GVL decl whose default-value expression can't be
            // resolved (e.g. a forward reference to another GVL's constant
            // that hasn't been computed yet, or a genuinely unknown
            // identifier) is retried in later passes rather than aborting
            // construction for every other GVL/suite - mirrors
            // SuiteCaseRunner's per-suite discovery isolation (TcXunit-654).
            // Cross-GVL constant references can appear in either
            // registration order (TcXunit-09s): a forward reference doesn't
            // throw (the referenced Cell already exists from the
            // allocation pass above, just still holding its null default),
            // it silently reads a not-yet-computed value - so convergence
            // can't be detected from exceptions alone. Instead, every decl
            // is recomputed on every pass (idempotent: default-value
            // expressions are side-effect-free reads of constants/literals)
            // for up to one pass per decl, an upper bound on the longest
            // possible dependency chain; anything still throwing after that
            // is a genuinely unresolvable reference and keeps its
            // zero-initialized (null) value.
            var allDecls = new List<(string GvlName, VarDecl Decl)>();
            foreach (var gvlName in _registry.GvlNames)
                foreach (var decl in _registry.GetGvlDecls(gvlName))
                    allDecls.Add((gvlName, decl));

            for (var pass = 0; pass < allDecls.Count; pass++)
            {
                foreach (var (gvlName, decl) in allDecls)
                {
                    try
                    {
                        _globals[gvlName][decl.Name].Value = DefaultValue(decl, null);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        public IReadOnlyList<TestCaseResult> RunSuite(string suiteTypeName) =>
            RunSuite(suiteTypeName, out _);

        // TcXunit-6fb.2: suite-level counterpart to TestCaseResult.ElapsedMilliseconds
        // (TcXunit-6fb.1) - wraps the same instantiate/execute call this type's
        // parameterless overload makes, but times it so CliRunner's JSON output can
        // report suites[].durationMs. An out-param overload (rather than changing
        // RunSuite's existing return type) keeps every pre-existing call site -
        // SuiteCaseRunner.cs and the many Engine tests that only care about the
        // IReadOnlyList<TestCaseResult> - compiling unchanged; only call sites that
        // actually want the duration opt into this overload.
        public IReadOnlyList<TestCaseResult> RunSuite(string suiteTypeName, out long elapsedMilliseconds)
        {
            var stopwatch = Stopwatch.StartNew();

            // TcXunit-p3t.1: the one place interpreter faults are wrapped with
            // their PLC source location. It is deliberately the OUTERMOST
            // boundary rather than every CallMethod level, for two reasons:
            // the innermost body has already stamped itself onto the exception
            // by the time it gets here (see Engine.Diagnostics.cs), and public
            // Engine.CallMethod keeps throwing the exact exception types its
            // callers already switch on. Instantiation is inside the try too -
            // an FB_init body is interpreted ST and can fault just as the
            // suite body can.
            try
            {
                var instance = NewInstance(suiteTypeName);
                var def = _registry.Get(suiteTypeName);
                ResetTopLevelTempFields(instance);
                // A suite body is a POU body, not a METHOD, so the frame
                // carries no method name - a fault here reports just "FB_X".
                // TcXunit-n65: GetStatements is resolved lazily inside
                // ExecuteBody's own try (see Engine.Diagnostics.cs), not
                // eagerly here, so a lazy parse failure in the suite's own
                // body attributes to the suite rather than escaping
                // unattributed past this outermost boundary.
                // TcXunit-3tx.3: ExecuteSuiteBody, not ExecuteBody - a fault
                // inside one test's bracket fails that test and lets the rest
                // of the suite run, instead of discarding every result.
                ExecuteSuiteBody(
                    () => _registry.GetStatements(def.ImplementationText),
                    new Frame(instance, suiteTypeName, null, def.BodyStartLine),
                    instance.NativeSuiteHost);
                stopwatch.Stop();
                elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                return instance.NativeSuiteHost.Collect();
            }
            catch (Exception ex)
            {
                var located = TryCreateSourceLocationException(ex);
                if (located != null)
                    throw located;

                // Nothing ST-level claimed it (e.g. an unresolvable type hit
                // while building default values): rethrow untouched rather
                // than inventing a location.
                throw;
            }
        }

        public FbInstance NewInstance(string typeName)
        {
            var instance = new FbInstance(typeName);

            var chain = new List<string>();
            var current = typeName;
            var nativeBoundaryHit = false;
            while (current != null)
            {
                var def = _registry.Get(current);
                if (def == null)
                {
                    nativeBoundaryHit = true;
                    break;
                }
                chain.Add(current);
                current = def.BaseTypeName;
            }

            if (nativeBoundaryHit)
            {
                if (NativeTimerTypes.Contains(current))
                {
                    instance.NativeTimerHost = TimerHost.Create(current);
                    instance.Fields["IN"] = new Cell { Value = false };
                    instance.Fields["PT"] = new Cell { Value = 0u };
                    instance.Fields["Q"] = new Cell { Value = false };
                    instance.Fields["ET"] = new Cell { Value = 0u };
                }
                else if (string.Equals(current, "Loopback", StringComparison.OrdinalIgnoreCase))
                {
                    instance.NativeLoopbackHost = new LoopbackHost();
                    instance.Fields["LinkUp"] = new Cell { Value = true };
                    instance.Fields["LastUpdateTime"] = new Cell { Value = 0L };
                }
                else if (NativeEdgeTriggerTypes.Contains(current))
                {
                    instance.NativeEdgeTriggerHost = EdgeTriggerHost.Create(current);
                    instance.Fields["CLK"] = new Cell { Value = false };
                    instance.Fields["Q"] = new Cell { Value = false };
                }
                else
                {
                    instance.NativeSuiteHost = new TcUnitSuiteHost();
                }
            }

            for (var i = chain.Count - 1; i >= 0; i--)
            {
                var def = _registry.Get(chain[i]);
                // VAR_INPUT/VAR_OUTPUT/VAR_IN_OUT sections must persist as
                // instance Fields alongside VAR (Local), same as the
                // hardcoded native timer/loopback IN/PT/Q/ET fields above -
                // otherwise dot-access and StepCycles-internal references to
                // a nested FB's own inputs/outputs never resolve
                // (TcXunit-0v1).
                foreach (var decl in _registry.GetDecls(def.DeclarationText).Where(IsPersistedField))
                {
                    instance.Fields[decl.Name] = CreateFieldCell(decl, instance);
                    instance.FieldTypeNames[decl.Name] = decl.TypeName;
                }
            }

            CallMethod(instance, "FB_init", Array.Empty<Expr>(), Array.Empty<NamedArg>(), null, null, optionalIfMissing: true);

            return instance;
        }

        // TcXunit-mxx: builds the Cell that backs one FbInstance field.
        // Plain (non-FB) fields construct eagerly, same as before - they're
        // O(1) with no further recursion. A field declared as another
        // registry-known POU type (FB/PROGRAM) is wrapped in a LazyCell
        // instead: its own DefaultValue would call NewInstance for that
        // type, repeating this same field walk for ITS fields, transitively
        // through however much of the type graph is reachable - regardless
        // of whether the code that declared the outer instance ever reads
        // this particular field. Deferring that inner construction until
        // the field is actually dereferenced scopes the work (and any fault
        // inside it, e.g. an unsupported construct several types away) to
        // callers that actually touch the field. Native stub types (TON,
        // Loopback, R_TRIG/F_TRIG, ...) aren't registry types - DefaultValue
        // handles those via NativeTimerTypes/NativeEdgeTriggerTypes/
        // "Loopback" and NewInstance's own nativeBoundaryHit branch above,
        // neither of which recurses, so they stay eager too.
        private Cell CreateFieldCell(VarDecl decl, FbInstance owningInstance)
        {
            if (_registry.Get(_registry.ResolveAlias(decl.TypeName)) != null)
                return new LazyCell(() => DefaultValue(decl, owningInstance), decl.TypeName);

            return new Cell { Value = DefaultValue(decl, owningInstance), DeclaredTypeName = decl.TypeName };
        }

        // Fields materialized at NewInstance() time (VAR_INPUT/VAR_OUTPUT/
        // VAR_IN_OUT alongside VAR/Local) - the set that must be visible to
        // dot-access (TcXunit-0v1). VAR/Input/Output/InOut persist across
        // calls/StepCycles. VAR_TEMP declared at a FB/PROGRAM's own top
        // level is included here too (methods reach it only through
        // instance.Fields, same as any other top-level field), but unlike
        // the others it is NOT meant to persist - ResetTopLevelTempFields
        // resets it to default before every top-level body invocation so it
        // behaves like a fresh local rather than a persisted VAR field
        // (TcXunit-9go).
        private static bool IsPersistedField(VarDecl decl) =>
            decl.Section == VarSection.Local ||
            decl.Section == VarSection.Input ||
            decl.Section == VarSection.Output ||
            decl.Section == VarSection.InOut ||
            decl.Section == VarSection.Temp;

        // Resets every VAR_TEMP field declared directly in instance's own
        // type ancestry's top-level declaration (as opposed to a METHOD's -
        // those already reset per call via BindParams/Frame.Locals) back to
        // its default value. Called immediately before each fresh
        // invocation of a POU's own top-level body (RunSuite's single run,
        // each StepCycles cycle, each bare/InvokeFbInstance call) so
        // FB/PROGRAM-top-level VAR_TEMP never carries a value over from a
        // previous invocation, matching IEC 61131-3 VAR_TEMP semantics
        // instead of persisting like a real VAR field (TcXunit-9go).
        private void ResetTopLevelTempFields(FbInstance instance)
        {
            var chain = new List<string>();
            var current = instance.ActualTypeName;
            while (current != null)
            {
                var def = _registry.Get(current);
                if (def == null)
                    break;
                chain.Add(current);
                current = def.BaseTypeName;
            }

            for (var i = chain.Count - 1; i >= 0; i--)
            {
                var def = _registry.Get(chain[i]);
                foreach (var decl in _registry.GetDecls(def.DeclarationText).Where(d => d.Section == VarSection.Temp))
                    instance.Fields[decl.Name].Value = DefaultValue(decl, instance);
            }
        }

        private static int ToCaseInt(object value) => value switch
        {
            int i => i,
            bool b => b ? 1 : 0,
            _ => Convert.ToInt32(value),
        };

        private static readonly HashSet<string> LoopbackFaultMethods = new HashSet<string>
        {
            "Transmit", "Drop", "Restore", "Freeze", "SetDelay", "Duplicate", "Corrupt",
        };

        private static bool IsLoopbackFaultMethod(string methodName) => LoopbackFaultMethods.Contains(methodName);

        // A FieldAccessExpr's receiver is a bare GVL name (e.g.
        // gScratchGlobals.stWidget) rather than a variable/field in scope
        // (TcXunit-71o) - checked the same way as the BuiltinEnums.Types
        // check above: only when the identifier doesn't already resolve as
        // a local/instance field, so a same-named local/field always wins.
        private bool TryGetGvlFields(FieldAccessExpr fieldAccess, Frame frame, out Dictionary<string, Cell> fields)
        {
            if (fieldAccess.Receiver is IdentifierExpr gvlId &&
                frame.ResolveCell(gvlId.Name) == null &&
                _globals.TryGetValue(gvlId.Name, out fields))
                return true;

            fields = null;
            return false;
        }

        // TcXunit-09s: a bare (unqualified) identifier that isn't a local/
        // instance field may still be a GVL member referenced without its
        // GvlName. prefix - legal IEC 61131-3 (global scope is visible
        // everywhere), and something TryGetGvlFields doesn't cover since it
        // only handles the qualified GvlName.Member shape. Searches every
        // registered GVL's field dictionary for a matching name; first match
        // wins (no cross-GVL name-collision detection, same level of rigor
        // as the rest of v1).
        private bool TryResolveGlobalCell(string name, out Cell cell)
        {
            foreach (var fields in _globals.Values)
            {
                if (fields.TryGetValue(name, out cell))
                    return true;
            }

            cell = null;
            return false;
        }
    }
}
