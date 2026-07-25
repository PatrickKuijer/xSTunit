using System;
using System.Collections.Generic;
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

        private static readonly HashSet<string> NativeTimerTypes = new HashSet<string> { "TON", "TOF", "FB_Pulse" };
        private static readonly HashSet<string> NativeEdgeTriggerTypes = new HashSet<string> { "R_TRIG", "F_TRIG" };

        public Engine(TypeRegistry registry)
        {
            _registry = registry;

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
                    fields[decl.Name] = new Cell();
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

        public IReadOnlyList<TestCaseResult> RunSuite(string suiteTypeName)
        {
            var instance = NewInstance(suiteTypeName);
            var def = _registry.Get(suiteTypeName);
            var frame = new Frame(instance, suiteTypeName);
            try
            {
                ExecuteStatements(Parser.ParseStatements(def.ImplementationText), frame);
            }
            catch (MethodReturnSignal)
            {
            }
            return instance.NativeSuiteHost.Collect();
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
                else if (current == "Loopback")
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
                foreach (var decl in VarBlockParser.Parse(def.DeclarationText).Where(IsPersistedField))
                    instance.Fields[decl.Name] = new Cell { Value = DefaultValue(decl, instance) };
            }

            CallMethod(instance, "FB_init", Array.Empty<Expr>(), Array.Empty<NamedArg>(), null, null, optionalIfMissing: true);

            return instance;
        }

        // FbInstance.StepCycles(n) - re-invokes the instance's top-level body n
        // times, reusing the instance's existing Cell state across calls (same
        // persistence CallMethod relies on). No dt/scheduler: caller controls
        // ordering across multiple instances by choosing call order.
        private void StepCycles(FbInstance instance, int cycles)
        {
            var def = _registry.Get(instance.ActualTypeName);
            if (def == null)
                throw new InvalidOperationException($"Type '{instance.ActualTypeName}' not found for StepCycles");

            var statements = Parser.ParseStatements(def.ImplementationText);
            for (var i = 0; i < cycles; i++)
            {
                var frame = new Frame(instance, instance.ActualTypeName);
                try
                {
                    ExecuteStatements(statements, frame);
                }
                catch (MethodReturnSignal)
                {
                    // A top-level RETURN inside the FB's cyclic body only ends
                    // this cycle; it must not unwind into whatever ST call
                    // (e.g. a TcUnit test method) invoked StepCycles.
                }
            }
        }

        // AssertConverges/AssertConvergesAndLatches (TcXunit-w5x.15.9, T6
        // design): the helper owns the master-then-proxy stepping loop so
        // test authors don't hand-roll polling. Both throw (rather than
        // record a TcUnit-style failure) with a per-field diff, matching
        // T6's "actionable diagnosis" resolution.
        private void AssertConverges(FbInstance master, FbInstance proxy, string[] fieldNames, int maxCycles)
        {
            for (var i = 1; i <= maxCycles; i++)
            {
                StepCycles(master, 1);
                StepCycles(proxy, 1);
                if (FieldsConverged(master, proxy, fieldNames))
                    return;
            }

            throw new ConvergenceAssertionException(
                $"AssertConverges: fields did not converge within {maxCycles} cycles: {FieldDiff(master, proxy, fieldNames)}");
        }

        // "Flips exactly once and stays latched": once fieldNames converge,
        // they must stay converged for every remaining cycle; diverging
        // again after latching is a failure, same as never latching at all.
        private void AssertConvergesAndLatches(FbInstance master, FbInstance proxy, string[] fieldNames, int maxCycles)
        {
            var latchedAtCycle = -1;

            for (var i = 1; i <= maxCycles; i++)
            {
                StepCycles(master, 1);
                StepCycles(proxy, 1);
                var converged = FieldsConverged(master, proxy, fieldNames);

                if (latchedAtCycle >= 0 && !converged)
                    throw new ConvergenceAssertionException(
                        $"AssertConvergesAndLatches: fields converged at cycle {latchedAtCycle} but diverged again at cycle {i}: {FieldDiff(master, proxy, fieldNames)}");

                if (converged && latchedAtCycle < 0)
                    latchedAtCycle = i;
            }

            if (latchedAtCycle < 0)
                throw new ConvergenceAssertionException(
                    $"AssertConvergesAndLatches: fields never converged within {maxCycles} cycles: {FieldDiff(master, proxy, fieldNames)}");
        }

        private static bool FieldsConverged(FbInstance master, FbInstance proxy, string[] fieldNames) =>
            fieldNames.All(name => Equals(FieldValue(master, name), FieldValue(proxy, name)));

        private static string FieldDiff(FbInstance master, FbInstance proxy, string[] fieldNames) =>
            string.Join("; ", fieldNames
                .Where(name => !Equals(FieldValue(master, name), FieldValue(proxy, name)))
                .Select(name => $"{name}: master={FieldValue(master, name)}, proxy={FieldValue(proxy, name)}"));

        private static object FieldValue(FbInstance instance, string fieldName)
        {
            if (!instance.Fields.TryGetValue(fieldName, out var cell))
                throw new InvalidOperationException($"Field '{fieldName}' not found on type '{instance.ActualTypeName}'");
            return cell.Value;
        }

        private static string[] ToStringArray(object value)
        {
            if (value is ArrayValue array)
                return array.Elements.Select(e => (string)e).ToArray();

            throw new NotSupportedException($"Expected an array of field names, got {value?.GetType().Name ?? "null"}");
        }

        // Fields materialized at NewInstance() time (VAR_INPUT/VAR_OUTPUT/
        // VAR_IN_OUT alongside VAR/Local) - the set that must persist across
        // calls/StepCycles and be visible to dot-access (TcXunit-0v1).
        private static bool IsPersistedField(VarDecl decl) =>
            decl.Section == VarSection.Local ||
            decl.Section == VarSection.Input ||
            decl.Section == VarSection.Output ||
            decl.Section == VarSection.InOut;

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
        // gFrameworkTemp.stMachine) rather than a variable/field in scope
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
