using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using xStunit.Runner.TcUnitStub;

namespace xStunit.Interpreter
{
    // Drives instantiation, virtual/non-virtual method dispatch, and
    // statement/expression execution over a TypeRegistry built from
    // xStunit.Parser's AST. Deliberately covers only the ST the fixtures
    // actually use and is extended construct by construct - a gap here is a
    // grammar not yet taught, not a defect.
    public sealed partial class Engine
    {
        private readonly TypeRegistry _registry;

        // One Cell-backed field dictionary per registered GVL, keyed by GVL
        // name. Built once at construction rather than lazily per access,
        // because default-value construction can itself recurse into
        // NewInstance and other GVLs' struct types through _registry. No
        // TwinCAT GVL init-cycle/task-binding semantics are modeled, just
        // zero-initialized storage per declared type.
        private readonly Dictionary<string, Dictionary<string, Cell>> _globals = new Dictionary<string, Dictionary<string, Cell>>();

        // Global Cells whose own default value is not settled yet. Membership
        // is reference identity, Cell declaring no value equality of its own.
        // Populated during construction and emptied when it finishes, so it
        // costs nothing once the Engine is running.
        private readonly HashSet<Cell> _unsettledGlobals = new HashSet<Cell>();
        private bool _settlingGlobals;
        private bool _readUnsettledGlobal;

        // One simulated clock for the whole Engine, not one per instance: the
        // TON/TOF/TP and LTON/LTOF/LTP native hosts read Clock.TotalNs whenever
        // they are invoked, and ns is the base unit so the LTIME trio can
        // express a sub-millisecond PT.
        public Clock Clock { get; } = new Clock();

        // Host-supplied stand-ins for compiled-only TwinCAT library functions.
        // Optional and consulted last (Engine.Invocation.cs), so an Engine built
        // without one leaves every unresolved call an error. Never null past the
        // constructor, so the dispatch site needs no null check of its own.
        private readonly Extensibility.NativeFunctionRegistry _nativeFunctions;

        // The same arrangement for stateful library FUNCTION_BLOCKs and for the
        // named constants those libraries publish. Three registries rather than
        // one lookup, because each answers a different question at a different
        // point: a function name only matters once a call has failed to
        // resolve, a block type name only while an instance is constructed, a
        // constant only once an identifier has failed every real scope.
        private readonly Extensibility.NativeFunctionBlockRegistry _nativeFunctionBlocks;

        private readonly Extensibility.NativeConstantRegistry _nativeConstants;

        // The machine the code under test is compiled for, which only the
        // layout rules read: it is what makes an address 4 bytes or 8, and so
        // what SIZEOF and the MEMCPY byte image answer.
        private readonly TargetPlatform _target;

        public Engine(TypeRegistry registry)
            : this(registry, new Extensibility.NativePlugins(), TargetPlatform.Default)
        {
        }

        public Engine(TypeRegistry registry, Extensibility.NativeFunctionRegistry nativeFunctions)
            : this(registry, nativeFunctions, TargetPlatform.Default)
        {
        }

        public Engine(
            TypeRegistry registry, Extensibility.NativeFunctionRegistry nativeFunctions, TargetPlatform target)
            : this(registry, PluginsWith(nativeFunctions), target)
        {
        }

        // Adopted rather than copied, so the registry's own duplicate-message
        // attribution survives the wrapping.
        private static Extensibility.NativePlugins PluginsWith(Extensibility.NativeFunctionRegistry nativeFunctions) =>
            new Extensibility.NativePlugins(nativeFunctions);

        public Engine(TypeRegistry registry, Extensibility.NativePlugins plugins)
            : this(registry, plugins, TargetPlatform.Default)
        {
        }

