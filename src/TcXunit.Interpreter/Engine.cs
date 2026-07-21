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

        public Engine(TypeRegistry registry)
        {
            _registry = registry;
        }

        public IReadOnlyList<TestCaseResult> RunSuite(string suiteTypeName)
        {
            var instance = NewInstance(suiteTypeName);
            var def = _registry.Get(suiteTypeName);
            var frame = new Frame(instance, suiteTypeName);
            ExecuteStatements(Parser.ParseStatements(def.ImplementationText), frame);
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
                instance.NativeSuiteHost = new TcUnitSuiteHost();

            for (var i = chain.Count - 1; i >= 0; i--)
            {
                var def = _registry.Get(chain[i]);
                foreach (var decl in VarBlockParser.Parse(def.DeclarationText).Where(d => d.Section == VarSection.Local))
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

            ExecuteStatements(Parser.ParseStatements(methodDef.ImplementationText), newFrame);

            return newFrame.Locals.TryGetValue(methodName, out var returnCell) ? returnCell.Value : null;
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
            if (_registry.Get(decl.TypeName) != null)
                return NewInstance(decl.TypeName);

            if (decl.DefaultValueText != null)
                return Evaluate(Parser.ParseExpression(decl.DefaultValueText), new Frame(owningInstance, decl.TypeName));

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
                    SetVariable(assign.TargetName, Evaluate(assign.Value, frame), frame);
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
                default:
                    throw new NotSupportedException($"Statement type {stmt.GetType().Name} not supported");
            }
        }

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

        private static Cell ResolveCellForLValue(Expr expr, Frame frame)
        {
            if (expr is IdentifierExpr id)
            {
                var cell = frame.ResolveCell(id.Name);
                if (cell == null)
                    throw new InvalidOperationException($"Unknown variable '{id.Name}'");
                return cell;
            }

            throw new NotSupportedException("Only plain identifiers are supported as REF=/ADR() targets in v1");
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

            throw new NotSupportedException($"Unary operator '{unary.Op}' not supported");
        }

        private object EvaluateBinary(BinaryExpr binary, Frame frame)
        {
            var leftVal = Evaluate(binary.Left, frame);
            var rightVal = Evaluate(binary.Right, frame);

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
                    return new Pointer(ResolveCellForLValue(call.PositionalArgs[0], frame));

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
