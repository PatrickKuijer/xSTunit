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
    public sealed class Engine
    {
        private readonly TypeRegistry _registry;

        // Shared process-wide simulated clock (TcXunit-w5x.15.7 / T3 design) -
        // one Clock for the whole Engine, not per-instance; TON/TOF/FB_Pulse
        // native hosts read Clock.TotalMs whenever they're invoked.
        public Clock Clock { get; } = new Clock();

        private static readonly HashSet<string> NativeTimerTypes = new HashSet<string> { "TON", "TOF", "FB_Pulse" };

        public Engine(TypeRegistry registry)
        {
            _registry = registry;
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

        public object CallMethod(
            FbInstance instance,
            string methodName,
            IReadOnlyList<Expr> positionalArgs,
            IReadOnlyList<NamedArg> namedArgs,
            Frame callerFrame,
            string dispatchStartTypeOverride,
            bool optionalIfMissing = false)
        {
            var startType = dispatchStartTypeOverride ?? instance.ActualTypeName;

            string definingType = null;
            TcXunit.Parser.MethodAst methodDef = null;
            var type = startType;
            while (type != null)
            {
                var def = _registry.Get(type);
                if (def == null)
                    break;

                var found = def.Methods.FirstOrDefault(m => m.Name == methodName);
                if (found != null)
                {
                    definingType = type;
                    methodDef = found;
                    break;
                }

                type = def.BaseTypeName;
            }

            if (methodDef == null)
            {
                if (methodName == "StepCycles" && positionalArgs.Count == 1)
                {
                    var cycles = Convert.ToInt32(Evaluate(positionalArgs[0], callerFrame));
                    StepCycles(instance, cycles);
                    return null;
                }

                if ((methodName == "AssertConverges" || methodName == "AssertConvergesAndLatches") && positionalArgs.Count == 4)
                {
                    var master = (FbInstance)Evaluate(positionalArgs[0], callerFrame);
                    var proxy = (FbInstance)Evaluate(positionalArgs[1], callerFrame);
                    var fieldNames = ToStringArray(Evaluate(positionalArgs[2], callerFrame));
                    var maxCycles = Convert.ToInt32(Evaluate(positionalArgs[3], callerFrame));

                    if (methodName == "AssertConverges")
                        AssertConverges(master, proxy, fieldNames, maxCycles);
                    else
                        AssertConvergesAndLatches(master, proxy, fieldNames, maxCycles);
                    return null;
                }

                // Bare FB invocation, e.g. fbTon(IN:=x, PT:=t) - methodName is
                // parsed as a call on the current instance with no receiver,
                // but here it names a field holding another FbInstance to
                // invoke directly (TcXunit-w5x.15.7's TON/TOF/FB_Pulse hosts).
                if (instance.Fields.TryGetValue(methodName, out var calleeCell) &&
                    calleeCell.Value is FbInstance callee &&
                    callee.NativeTimerHost != null)
                {
                    BindTimerInputs(callee, positionalArgs, namedArgs, callerFrame);
                    callee.NativeTimerHost.Update(callee, Clock.TotalMs);
                    return null;
                }

                // Bare invocation of an ordinary interpreted (non-native) FB
                // field, e.g. sfbLoopback(ibEnable := TRUE) - generalizes the
                // native-timer bare-invoke above: bind VAR_INPUT/VAR_IN_OUT
                // args into the callee's persisted Fields, then run its
                // top-level body once (TcXunit-0v1).
                if (instance.Fields.TryGetValue(methodName, out var interpretedCalleeCell) &&
                    interpretedCalleeCell.Value is FbInstance interpretedCallee &&
                    interpretedCallee.NativeTimerHost == null &&
                    interpretedCallee.NativeLoopbackHost == null &&
                    interpretedCallee.NativeSuiteHost == null &&
                    _registry.Get(interpretedCallee.ActualTypeName) != null)
                {
                    InvokeFbInstance(interpretedCallee, positionalArgs, namedArgs, callerFrame);
                    return null;
                }

                if (instance.NativeLoopbackHost != null && IsLoopbackFaultMethod(methodName))
                {
                    switch (methodName)
                    {
                        case "Transmit":
                            var sourceCell = ResolveNamedOrPositionalCell("source", 0, positionalArgs, namedArgs, callerFrame);
                            var sinkCell = ResolveNamedOrPositionalCell("sink", 1, positionalArgs, namedArgs, callerFrame);
                            instance.NativeLoopbackHost.Transmit(instance, sourceCell, sinkCell, Clock.TotalMs);
                            break;
                        case "Drop":
                            instance.NativeLoopbackHost.Drop(instance);
                            break;
                        case "Restore":
                            instance.NativeLoopbackHost.Restore(instance);
                            break;
                        case "Freeze":
                            instance.NativeLoopbackHost.Freeze(instance);
                            break;
                        case "SetDelay":
                            var n = Convert.ToInt32(Evaluate(ResolveNamedOrPositionalArg("n", 0, positionalArgs, namedArgs), callerFrame));
                            instance.NativeLoopbackHost.SetDelay(instance, n);
                            break;
                        case "Duplicate":
                            instance.NativeLoopbackHost.Duplicate(instance);
                            break;
                        case "Corrupt":
                            var value = Evaluate(ResolveNamedOrPositionalArg("value", 0, positionalArgs, namedArgs), callerFrame);
                            instance.NativeLoopbackHost.Corrupt(instance, value);
                            break;
                    }
                    return null;
                }

                if (optionalIfMissing)
                    return null;

                if (instance.NativeSuiteHost != null)
                {
                    var evaluatedPositional = positionalArgs.Select(e => Evaluate(e, callerFrame)).ToList();
                    var evaluatedNamed = namedArgs.ToDictionary(a => a.Name, a => Evaluate(a.Value, callerFrame));
                    return NativeMethodBridge.Invoke(instance.NativeSuiteHost, methodName, evaluatedPositional, evaluatedNamed);
                }

                throw new InvalidOperationException($"Method '{methodName}' not found starting from type '{startType}'");
            }

            var newFrame = new Frame(instance, definingType);
            var paramDecls = VarBlockParser.Parse(methodDef.DeclarationText);
            BindParams(paramDecls, positionalArgs, namedArgs, callerFrame, newFrame);

            try
            {
                ExecuteStatements(Parser.ParseStatements(methodDef.ImplementationText), newFrame);
            }
            catch (MethodReturnSignal)
            {
            }

            return newFrame.Locals.TryGetValue(methodName, out var returnCell) ? returnCell.Value : null;
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
                ExecuteStatements(statements, frame);
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

        // IN/PT bound by position (IEC order) or by name; unset args keep the
        // timer instance's current field value (e.g. a caller that only ever
        // passes IN relies on PT staying whatever it was last set to).
        private static readonly string[] TimerPositionalParams = { "IN", "PT" };

        private void BindTimerInputs(
            FbInstance callee,
            IReadOnlyList<Expr> positionalArgs,
            IReadOnlyList<NamedArg> namedArgs,
            Frame callerFrame)
        {
            for (var i = 0; i < positionalArgs.Count && i < TimerPositionalParams.Length; i++)
                callee.Fields[TimerPositionalParams[i]].Value = Evaluate(positionalArgs[i], callerFrame);

            foreach (var arg in namedArgs)
                if (callee.Fields.TryGetValue(arg.Name, out var cell))
                    cell.Value = Evaluate(arg.Value, callerFrame);
        }

        // Fields materialized at NewInstance() time (VAR_INPUT/VAR_OUTPUT/
        // VAR_IN_OUT alongside VAR/Local) - the set that must persist across
        // calls/StepCycles and be visible to dot-access (TcXunit-0v1).
        private static bool IsPersistedField(VarDecl decl) =>
            decl.Section == VarSection.Local ||
            decl.Section == VarSection.Input ||
            decl.Section == VarSection.Output ||
            decl.Section == VarSection.InOut;

        // Declared VAR_INPUT/VAR_IN_OUT params for a bare-invoked interpreted
        // FB, in base-to-derived declaration order (matches IEC positional
        // arg order and the Fields-materialization loop in NewInstance).
        private List<VarDecl> GetOwnInputDecls(FbInstance instance)
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

            var result = new List<VarDecl>();
            for (var i = chain.Count - 1; i >= 0; i--)
            {
                var def = _registry.Get(chain[i]);
                result.AddRange(VarBlockParser.Parse(def.DeclarationText)
                    .Where(d => d.Section == VarSection.Input || d.Section == VarSection.InOut));
            }
            return result;
        }

        // Generalizes BindTimerInputs beyond the native TON/TOF/FB_Pulse
        // boundary: binds bare-invocation args into the callee's persisted
        // Fields by name or IEC positional order, then runs the callee's own
        // top-level body once (TcXunit-0v1).
        private void InvokeFbInstance(
            FbInstance callee,
            IReadOnlyList<Expr> positionalArgs,
            IReadOnlyList<NamedArg> namedArgs,
            Frame callerFrame)
        {
            var inputDecls = GetOwnInputDecls(callee);

            for (var i = 0; i < positionalArgs.Count && i < inputDecls.Count; i++)
                callee.Fields[inputDecls[i].Name].Value = Evaluate(positionalArgs[i], callerFrame);

            foreach (var arg in namedArgs)
                if (callee.Fields.TryGetValue(arg.Name, out var cell))
                    cell.Value = Evaluate(arg.Value, callerFrame);

            var def = _registry.Get(callee.ActualTypeName);
            var calleeFrame = new Frame(callee, callee.ActualTypeName);
            try
            {
                ExecuteStatements(Parser.ParseStatements(def.ImplementationText), calleeFrame);
            }
            catch (MethodReturnSignal)
            {
            }
        }

        private void BindParams(
            IReadOnlyList<VarDecl> paramDecls,
            IReadOnlyList<Expr> positionalArgs,
            IReadOnlyList<NamedArg> namedArgs,
            Frame callerFrame,
            Frame newFrame)
        {
            var posIndex = 0;
            foreach (var decl in paramDecls)
            {
                object value;

                if (decl.Section == VarSection.Input || decl.Section == VarSection.InOut)
                {
                    var match = namedArgs.FirstOrDefault(a => a.Name == decl.Name);
                    if (match != null)
                        value = Evaluate(match.Value, callerFrame);
                    else if (posIndex < positionalArgs.Count)
                        value = Evaluate(positionalArgs[posIndex++], callerFrame);
                    else
                        value = DefaultValue(decl, newFrame.Instance);
                }
                else
                {
                    value = DefaultValue(decl, newFrame.Instance);
                }

                newFrame.Locals[decl.Name] = new Cell { Value = value };
            }
        }

        private object DefaultValue(VarDecl decl, FbInstance owningInstance)
        {
            if (ArrayTypeInfo.IsArrayType(decl.TypeName))
                return BuildArrayDefault(decl, owningInstance);

            var structAst = _registry.GetStruct(decl.TypeName);
            if (structAst != null)
                return BuildStructDefault(structAst, decl.DefaultValueText, owningInstance);

            if (_registry.Get(decl.TypeName) != null || NativeTimerTypes.Contains(decl.TypeName) || decl.TypeName == "Loopback")
                return NewInstance(decl.TypeName);

            if (decl.DefaultValueText != null)
                return Evaluate(Parser.ParseExpression(decl.DefaultValueText), new Frame(owningInstance, decl.TypeName));

            if (StringTypeInfo.IsStringType(decl.TypeName))
                return "";

            if (decl.TypeName == "BOOL")
                return false;

            if (decl.TypeName == "REAL")
                return 0f;

            if (decl.TypeName == "LREAL")
                return 0d;

            if (decl.TypeName == "TIME")
                return 0u;

            if (decl.TypeName == "LTIME")
                return 0ul;

            if (decl.TypeName.StartsWith("POINTER TO") || decl.TypeName.StartsWith("REFERENCE TO"))
                return null;

            return 0;
        }

        // Builds a STRUCT default: every declared field at its own
        // DefaultValue first, then overlays the struct literal initializer
        // (if any) on top - unset fields keep the type's default value per
        // TwinCAT's documented partial-initialization behavior
        // (TcXunit-w5x.15.6 / T7).
        private StructInstance BuildStructDefault(StructAst structAst, string literalText, FbInstance owningInstance)
        {
            var instance = new StructInstance(structAst.Name);
            foreach (var field in structAst.Fields)
                instance.Fields[field.Name] = new Cell { Value = DefaultValue(field, owningInstance) };

            if (literalText != null && Parser.ParseExpression(literalText) is StructLiteralExpr lit)
                OverlayStruct(instance, lit, new Frame(owningInstance, structAst.Name));

            return instance;
        }

        // Builds an ARRAY default: every element at the declared element
        // type's DefaultValue, then overlays the array literal initializer
        // (if any) positionally - unset trailing elements keep the element
        // type's default, matching the array literal's own partial-init
        // shorthand (TcXunit-w5x.15.6).
        private ArrayValue BuildArrayDefault(VarDecl decl, FbInstance owningInstance)
        {
            var (dimensions, elementTypeName) = ArrayTypeInfo.Parse(decl.TypeName);
            var count = dimensions.Aggregate(1, (acc, d) => acc * (d.Hi - d.Lo + 1));
            var elementDecl = new VarDecl(null, elementTypeName, null, VarSection.Local);

            var elements = new object[count];
            for (var i = 0; i < count; i++)
                elements[i] = DefaultValue(elementDecl, owningInstance);

            var array = new ArrayValue(dimensions, elementTypeName, elements);

            if (decl.DefaultValueText != null && Parser.ParseExpression(decl.DefaultValueText) is ArrayLiteralExpr lit)
                OverlayArray(array, lit, new Frame(owningInstance, elementTypeName));

            return array;
        }

        // Applies a struct/array literal's per-field/per-element expression
        // on top of an already-defaulted value: nested struct/array fields
        // recurse into the matching overlay so partial initializers compose
        // (e.g. an ARRAY-of-STRUCT field overriding only some elements).
        // Anything else is a plain evaluate + assignment-coerce.
        private object OverlayOrEvaluate(object existingDefault, Expr expr, Frame frame)
        {
            if (expr is StructLiteralExpr structLit && existingDefault is StructInstance structInst)
            {
                OverlayStruct(structInst, structLit, frame);
                return structInst;
            }

            if (expr is ArrayLiteralExpr arrayLit && existingDefault is ArrayValue arrayVal)
            {
                OverlayArray(arrayVal, arrayLit, frame);
                return arrayVal;
            }

            return CoerceForAssignment(existingDefault, Evaluate(expr, frame));
        }

        private void OverlayStruct(StructInstance instance, StructLiteralExpr lit, Frame frame)
        {
            foreach (var init in lit.FieldInits)
            {
                if (!instance.Fields.TryGetValue(init.Name, out var cell))
                    throw new InvalidOperationException($"Unknown field '{init.Name}' on struct '{instance.TypeName}'");
                cell.Value = OverlayOrEvaluate(cell.Value, init.Value, frame);
            }
        }

        private void OverlayArray(ArrayValue array, ArrayLiteralExpr lit, Frame frame)
        {
            for (var i = 0; i < lit.Elements.Count && i < array.Elements.Length; i++)
                array.Elements[i] = OverlayOrEvaluate(array.Elements[i], lit.Elements[i], frame);
        }

        public void ExecuteStatements(IReadOnlyList<Stmt> statements, Frame frame)
        {
            foreach (var stmt in statements)
                ExecuteStatement(stmt, frame);
        }

        private void ExecuteStatement(Stmt stmt, Frame frame)
        {
            switch (stmt)
            {
                case AssignStmt assign:
                    SetLValue(assign.Target, Evaluate(assign.Value, frame), frame);
                    break;
                case RefAssignStmt refAssign:
                    frame.Locals[refAssign.TargetName] = ResolveCellForLValue(refAssign.Value, frame);
                    break;
                case IfStmt ifStmt:
                    if ((bool)Evaluate(ifStmt.Condition, frame))
                        ExecuteStatements(ifStmt.Then, frame);
                    else
                        ExecuteStatements(ifStmt.Else, frame);
                    break;
                case ExprStmt exprStmt:
                    Evaluate(exprStmt.Call, frame);
                    break;
                case ForStmt forStmt:
                    ExecuteFor(forStmt, frame);
                    break;
                case WhileStmt whileStmt:
                    ExecuteWhile(whileStmt, frame);
                    break;
                case RepeatStmt repeatStmt:
                    ExecuteRepeat(repeatStmt, frame);
                    break;
                case CaseStmt caseStmt:
                    ExecuteCase(caseStmt, frame);
                    break;
                case ExitStmt:
                    throw new LoopExitSignal();
                case ReturnStmt:
                    throw new MethodReturnSignal();
                default:
                    throw new NotSupportedException($"Statement type {stmt.GetType().Name} not supported");
            }
        }

        // Internal unwind signal for EXIT (TcXunit-mym.3): caught only by the
        // nearest enclosing FOR/WHILE/REPEAT's try/catch below, so it passes
        // straight through any IF/CASE it's lexically nested inside without
        // being special-cased there.
        private sealed class LoopExitSignal : Exception
        {
        }

        // Internal unwind signal for RETURN (TcXunit-mym.2): caught only at
        // CallMethod's and RunSuite's top-level ExecuteStatements call, so it
        // passes straight through any IF/FOR/WHILE/etc it's lexically nested
        // inside without being special-cased there.
        private sealed class MethodReturnSignal : Exception
        {
        }

        private void ExecuteFor(ForStmt stmt, Frame frame)
        {
            var from = Convert.ToInt32(Evaluate(stmt.From, frame));
            var to = Convert.ToInt32(Evaluate(stmt.To, frame));
            var step = stmt.Step != null ? Convert.ToInt32(Evaluate(stmt.Step, frame)) : 1;

            if (step == 0)
                throw new InvalidOperationException("FOR loop step must not be zero");

            try
            {
                for (var i = from; step > 0 ? i <= to : i >= to; i += step)
                {
                    SetVariable(stmt.VarName, i, frame);
                    ExecuteStatements(stmt.Body, frame);
                }
            }
            catch (LoopExitSignal)
            {
            }
        }

        private void ExecuteWhile(WhileStmt stmt, Frame frame)
        {
            try
            {
                while ((bool)Evaluate(stmt.Condition, frame))
                    ExecuteStatements(stmt.Body, frame);
            }
            catch (LoopExitSignal)
            {
            }
        }

        private void ExecuteRepeat(RepeatStmt stmt, Frame frame)
        {
            try
            {
                do
                {
                    ExecuteStatements(stmt.Body, frame);
                } while (!(bool)Evaluate(stmt.Until, frame));
            }
            catch (LoopExitSignal)
            {
            }
        }

        private void ExecuteCase(CaseStmt stmt, Frame frame)
        {
            var selectorInt = ToCaseInt(Evaluate(stmt.Selector, frame));

            foreach (var arm in stmt.Arms)
            {
                if (arm.Labels.Any(label => CaseLabelMatches(label, selectorInt, frame)))
                {
                    ExecuteStatements(arm.Body, frame);
                    return;
                }
            }

            ExecuteStatements(stmt.ElseBody, frame);
        }

        private bool CaseLabelMatches(CaseLabel label, int selectorInt, Frame frame)
        {
            var from = ToCaseInt(Evaluate(label.From, frame));
            if (!label.IsRange)
                return from == selectorInt;

            var to = ToCaseInt(Evaluate(label.To, frame));
            return selectorInt >= from && selectorInt <= to;
        }

        private static int ToCaseInt(object value) => value switch
        {
            int i => i,
            bool b => b ? 1 : 0,
            _ => Convert.ToInt32(value),
        };

        private static void SetVariable(string name, object value, Frame frame)
        {
            var cell = frame.ResolveCell(name);
            if (cell == null)
            {
                cell = new Cell();
                frame.Locals[name] = cell;
            }
            cell.Value = CoerceForAssignment(cell.Value, value);
        }

        // Assignment-target dispatch: identifiers go through SetVariable (may
        // implicitly declare a local), field/index targets write directly into
        // the already-allocated Cell/array slot they resolve to.
        private void SetLValue(Expr target, object value, Frame frame)
        {
            switch (target)
            {
                case IdentifierExpr id:
                    SetVariable(id.Name, value, frame);
                    break;
                case FieldAccessExpr fieldAccess:
                {
                    var fields = FieldsOf(Evaluate(fieldAccess.Receiver, frame));
                    if (!fields.TryGetValue(fieldAccess.FieldName, out var cell))
                        throw new InvalidOperationException($"Unknown field '{fieldAccess.FieldName}'");
                    cell.Value = CoerceForAssignment(cell.Value, value);
                    break;
                }
                case IndexExpr index:
                {
                    var array = (ArrayValue)Evaluate(index.Receiver, frame);
                    var flat = FlattenIndex(array, index.Indices, frame);
                    array.Elements[flat] = CoerceForAssignment(array.Elements[flat], value);
                    break;
                }
                default:
                    throw new NotSupportedException($"Assignment target {target.GetType().Name} not supported");
            }
        }

        // Row-major flattening against the array's declared per-dimension
        // lo..hi bounds, matching ArrayTypeInfo's dimension order.
        private int FlattenIndex(ArrayValue array, IReadOnlyList<Expr> indexExprs, Frame frame)
        {
            if (indexExprs.Count != array.Dimensions.Count)
                throw new InvalidOperationException(
                    $"Array has {array.Dimensions.Count} dimension(s) but {indexExprs.Count} index/indices given");

            var flat = 0;
            for (var d = 0; d < array.Dimensions.Count; d++)
            {
                var (lo, hi) = array.Dimensions[d];
                var idx = (int)Evaluate(indexExprs[d], frame);
                if (idx < lo || idx > hi)
                    throw new IndexOutOfRangeException($"Array index {idx} out of bounds [{lo}..{hi}] in dimension {d}");

                var dimSize = hi - lo + 1;
                flat = flat * dimSize + (idx - lo);
            }
            return flat;
        }

        // INT->REAL->LREAL widens implicitly on assignment (inferred from the
        // target cell's current CLR type, since Cell carries no declared-type tag
        // of its own); the reverse requires an explicit X_TO_Y cast produced by
        // TryEvaluateCast, which never returns a wider CLR type than the cast
        // target - so a rejection here means the assignment skipped a cast.
        private static object CoerceForAssignment(object existing, object incoming)
        {
            if (existing is int && incoming is float)
                throw new InvalidOperationException("Implicit narrowing from REAL to INT is not allowed; use REAL_TO_INT(...)");
            if (existing is int && incoming is double)
                throw new InvalidOperationException("Implicit narrowing from LREAL to INT is not allowed; use LREAL_TO_INT(...)");
            if (existing is float && incoming is double)
                throw new InvalidOperationException("Implicit narrowing from LREAL to REAL is not allowed; use LREAL_TO_REAL(...)");

            if (existing is float && incoming is int intForFloat)
                return (float)intForFloat;
            if (existing is double && (incoming is int || incoming is float))
                return Convert.ToDouble(incoming);

            return incoming;
        }

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

            throw new InvalidOperationException($"Loopback fault method missing required argument '{paramName}'");
        }

        private Cell ResolveCellForLValue(Expr expr, Frame frame)
        {
            if (expr is IdentifierExpr id)
            {
                var cell = frame.ResolveCell(id.Name);
                if (cell == null)
                    throw new InvalidOperationException($"Unknown variable '{id.Name}'");
                return cell;
            }

            if (expr is FieldAccessExpr fieldAccess)
            {
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
                    if (cell == null)
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

            if (binary.Op == "AND" || binary.Op == "OR" || binary.Op == "XOR")
                return EvaluateBitstring(binary.Op, leftVal, rightVal);

            if (binary.Op == "MOD")
                return EvaluateMod(leftVal, rightVal);

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

        // MEMCPY/MEMSET/MEMMOVE (TcXunit-sej.3): dest/src must be pointers to
        // an array element (see ResolveCellForAdr/ArrayElementCell) - this is
        // the POINTER TO BYTE over ARRAY OF BYTE buffer-packing case these
        // intrinsics exist for. n counts elements (== bytes for a BYTE/SINT/
        // USINT-element array); out-of-range access throws naturally via the
        // backing Elements[] indexer.
        private Pointer RequirePointerArg(CallExpr call, int index, Frame frame)
        {
            var value = Evaluate(call.PositionalArgs[index], frame);
            if (!(value is Pointer ptr))
                throw new InvalidOperationException(
                    $"{call.MethodName} argument {index} must be a POINTER TO BYTE (e.g. ADR(buf) or ADR(buf[i])), got {value?.GetType().Name}");
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
                    return MemCopy(
                        RequirePointerArg(call, 0, frame),
                        RequirePointerArg(call, 1, frame),
                        (int)Evaluate(call.PositionalArgs[2], frame),
                        overlapSafe: call.MethodName == "MEMMOVE");

                if (call.MethodName == "MEMSET")
                    return MemSet(
                        RequirePointerArg(call, 0, frame),
                        Evaluate(call.PositionalArgs[1], frame),
                        (int)Evaluate(call.PositionalArgs[2], frame));

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