        // The one real constructor. The NativeFunctionRegistry overloads above
        // predate the other extension points and stay because most callers only
        // ever needed functions; anything supplying more than that passes a
        // NativePlugins, so a further extension point costs no new overload.
        public Engine(TypeRegistry registry, Extensibility.NativePlugins plugins, TargetPlatform target)
        {
            plugins = plugins ?? new Extensibility.NativePlugins();

            _registry = registry;
            _target = target;
            _nativeFunctions = plugins.Functions;
            _nativeFunctionBlocks = plugins.FunctionBlocks;
            _nativeConstants = plugins.Constants;

            // Every GVL's Cells are allocated and registered in _globals
            // *before* any default value is computed, so a default-value
            // expression referring to another GVL's (or its own GVL's) member -
            // qualified or unqualified - always finds a Cell to resolve
            // against, regardless of GvlNames iteration order.
            foreach (var gvlName in _registry.GvlNames)
            {
                var fields = new Dictionary<string, Cell>();
                foreach (var decl in _registry.GetGvlDecls(gvlName))
                {
                    var cell = new Cell { DeclaredTypeName = decl.TypeName };
                    fields[decl.Name] = cell;
                    _unsettledGlobals.Add(cell);
                }

                _globals[gvlName] = fields;
            }

            // A decl whose default-value expression can't be resolved is
            // retried in later passes rather than aborting construction for
            // every other GVL and suite, mirroring SuiteCaseRunner's per-suite
            // discovery isolation.
            //
            // Convergence cannot be detected from exceptions alone: a forward
            // reference to another GVL's constant doesn't throw, because the
            // allocation pass above already created that Cell - it silently
            // reads a not-yet-computed value. What makes a value final is
            // therefore not that it evaluated, but that it evaluated without
            // reading anything still unsettled, which is what _unsettledGlobals
            // and NoteGlobalRead track. A decl clearing that bar is settled and
            // never revisited, so the number of passes follows the depth of the
            // dependency chains rather than the number of globals - the
            // difference between linear and quadratic work in how many globals
            // a workspace happens to declare.
            var pending = new List<(string GvlName, VarDecl Decl)>();
            foreach (var gvlName in _registry.GvlNames)
                foreach (var decl in _registry.GetGvlDecls(gvlName))
                    pending.Add((gvlName, decl));

            _settlingGlobals = true;
            try
            {
                while (pending.Count > 0)
                {
                    var unsettled = new List<(string GvlName, VarDecl Decl)>();
                    foreach (var entry in pending)
                    {
                        var cell = _globals[entry.GvlName][entry.Decl.Name];
                        _readUnsettledGlobal = false;
                        try
                        {
                            // Capacity is re-resolved alongside the value for
                            // the same reason: a STRING sized by another GVL's
                            // constant cannot be settled until that constant
                            // has one, and the allocation pass above
                            // deliberately ran before any of them did.
                            cell.StringCapacity = ResolveStringCapacity(entry.Decl.TypeName, null);
                            cell.Value = DefaultValue(entry.Decl, null);
                        }
                        catch (Exception)
                        {
                            unsettled.Add(entry);
                            continue;
                        }

                        // The value is written through even when it turns out
                        // to be unsettled, so it stands as this pass's best
                        // effort if the loop stops short of resolving it.
                        if (_readUnsettledGlobal)
                            unsettled.Add(entry);
                        else
                            _unsettledGlobals.Remove(cell);
                    }

                    // A pass that settles nothing new never will: nothing
                    // outside this set can change between passes, so every
                    // decl still here - whether blocked on a cycle, on a
                    // reference nothing defines, or on an initializer that
                    // throws for reasons of its own - would evaluate exactly
                    // the same way again. They keep the zero-initialized
                    // (null) or best-effort value they already hold.
                    if (unsettled.Count == pending.Count)
                        break;

                    pending = unsettled;
                }
            }
            finally
            {
                _settlingGlobals = false;
                _unsettledGlobals.Clear();
            }
        }

        // Records that a global Cell read while construction was seeding
        // defaults has no settled default of its own yet, which disqualifies
        // the value being computed around it from counting as final. Safe to
        // call with any Cell: one that is not an unsettled global is ignored.
        private void NoteGlobalRead(Cell cell)
        {
            if (_settlingGlobals && _unsettledGlobals.Contains(cell))
                _readUnsettledGlobal = true;
        }

        public IReadOnlyList<TestCaseResult> RunSuite(string suiteTypeName) =>
            RunSuite(suiteTypeName, out _);

        public IReadOnlyList<TestCaseResult> RunSuite(string suiteTypeName, out long elapsedMilliseconds) =>
            RunSuite(suiteTypeName, out elapsedMilliseconds, out _);

