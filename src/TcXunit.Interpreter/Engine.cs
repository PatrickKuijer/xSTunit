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
            cell.Value = value;
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
                case CallExpr call:
                    return EvaluateCall(call, frame);
                default:
                    throw new NotSupportedException($"Expression type {expr.GetType().Name} not supported");
            }
        }

        private object EvaluateBinary(BinaryExpr binary, Frame frame)
        {
            var left = (int)Evaluate(binary.Left, frame);
            var right = (int)Evaluate(binary.Right, frame);

            switch (binary.Op)
            {
                case "+": return left + right;
                case "-": return left - right;
                case "<": return left < right;
                case ">": return left > right;
                case "<=": return left <= right;
                case ">=": return left >= right;
                case "=": return left == right;
                case "<>": return left != right;
                default:
                    throw new NotSupportedException($"Operator '{binary.Op}' not supported");
            }
        }

        private object EvaluateCall(CallExpr call, Frame frame)
        {
            if (call.Receiver == null)
            {
                if (call.MethodName == "ADR")
                    return new Pointer(ResolveCellForLValue(call.PositionalArgs[0], frame));

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
    }
}
