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

        // source/sink aren't AST method params (Transmit is native, no
        // VarBlockParser decls to bind against) - resolved by fixed name
        // first (fbLink.Transmit(source:=..., sink:=...)), falling back to
        // IEC positional order same as BindParams.
        private Cell ResolveNamedOrPositionalCell(
            string paramName,
            int posIndex,
            IReadOnlyList<Expr> positionalArgs,
            IReadOnlyList<NamedArg> namedArgs,
            Frame callerFrame)
        {
            var match = namedArgs.FirstOrDefault(a => a.Name == paramName);
            if (match != null)
                return ResolveCellForLValue(match.Value, callerFrame);
            if (posIndex < positionalArgs.Count)
                return ResolveCellForLValue(positionalArgs[posIndex], callerFrame);

            throw new InvalidOperationException($"Transmit missing required argument '{paramName}'");
        }

        private static readonly HashSet<string> LoopbackFaultMethods = new HashSet<string>
        {
            "Transmit", "Drop", "Restore", "Freeze", "SetDelay", "Duplicate", "Corrupt",
        };

        private static bool IsLoopbackFaultMethod(string methodName) => LoopbackFaultMethods.Contains(methodName);

        private static Expr ResolveNamedOrPositionalArg(
            string methodName,
            string paramName,
            int posIndex,
            IReadOnlyList<Expr> positionalArgs,
            IReadOnlyList<NamedArg> namedArgs)
        {
            var match = namedArgs.FirstOrDefault(a => a.Name == paramName);
            if (match != null)
                return match.Value;
            if (posIndex < positionalArgs.Count)
                return positionalArgs[posIndex];

            throw new InvalidOperationException($"{methodName} missing required argument '{paramName}'");
        }

        private Cell ResolveCellForLValue(Expr expr, Frame frame)
        {
            if (expr is IdentifierExpr id)
            {
                var cell = frame.ResolveCell(id.Name);
                if (cell == null && !TryResolveGlobalCell(id.Name, out cell))
                    throw new InvalidOperationException($"Unknown variable '{id.Name}'");
                return cell;
            }

            if (expr is FieldAccessExpr fieldAccess)
            {
                if (TryGetGvlFields(fieldAccess, frame, out var gvlFields))
                {
                    if (!gvlFields.TryGetValue(fieldAccess.FieldName, out var gvlCell))
                        throw new InvalidOperationException($"Unknown field '{fieldAccess.FieldName}'");
                    return gvlCell;
                }

                var fields = FieldsOf(Evaluate(fieldAccess.Receiver, frame));
                if (!fields.TryGetValue(fieldAccess.FieldName, out var cell))
                    throw new InvalidOperationException($"Unknown field '{fieldAccess.FieldName}'");
                return cell;
            }

            if (expr is IndexExpr index)
            {
                var array = (ArrayValue)Evaluate(index.Receiver, frame);
                return new ArrayElementCell(array, FlattenIndex(array, index.Indices, frame));
            }

            throw new NotSupportedException("Only plain identifiers, field access, and array indexing are supported as REF=/ADR()/Transmit() targets in v1");
        }

        // ADR() target resolution: same as ResolveCellForLValue, except a bare
        // array (identifier or field, no index) decays to the address of its
        // first element - ADR(arr) means "address of arr[lowbound]" in IEC
        // 61131-3, and only an element-targeting Cell can be pointer-arithmetic'd
        // (see EvaluateBinary's Pointer +/- handling, TcXunit-sej.2).
        private Cell ResolveCellForAdr(Expr expr, Frame frame)
        {
            var cell = ResolveCellForLValue(expr, frame);
            if (!(cell is ArrayElementCell) && cell.Value is ArrayValue array)
                return new ArrayElementCell(array, 0);
            return cell;
        }

        // __ISVALIDREF(ref): TwinCAT intrinsic returning TRUE when a REFERENCE
        // TO variable currently aliases a valid target, FALSE when unassigned.
        // An unbound REFERENCE TO defaults to a null-valued Cell (DefaultValue
        // returns null for REFERENCE TO/POINTER TO), while a REF=-bound
        // reference aliases the target's own Cell (whose value is the FB/struct
        // /scalar it points at) - so a non-null resolved value maps to "valid".
        private bool IsValidRef(Expr expr, Frame frame)
        {
            return ResolveCellForLValue(expr, frame).Value != null;
        }

        // FbInstance and StructInstance are both "named-field container of
        // Cells" (T7's struct Cell-shape decision) - FieldAccessExpr reads
        // either the same way.
        private static Dictionary<string, Cell> FieldsOf(object receiver) => receiver switch
        {
            FbInstance fb => fb.Fields,
            StructInstance st => st.Fields,
            _ => throw new NotSupportedException($"Cannot access fields on {receiver?.GetType().Name}"),
        };

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

        public object Evaluate(Expr expr, Frame frame)
        {
            switch (expr)
            {
                case IntLiteralExpr i:
                    return i.Value;
                case RealLiteralExpr r:
                    return r.Value;
                case LrealLiteralExpr lr:
                    return lr.Value;
                case TimeLiteralExpr t:
                    return t.Value;
                case LtimeLiteralExpr lt:
                    return lt.Value;
                case StringLiteralExpr s:
                    return s.Value;
                case BoolLiteralExpr b:
                    return b.Value;
                case IdentifierExpr id:
                {
                    var cell = frame.ResolveCell(id.Name);
                    if (cell == null && !TryResolveGlobalCell(id.Name, out cell))
                        throw new InvalidOperationException($"Unknown variable '{id.Name}'");
                    return cell.Value;
                }
                case ThisRefExpr:
                    return frame.Instance;
                case SuperRefExpr:
                    return frame.Instance;
                case DerefExpr deref:
                    return ((Pointer)Evaluate(deref.Inner, frame)).Target.Value;
                case IndexExpr index:
                {
                    var array = (ArrayValue)Evaluate(index.Receiver, frame);
                    return array.Elements[FlattenIndex(array, index.Indices, frame)];
                }
                case FieldAccessExpr fieldAccess:
                {
                    // Type.Member where Type names a built-in enum rather
                    // than a variable (e.g. TcEventSeverity.Warning) -
                    // resolve the member's underlying value directly,
                    // bypassing the variable/field lookup below (which
                    // would otherwise throw on the receiver identifier).
                    if (fieldAccess.Receiver is IdentifierExpr enumTypeId &&
                        frame.ResolveCell(enumTypeId.Name) == null &&
                        BuiltinEnums.Types.TryGetValue(enumTypeId.Name, out var enumMembers))
                    {
                        if (!enumMembers.TryGetValue(fieldAccess.FieldName, out var enumValue))
                            throw new InvalidOperationException($"Unknown enum member '{enumTypeId.Name}.{fieldAccess.FieldName}'");
                        return enumValue;
                    }

                    // GvlName.field where GvlName isn't a variable/field in
                    // scope but a registered GVL (TcXunit-71o) - same
                    // "receiver identifier doesn't resolve as a variable"
                    // shape as the enum check above.
                    if (TryGetGvlFields(fieldAccess, frame, out var gvlFields))
                    {
                        if (!gvlFields.TryGetValue(fieldAccess.FieldName, out var gvlCell))
                            throw new InvalidOperationException($"Unknown field '{fieldAccess.FieldName}'");
                        return gvlCell.Value;
                    }

                    var fields = FieldsOf(Evaluate(fieldAccess.Receiver, frame));
                    if (!fields.TryGetValue(fieldAccess.FieldName, out var cell))
                        throw new InvalidOperationException($"Unknown field '{fieldAccess.FieldName}'");
                    return cell.Value;
                }
                case StructLiteralExpr structLit:
                {
                    // No declared struct type is known in a bare expression
                    // context, so this builds an untyped instance straight
                    // from the given fields (no default-merge) - the typed,
                    // default-merging path is BuildStructDefault, used when
                    // a VarDecl's declared type is a known StructAst.
                    var instance = new StructInstance(null);
                    foreach (var init in structLit.FieldInits)
                        instance.Fields[init.Name] = new Cell { Value = Evaluate(init.Value, frame) };
                    return instance;
                }
                case ArrayLiteralExpr arrayLit:
                {
                    // No declared bounds are known in a bare expression
                    // context, so this defaults to a single 0-based
                    // dimension sized to the literal - BuildArrayDefault is
                    // the typed path that overlays onto declared bounds.
                    var elements = arrayLit.Elements.Select(e => Evaluate(e, frame)).ToArray();
                    return new ArrayValue(new List<(int, int)> { (0, elements.Length - 1) }, null, elements);
                }
                case BinaryExpr binary:
                    return EvaluateBinary(binary, frame);
                case UnaryExpr unary:
                    return EvaluateUnary(unary, frame);
                case CallExpr call:
                    return EvaluateCall(call, frame);
                default:
                    throw new NotSupportedException($"Expression type {expr.GetType().Name} not supported");
            }
        }

        private object EvaluateUnary(UnaryExpr unary, Frame frame)
        {
            var value = Evaluate(unary.Operand, frame);

            if (unary.Op == "NOT")
            {
                if (value is bool b)
                    return !b;
                if (value is int i)
                    return ~i;
                throw new NotSupportedException($"Operator 'NOT' requires a BOOL or INT operand, got {value?.GetType().Name}");
            }

            if (unary.Op == "-")
            {
                switch (value)
                {
                    case int i: return -i;
                    case float f: return -f;
                    case double d: return -d;
                    default:
                        throw new NotSupportedException($"Unary '-' requires a numeric operand, got {value?.GetType().Name}");
                }
            }

            throw new NotSupportedException($"Unary operator '{unary.Op}' not supported");
        }

        private object EvaluateBinary(BinaryExpr binary, Frame frame)
        {
            var leftVal = Evaluate(binary.Left, frame);
            var rightVal = Evaluate(binary.Right, frame);

            if ((binary.Op == "+" || binary.Op == "-") && (leftVal is Pointer || rightVal is Pointer))
                return EvaluatePointerArithmetic(binary.Op, leftVal, rightVal);

            if ((binary.Op == "=" || binary.Op == "<>") &&
                (leftVal is Pointer || rightVal is Pointer || leftVal == null || rightVal == null))
                return EvaluatePointerEquality(binary.Op, leftVal, rightVal);

            if (binary.Op == "AND" || binary.Op == "OR" || binary.Op == "XOR")
                return EvaluateBitstring(binary.Op, leftVal, rightVal);

            if (binary.Op == "MOD")
                return EvaluateMod(leftVal, rightVal);

            // BOOL only supports equality/inequality in IEC 61131-3 (no
            // ordering, no arithmetic) - handle it here so it doesn't fall
            // through to the int cast below.
            if (leftVal is bool lbEq && rightVal is bool rbEq && (binary.Op == "=" || binary.Op == "<>"))
                return binary.Op == "=" ? lbEq == rbEq : lbEq != rbEq;

            // INT->REAL->LREAL implicit widening: promote to the widest operand's
            // type for the whole operation, per TwinCAT's "smaller to larger is
            // implicit" arithmetic promotion rule.
            if (leftVal is double || rightVal is double)
                return EvaluateNumeric(binary.Op, ToDouble(leftVal), ToDouble(rightVal));

            if (leftVal is float || rightVal is float)
                return EvaluateNumeric(binary.Op, ToFloat(leftVal), ToFloat(rightVal));

            return EvaluateNumeric(binary.Op, (int)leftVal, (int)rightVal);
        }

        // ADR(x) +/- offset: offset moves in whole array elements, not raw
        // bytes - correct as literal byte arithmetic when the pointee is a
        // BYTE/SINT/USINT array (the buffer-packing case MEMCPY/MEMSET/MEMMOVE
        // exist for), an approximation for wider element types. Only pointers
        // whose target is an array element (ArrayElementCell, including the
        // ADR(arr)-decays-to-element-0 case) support arithmetic - a pointer to
        // a scalar or whole STRUCT has no element to step through, and this
        // interpreter has no byte-level STRUCT layout model (flagged gap,
        // TcXunit-sej.2).
        private static object EvaluatePointerArithmetic(string op, object leftVal, object rightVal)
        {
            if (op == "-" && leftVal is Pointer && rightVal is Pointer)
                throw new NotSupportedException("Pointer-minus-pointer is not supported");

            var (ptr, offsetVal) = leftVal is Pointer p ? (p, rightVal) : ((Pointer)rightVal, leftVal);
            var delta = (int)offsetVal;
            if (op == "-")
                delta = -delta;

            if (!(ptr.Target is ArrayElementCell aec))
                throw new NotSupportedException(
                    "Pointer arithmetic (ADR(x) +/- offset) is only supported when the pointer targets an " +
                    "array element (e.g. ADR(byteBuf) or ADR(byteBuf[i])); byte-offset into a scalar or " +
                    "the interior of a STRUCT is not modeled.");

            var newIndex = aec.Index + delta;
            if (newIndex < 0 || newIndex >= aec.Array.Elements.Length)
                throw new IndexOutOfRangeException(
                    $"Pointer arithmetic moved index to {newIndex}, out of bounds [0..{aec.Array.Elements.Length - 1}]");

            return new Pointer(new ArrayElementCell(aec.Array, newIndex));
        }

        // Pointer '='/'<>' comparison (TcXunit-dur): the standard IEC 61131-3
        // null-pointer-check idiom is 'IF ipSrc = 0 THEN'. An unbound/default
        // POINTER TO x Cell holds C# null (see DefaultValue), never int 0, so
        // the null side of the comparison is a null reference, not a numeric
        // zero - but the literal on the other side is still the int 0. Two
        // real (ADR-bound) pointers are compared by target-Cell identity;
        // a bound pointer is never "null"/zero, so it compares unequal to
        // both null and any int literal.
        private static object EvaluatePointerEquality(string op, object leftVal, object rightVal)
        {
            bool equal;
            if (leftVal is Pointer leftPtr && rightVal is Pointer rightPtr)
                equal = PointerTargetsEqual(leftPtr.Target, rightPtr.Target);
            else if (leftVal is Pointer || rightVal is Pointer)
                equal = false;
            else if (leftVal == null && rightVal == null)
                equal = true;
            else if (leftVal == null)
                equal = rightVal is int rightInt && rightInt == 0;
            else
                equal = leftVal is int leftInt && leftInt == 0;

            return op == "=" ? equal : !equal;
        }

        // ADR(x) builds a fresh ArrayElementCell wrapper on every call
        // (TcXunit-sej.2), so two pointers to the "same" element are two
        // distinct ArrayElementCell instances - compare the underlying
        // ArrayValue + Index instead of Cell reference identity for that
        // case; fall back to reference equality for a plain scalar/struct
        // field Cell (ADR(x) on those returns the actual field Cell).
        private static bool PointerTargetsEqual(Cell left, Cell right)
        {
            if (left is ArrayElementCell leftAec && right is ArrayElementCell rightAec)
                return ReferenceEquals(leftAec.Array, rightAec.Array) && leftAec.Index == rightAec.Index;

            return ReferenceEquals(left, right);
        }

        // MEMCPY/MEMSET/MEMMOVE (TcXunit-sej.3): dest/src must be pointers to
        // an array element (see ResolveCellForAdr/ArrayElementCell) - this is
        // the POINTER TO BYTE over ARRAY OF BYTE buffer-packing case these
        // intrinsics exist for. n counts elements (== bytes for a BYTE/SINT/
        // USINT-element array); out-of-range access throws naturally via the
        // backing Elements[] indexer.
        //
        // TcXunit-996: these are native intrinsics (no VarBlockParser decls
        // to bind against, unlike FB/method calls), so named args aren't
        // reconciled by BindParams - resolve each declared param (destAddr/
        // srcAddr/value/n) by name first (e.g. MEMCPY(destAddr := ipDst,
        // srcAddr := ipSrc, inSrcSize)), consuming PositionalArgs in
        // left-to-right order only for params *not* given by name (mirrors
        // BindParams' shared posIndex - a positional arg's PositionalArgs
        // slot depends on how many preceding params were named, not on the
        // param's declared signature position).
        private static IReadOnlyDictionary<string, Expr> ResolveIntrinsicArgs(
            IReadOnlyList<string> paramNamesInDeclOrder,
            IReadOnlyList<Expr> positionalArgs,
            IReadOnlyList<NamedArg> namedArgs)
        {
            var resolved = new Dictionary<string, Expr>();
            var posIndex = 0;
            foreach (var paramName in paramNamesInDeclOrder)
            {
                var match = namedArgs.FirstOrDefault(a => a.Name == paramName);
                if (match != null)
                    resolved[paramName] = match.Value;
                else if (posIndex < positionalArgs.Count)
                    resolved[paramName] = positionalArgs[posIndex++];
            }
            return resolved;
        }

        private static Expr RequireIntrinsicArg(string methodName, string paramName, IReadOnlyDictionary<string, Expr> args)
        {
            if (args.TryGetValue(paramName, out var value))
                return value;

            throw new InvalidOperationException($"{methodName} missing required argument '{paramName}'");
        }

        private Pointer RequirePointerArg(string methodName, string paramName, IReadOnlyDictionary<string, Expr> args, Frame frame)
        {
            var value = Evaluate(RequireIntrinsicArg(methodName, paramName, args), frame);
            if (!(value is Pointer ptr))
                throw new InvalidOperationException(
                    $"{methodName} argument '{paramName}' must be a POINTER TO BYTE (e.g. ADR(buf) or ADR(buf[i])), got {value?.GetType().Name}");
            return ptr;
        }

        private static (ArrayValue Array, int Index) RequireArrayElement(Pointer ptr, string methodName, string paramName)
        {
            if (!(ptr.Target is ArrayElementCell aec))
                throw new NotSupportedException(
                    $"{methodName} '{paramName}' pointer must target an array element (e.g. ADR(buf) or ADR(buf[i])) - " +
                    "byte-offset into a scalar or STRUCT interior isn't modeled.");
            return (aec.Array, aec.Index);
        }

        // MEMCPY (overlapSafe: false) copies forward regardless of overlap,
        // same as the C intrinsic it mirrors. MEMMOVE (overlapSafe: true)
        // detects a forward overlap (dest inside [src, src+count) on the same
        // backing array) and copies backward instead, so a "shift buffer
        // down after consuming its head" pattern doesn't clobber source
        // elements before they're read.
        private static Pointer MemCopy(Pointer dest, Pointer src, int count, bool overlapSafe)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "MEMCPY/MEMMOVE count must be >= 0");

            var methodName = overlapSafe ? "MEMMOVE" : "MEMCPY";
            var (destArray, destIndex) = RequireArrayElement(dest, methodName, "destAddr");
            var (srcArray, srcIndex) = RequireArrayElement(src, methodName, "srcAddr");

            var backward = overlapSafe
                && ReferenceEquals(destArray, srcArray)
                && destIndex > srcIndex
                && destIndex < srcIndex + count;

            if (backward)
            {
                for (var i = count - 1; i >= 0; i--)
                    destArray.Elements[destIndex + i] = srcArray.Elements[srcIndex + i];
            }
            else
            {
                for (var i = 0; i < count; i++)
                    destArray.Elements[destIndex + i] = srcArray.Elements[srcIndex + i];
            }

            return dest;
        }

        private static Pointer MemSet(Pointer dest, object value, int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "MEMSET count must be >= 0");

            var (destArray, destIndex) = RequireArrayElement(dest, "MEMSET", "destAddr");
            var lowByte = Convert.ToInt32(value) & 0xFF;

            for (var i = 0; i < count; i++)
                destArray.Elements[destIndex + i] = lowByte;

            return dest;
        }

        private static double ToDouble(object value) => value switch
        {
            double d => d,
            float f => f,
            int i => i,
            _ => throw new NotSupportedException($"Cannot use {value?.GetType().Name} in numeric arithmetic"),
        };

        private static float ToFloat(object value) => value switch
        {
            float f => f,
            int i => i,
            _ => throw new NotSupportedException($"Cannot use {value?.GetType().Name} in numeric arithmetic"),
        };

        private static object EvaluateNumeric(string op, double left, double right) => op switch
        {
            "+" => left + right,
            "-" => left - right,
            "*" => left * right,
            "/" => left / right,
            "<" => left < right,
            ">" => left > right,
            "<=" => left <= right,
            ">=" => left >= right,
            "=" => left == right,
            "<>" => left != right,
            _ => throw new NotSupportedException($"Operator '{op}' not supported"),
        };

        private static object EvaluateNumeric(string op, float left, float right) => op switch
        {
            "+" => left + right,
            "-" => left - right,
            "*" => left * right,
            "/" => left / right,
            "<" => left < right,
            ">" => left > right,
            "<=" => left <= right,
            ">=" => left >= right,
            "=" => left == right,
            "<>" => left != right,
            _ => throw new NotSupportedException($"Operator '{op}' not supported"),
        };

        private static object EvaluateNumeric(string op, int left, int right) => op switch
        {
            "+" => left + right,
            "-" => left - right,
            "*" => left * right,
            "/" => left / right,
            "<" => left < right,
            ">" => left > right,
            "<=" => left <= right,
            ">=" => left >= right,
            "=" => left == right,
            "<>" => left != right,
            _ => throw new NotSupportedException($"Operator '{op}' not supported"),
        };

        // MOD/AND/OR/XOR are IEC 61131-3 bitstring/logical operators, not numeric
        // arithmetic - AND/OR/XOR operate on matching BOOL or INT operands; MOD is
        // integer-only (no REAL/LREAL remainder in the fixture's scope).
        private static object EvaluateBitstring(string op, object left, object right)
        {
            if (left is bool lb && right is bool rb)
                return op switch
                {
                    "AND" => lb && rb,
                    "OR" => lb || rb,
                    "XOR" => lb ^ rb,
                    _ => throw new NotSupportedException($"Operator '{op}' not supported"),
                };

            if (left is int li && right is int ri)
                return op switch
                {
                    "AND" => li & ri,
                    "OR" => li | ri,
                    "XOR" => li ^ ri,
                    _ => throw new NotSupportedException($"Operator '{op}' not supported"),
                };

            throw new NotSupportedException($"Operator '{op}' requires matching BOOL or INT operands, got {left?.GetType().Name} and {right?.GetType().Name}");
        }

        private static object EvaluateMod(object left, object right)
        {
            if (left is int li && right is int ri)
                return li % ri;

            throw new NotSupportedException($"Operator 'MOD' is integer-only, got {left?.GetType().Name} and {right?.GetType().Name}");
        }

        private object EvaluateCall(CallExpr call, Frame frame)
        {
            if (call.Receiver == null)
            {
                if (call.MethodName == "ADR")
                    return new Pointer(ResolveCellForAdr(call.PositionalArgs[0], frame));

                if (call.MethodName == "__ISVALIDREF")
                    return IsValidRef(call.PositionalArgs[0], frame);

                if (call.MethodName == "MEMCPY" || call.MethodName == "MEMMOVE")
                {
                    var args = ResolveIntrinsicArgs(new[] { "destAddr", "srcAddr", "n" }, call.PositionalArgs, call.NamedArgs);
                    return MemCopy(
                        RequirePointerArg(call.MethodName, "destAddr", args, frame),
                        RequirePointerArg(call.MethodName, "srcAddr", args, frame),
                        (int)Evaluate(RequireIntrinsicArg(call.MethodName, "n", args), frame),
                        overlapSafe: call.MethodName == "MEMMOVE");
                }

                if (call.MethodName == "MEMSET")
                {
                    var args = ResolveIntrinsicArgs(new[] { "destAddr", "value", "n" }, call.PositionalArgs, call.NamedArgs);
                    return MemSet(
                        RequirePointerArg(call.MethodName, "destAddr", args, frame),
                        Evaluate(RequireIntrinsicArg(call.MethodName, "value", args), frame),
                        (int)Evaluate(RequireIntrinsicArg(call.MethodName, "n", args), frame));
                }

                if (TryEvaluateCast(call, frame, out var castResult))
                    return castResult;

                return CallMethod(frame.Instance, call.MethodName, call.PositionalArgs, call.NamedArgs, frame, null);
            }

            if (call.Receiver is ThisRefExpr)
                return CallMethod(frame.Instance, call.MethodName, call.PositionalArgs, call.NamedArgs, frame, null);

            if (call.Receiver is SuperRefExpr)
            {
                var baseType = _registry.Get(frame.DeclaringTypeName)?.BaseTypeName;
                return CallMethod(frame.Instance, call.MethodName, call.PositionalArgs, call.NamedArgs, frame, baseType);
            }

            var receiverInstance = (FbInstance)Evaluate(call.Receiver, frame);
            return CallMethod(receiverInstance, call.MethodName, call.PositionalArgs, call.NamedArgs, frame, null);
        }

        private static readonly HashSet<string> IntegerCastTargets = new HashSet<string>
        {
            "SINT", "USINT", "INT", "UINT", "DINT", "UDINT", "LINT", "ULINT", "BYTE", "WORD", "DWORD", "LWORD",
        };

        // Recognizes explicit <from>_TO_<to> conversion calls (e.g. LREAL_TO_INT)
        // per TwinCAT's narrowing-cast naming convention. Not a real method, so
        // it's intercepted here before falling through to CallMethod/native-bridge
        // dispatch.
        private bool TryEvaluateCast(CallExpr call, Frame frame, out object result)
        {
            result = null;

            var separator = call.MethodName.IndexOf("_TO_", StringComparison.Ordinal);
            if (separator < 0 || call.PositionalArgs.Count != 1)
                return false;

            var toType = call.MethodName.Substring(separator + 4);
            var value = Evaluate(call.PositionalArgs[0], frame);

            if (toType == "REAL")
                result = Convert.ToSingle(value);
            else if (toType == "LREAL")
                result = Convert.ToDouble(value);
            else if (IntegerCastTargets.Contains(toType))
                result = Convert.ToInt32(value);
            else
                return false;

            return true;
        }
    }
}