        // completedTests is the only way to reach the tests that finished before
        // a fault ended the suite: they are recorded on the suite host, which
        // goes out of scope with the exception. It is written BEFORE the throw,
        // so a caller whose variable is assigned before the call reads it from
        // its own catch block - a fault outside a TEST()/TEST_FINISHED() bracket
        // does not un-run the tests before it, and reporting them as if it did
        // says "nothing passed" about a run where something did.
        public IReadOnlyList<TestCaseResult> RunSuite(
            string suiteTypeName, out long elapsedMilliseconds, out IReadOnlyList<TestCaseResult> completedTests)
        {
            var stopwatch = Stopwatch.StartNew();
            completedTests = Array.Empty<TestCaseResult>();

            // Held outside the try so the catch below can still reach the host
            // after the statement that built it faulted.
            SuiteHost host = null;

            // The one place interpreter faults are wrapped with their PLC source
            // location, and deliberately the OUTERMOST boundary rather than every
            // CallMethod level: the innermost body has already stamped itself
            // onto the exception by the time it arrives here (see
            // Engine.Diagnostics.cs), and public Engine.CallMethod goes on
            // throwing the exact exception types its callers switch on.
            // Instantiation is inside the try too - an FB_init body is
            // interpreted ST and can fault just as the suite body can.
            try
            {
                var instance = NewInstance(suiteTypeName);
                host = instance.NativeSuiteHost;
                var def = _registry.Get(suiteTypeName);
                ResetTopLevelTempFields(instance);

                // A suite body is one PLC cycle, so it latches the task start
                // the same way StepCycles does. Without this a test advancing
                // the clock would move "when did this cycle start" along with
                // "what time is it", and the two library functions that
                // distinguish them would answer identically.
                Clock.BeginCycle();
                // A suite body is a POU body, not a METHOD, so the frame
                // carries no method name - a fault here reports just "FB_X".
                // GetStatements is passed lazily so it resolves inside
                // ExecuteSuiteBody's own try: a parse failure in the suite's own
                // body then attributes to the suite instead of escaping
                // unattributed past this outermost boundary. ExecuteSuiteBody
                // rather than ExecuteBody, so a fault inside one test's bracket
                // fails that test and lets the rest of the suite run.
                ExecuteSuiteBody(
                    () => _registry.GetStatements(def.ImplementationText),
                    new Frame(instance, suiteTypeName, null, def.BodyStartLine),
                    host);
                stopwatch.Stop();
                elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                completedTests = host.Collect();
                return completedTests;
            }
            catch (Exception ex)
            {
                // CompletedTests, not Collect(): Collect() re-runs the
                // end-of-suite check and would throw over a bracket this fault
                // left open, replacing the fault being reported with a
                // complaint about its own side effect.
                completedTests = host?.CompletedTests ?? Array.Empty<TestCaseResult>();

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
            while (current != null)
            {
                var def = _registry.Get(current);
                if (def == null)
                    break;
                chain.Add(current);
                current = def.BaseTypeName;
            }

            // current is now the walk's unresolved tail - the base type this
            // ancestry names but the registry doesn't know - or null when the
            // chain ran to its end inside the registry. That single value is
            // the whole input to native classification: kind, host, and the
            // fields that host expects to find already seeded all come back
            // together from ClassifyNativeHost, so the host-building and
            // field-seeding halves cannot drift apart.
            var nativeHost = ClassifyNativeHost(current);
            instance.NativeKind = nativeHost.Kind;
            instance.NativeHost = nativeHost.Host;
            foreach (var field in nativeHost.DefaultFields)
            {
                // A field that named its IEC type goes through the same
                // construction as a real declaration, so a plugin's
                // STRING(255) truncates where the declaration says it does;
                // the in-tree stubs name no type and keep the untyped Cell
                // they have always had.
                instance.Fields[field.Name] = field.DeclaredTypeName == null
                    ? new Cell { Value = field.DefaultValue }
                    : NewDeclaredCell(field.DefaultValue, field.DeclaredTypeName, instance);

                if (field.DeclaredTypeName != null)
                    instance.FieldTypeNames[field.Name] = field.DeclaredTypeName;
            }

            for (var i = chain.Count - 1; i >= 0; i--)
            {
                var def = _registry.Get(chain[i]);
                // VAR_INPUT/VAR_OUTPUT/VAR_IN_OUT must persist as instance
                // Fields alongside VAR (Local), or dot-access and
                // StepCycles-internal references to a nested FB's own
                // inputs/outputs never resolve.
                foreach (var decl in _registry.GetDecls(def.DeclarationText).Where(IsPersistedField))
                {
                    instance.Fields[decl.Name] = CreateFieldCell(decl, instance);
                    instance.FieldTypeNames[decl.Name] = decl.TypeName;
                }
            }

            CallMethod(instance, "FB_init", Array.Empty<Expr>(), Array.Empty<NamedArg>(), null, null, optionalIfMissing: true);

            return instance;
        }

        // A field declared as another registry-known POU type (FB/PROGRAM) is
        // wrapped in a LazyCell: its DefaultValue would call NewInstance for
        // that type, repeating this same field walk transitively through
        // however much of the type graph is reachable, whether or not the
        // declaring code ever reads the field. Deferring until the field is
        // dereferenced scopes that work - and any fault inside it, e.g. an
        // unsupported construct several types away - to callers that actually
        // touch it.
        //
        // Everything else stays eager: plain fields are O(1) with no further
        // recursion, and native stub types (TON, Loopback, R_TRIG/F_TRIG, ...)
        // are not registry types, so DefaultValue resolves them through the
        // non-recursing native-stub path instead.
        private Cell CreateFieldCell(VarDecl decl, FbInstance owningInstance)
        {
            if (IsDeferredFieldType(decl.TypeName))
                return new LazyCell(() => DefaultValue(decl, owningInstance), decl.TypeName);

            return NewDeclaredCell(DefaultValue(decl, owningInstance), decl.TypeName, owningInstance);
        }

        // An ARRAY OF an FB type pays that same construction cost once per
        // element, so it defers too - as one cell over the whole array, not one
        // per element: ArrayValue.Elements is a plain object[] that every index,
        // iteration and byte-model site reads straight out of, and per-element
        // cells would have to be honoured at all of them.
        private bool IsDeferredFieldType(string typeName)
        {
            var resolved = _registry.ResolveAlias(typeName);
            if (ArrayTypeInfo.IsArrayType(resolved))
                return ArrayTypeInfo.TryGetElementTypeName(resolved, out var elementTypeName) &&
                    IsDeferredFieldType(elementTypeName);

            return _registry.Get(resolved) != null;
        }

        // The sections materialized as instance Fields at NewInstance() time,
        // i.e. the set visible to dot-access. VAR/Input/Output/InOut persist
        // across calls and StepCycles.
        //
        // Top-level VAR_TEMP is included because methods can only reach it
        // through instance.Fields, but unlike the others it must NOT persist:
        // ResetTopLevelTempFields returns it to its default before every
        // top-level body invocation, so it behaves like a fresh local.
        private static bool IsPersistedField(VarDecl decl) =>
            decl.Section == VarSection.Local ||
            decl.Section == VarSection.Input ||
            decl.Section == VarSection.Output ||
            decl.Section == VarSection.InOut ||
            decl.Section == VarSection.Temp;

        // Resets every VAR_TEMP declared at the top level of instance's own type
        // ancestry (a METHOD's VAR_TEMP already resets per call, via
        // BindParams/Frame.Locals). Must run immediately before each fresh
        // invocation of a POU's own top-level body - RunSuite's single run, each
        // StepCycles cycle, each bare/InvokeFbInstance call - so top-level
        // VAR_TEMP never carries a value over from a previous invocation, per
        // IEC 61131-3 VAR_TEMP semantics.
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

        // Whether a FieldAccessExpr's receiver is a bare GVL name (e.g.
        // gScratchGlobals.stWidget) rather than a variable/field in scope.
        // Tested only when the identifier doesn't already resolve as a
        // local/instance field, so a same-named local or field always wins.
        private bool TryGetGvlFields(FieldAccessExpr fieldAccess, Frame frame, out Dictionary<string, Cell> fields)
        {
            if (fieldAccess.Receiver is IdentifierExpr gvlId &&
                frame.ResolveCell(gvlId.Name) == null &&
                _globals.TryGetValue(gvlId.Name, out fields))
                return true;

            fields = null;
            return false;
        }

        // A bare identifier that isn't a local or instance field may still be a
        // GVL member referenced without its GvlName. prefix - legal IEC 61131-3,
        // since global scope is visible everywhere, and not covered by
        // TryGetGvlFields, which only handles the qualified GvlName.Member
        // shape. First match across the registered GVLs wins; cross-GVL name
        // collisions are not detected.
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
