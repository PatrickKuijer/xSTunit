using System;
using System.Collections.Generic;
using System.Linq;
using TcXunit.Runner.TcUnitStub;

namespace TcXunit.Interpreter
{
    public sealed partial class Engine
    {
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
                // The FB-typed variable may be a top-level instance field, or
                // a METHOD-local VAR (e.g. a TEST case declaring
                // `sfbDigitalInput : FB_DigitalInputFilter` and calling
                // sfbDigitalInput()) - the latter lives in the caller frame's
                // Locals, not instance.Fields, so check both (locals take
                // precedence, mirroring Frame.ResolveCell).
                if (TryResolveCalleeCell(callerFrame, instance, methodName, out var calleeCell) &&
                    calleeCell.Value is FbInstance callee &&
                    callee.NativeTimerHost != null)
                {
                    BindTimerInputs(callee, positionalArgs, namedArgs, callerFrame);
                    callee.NativeTimerHost.Update(callee, Clock.TotalMs);
                    return null;
                }

                // Bare invocation of a native R_TRIG/F_TRIG, e.g.
                // fbTrig(CLK:=x) - same precedent as the native-timer
                // bare-invoke above, but the host only tracks CLK->Q (no
                // PT/ET, no clock dependency).
                if (TryResolveCalleeCell(callerFrame, instance, methodName, out var edgeCalleeCell) &&
                    edgeCalleeCell.Value is FbInstance edgeCallee &&
                    edgeCallee.NativeEdgeTriggerHost != null)
                {
                    BindEdgeTriggerInputs(edgeCallee, positionalArgs, namedArgs, callerFrame);
                    edgeCallee.NativeEdgeTriggerHost.Update(edgeCallee);
                    return null;
                }

                // Bare invocation of an ordinary interpreted (non-native) FB
                // field or method-local var, e.g. sfbLoopback(ibEnable := TRUE)
                // - generalizes the native-timer bare-invoke above: bind
                // VAR_INPUT/VAR_IN_OUT args into the callee's persisted
                // Fields, then run its top-level body once (TcXunit-0v1).
                if (TryResolveCalleeCell(callerFrame, instance, methodName, out var interpretedCalleeCell) &&
                    interpretedCalleeCell.Value is FbInstance interpretedCallee &&
                    interpretedCallee.NativeTimerHost == null &&
                    interpretedCallee.NativeLoopbackHost == null &&
                    interpretedCallee.NativeSuiteHost == null &&
                    interpretedCallee.NativeEdgeTriggerHost == null &&
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
                            var n = Convert.ToInt32(Evaluate(ResolveNamedOrPositionalArg("SetDelay", "n", 0, positionalArgs, namedArgs), callerFrame));
                            instance.NativeLoopbackHost.SetDelay(instance, n);
                            break;
                        case "Duplicate":
                            instance.NativeLoopbackHost.Duplicate(instance);
                            break;
                        case "Corrupt":
                            var value = Evaluate(ResolveNamedOrPositionalArg("Corrupt", "value", 0, positionalArgs, namedArgs), callerFrame);
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

                    // TcXunit-gd2.5: AssertEquals(Expected: ANY, Actual: ANY,
                    // Message) needs Expected/Actual's *declared* IEC type to
                    // pick the matching AssertEquals_<TYPE> - the interpreter
                    // has no real ANY value carrying its own runtime type tag
                    // (unlike a TwinCAT ANY struct's TypeClass/pValue/diSize),
                    // so this resolves it from the *expression* the same way
                    // SIZEOF() does (Engine.SizeOf.cs), before it's evaluated
                    // away to a bare CLR value above - several IEC scalar
                    // types share the same CLR representation once evaluated
                    // (see IecNumericType.cs/ScalarAssertType.cs) and can't be
                    // told apart from the value alone.
                    IReadOnlyDictionary<string, string> anyTypeNames = null;
                    if (methodName == "AssertEquals")
                    {
                        var expectedExpr = ResolveNamedOrPositionalArg("AssertEquals", "Expected", 0, positionalArgs, namedArgs);
                        var actualExpr = ResolveNamedOrPositionalArg("AssertEquals", "Actual", 1, positionalArgs, namedArgs);
                        anyTypeNames = new Dictionary<string, string>
                        {
                            ["Expected"] = ResolveDeclaredTypeName(expectedExpr, callerFrame),
                            ["Actual"] = ResolveDeclaredTypeName(actualExpr, callerFrame),
                        };
                    }

                    return NativeMethodBridge.Invoke(instance.NativeSuiteHost, methodName, evaluatedPositional, evaluatedNamed, anyTypeNames);
                }

                // Unqualified call inside a METHOD body naming neither an
                // ancestor method nor a callee field: falls back to a
                // top-level global FUNCTION POU of the same name
                // (TcXunit-9su) - a plain FUNCTION has no Method children and
                // no FUNCTION_BLOCK/PROGRAM declaration keyword, so it never
                // matched the ancestry walk above. Runs with no receiver
                // instance (a FUNCTION can't see the caller's FB fields,
                // only its own params/locals and GVLs via
                // TryResolveGlobalCell).
                var globalFunctionDef = _registry.Get(methodName);
                if (globalFunctionDef != null &&
                    GlobalFunctionDeclarationPattern.IsMatch(
                        CallableReturnTypeParser.StripLeadingComments(globalFunctionDef.DeclarationText)))
                    return CallGlobalFunction(globalFunctionDef, positionalArgs, namedArgs, callerFrame);

                throw new InvalidOperationException($"Method '{methodName}' not found starting from type '{startType}'");
            }

            // definingType (not instance.ActualTypeName) is the POU that owns
            // the body about to run, so an inherited method is attributed to
            // the base FB that actually declares it (TcXunit-p3t.1). Same
            // reason methodDef's own BodyStartLine is what travels: an
            // override and the method it overrides share nothing but a name,
            // and TypeRegistry caches parsed statements by body TEXT, so the
            // offset has to ride the frame rather than the statement list
            // (TcXunit-p3t.4).
            var newFrame = new Frame(instance, definingType, methodName, methodDef.BodyStartLine);
            SeedReturnCell(newFrame, methodName, methodDef.DeclarationText);
            var paramDecls = _registry.GetDecls(methodDef.DeclarationText);
            BindParams(paramDecls, positionalArgs, namedArgs, callerFrame, newFrame);

            ExecuteBody(_registry.GetStatements(methodDef.ImplementationText), newFrame);

            WriteBackOutputArgs(paramDecls, namedArgs, newFrame, callerFrame);

            return newFrame.Locals.TryGetValue(methodName, out var returnCell) ? returnCell.Value : null;
        }

        // No ^ anchor: DeclarationText may lead with a (* ... *) block
        // comment or // line comment (this codebase's standard convention -
        // see TcXunit-9k6), so instead of anchoring to the very start of the
        // string, the leading comment/whitespace run is stripped first
        // (CallableReturnTypeParser.StripLeadingComments - shared with the
        // header-return-type parse, which needs the identical treatment for
        // the identical reason) and the resulting text is anchored with ^.
        private static readonly System.Text.RegularExpressions.Regex GlobalFunctionDeclarationPattern =
            new System.Text.RegularExpressions.Regex(@"^\s*FUNCTION(?!_BLOCK)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // TcXunit-cq6: a callable's return value lives in a Local named after
        // the callable itself, and a Cell carries no declared-type tag - so
        // assignment compatibility is decided purely by the CLR type already
        // in the Cell (NumericCoercion.CoerceForAssignment). Left to be
        // created lazily by its first assignment, an LREAL-returning method
        // opening with 'M_Read := 0.0;' got a *REAL* cell (a bare decimal
        // literal lexes as REAL - see Engine.Defaults' TcXunit-5qs note), and
        // every later LREAL assignment into it was rejected as an implicit
        // narrowing. Seeding the cell from the declared return type up front
        // gives the narrowing rule the right type to judge against, the same
        // way BindParams already seeds each param from its VarDecl.
        //
        // Only IEC numeric return types are seeded. They're the ones the
        // narrowing/widening rule actually turns on, and their defaults are
        // plain boxed zeros. Other return types (a DUT, an interface, a
        // POINTER) would need DefaultValue's full construction path, which for
        // a registered type name means materializing an instance - too much to
        // do speculatively for a return value that carries no narrowing
        // hazard. Those keep today's behavior: no cell until first assignment,
        // null out of CallMethod when never assigned. The elementary
        // non-numerics (BOOL/STRING/TIME/DATE) sit between the two and are
        // tracked separately (TcXunit-qft).
        //
        // The IecNumericType lookup is case-sensitive, so a lowercase-spelled
        // return type ("METHOD M_Read : lreal") parses but doesn't seed. That
        // is deliberate consistency, not an oversight: every type-name lookup
        // in the interpreter (VarBlockParser decls through Engine.Defaults'
        // DefaultValue, StringTypeInfo, GetStruct) is likewise case-sensitive
        // even though ST itself is case-insensitive. Fixing it here alone
        // would just move the surprise - it needs doing across all of them at
        // once (TcXunit-fzm).
        private void SeedReturnCell(Frame frame, string name, string declarationText)
        {
            if (!TryGetNumericCallableType(declarationText, out var declaredType, out var zero))
                return;

            frame.Locals[name] = new Cell { Value = zero, DeclaredTypeName = declaredType };
            frame.LocalTypeNames[name] = declaredType;
        }

        // Shared by SeedReturnCell above and Engine.Properties' Get/Set
        // accessor seeding (TcXunit-8we): resolves declarationText's header
        // return/property type and reports it only when it's an IEC numeric
        // type, mirroring SeedReturnCell's original (TcXunit-cq6) scoping -
        // see that ticket's reasoning above for why non-numeric return types
        // are deliberately left alone.
        private bool TryGetNumericCallableType(string declarationText, out string declaredType, out object zero)
        {
            declaredType = _registry.GetReturnTypeName(declarationText);
            zero = null;
            if (declaredType == null)
                return false;

            return IecNumericType.TryGetDefault(_registry.ResolveAlias(declaredType), out zero);
        }

        // Global FUNCTION invocation (TcXunit-9su): same body-execution shape
        // as the METHOD path above, but with no receiver instance - a
        // FUNCTION's return value is written to a Local named after the
        // function itself (functionDef.Name), same IEC convention as METHOD.
        private object CallGlobalFunction(
            TcXunit.Parser.PouAst functionDef,
            IReadOnlyList<Expr> positionalArgs,
            IReadOnlyList<NamedArg> namedArgs,
            Frame callerFrame)
        {
            var newFrame = new Frame(null, functionDef.Name);
            SeedReturnCell(newFrame, functionDef.Name, functionDef.DeclarationText);
            var paramDecls = _registry.GetDecls(functionDef.DeclarationText);
            BindParams(paramDecls, positionalArgs, namedArgs, callerFrame, newFrame);

            try
            {
                ExecuteStatements(_registry.GetStatements(functionDef.ImplementationText), newFrame);
            }
            catch (MethodReturnSignal)
            {
            }

            WriteBackOutputArgs(paramDecls, namedArgs, newFrame, callerFrame);

            return newFrame.Locals.TryGetValue(functionDef.Name, out var returnCell) ? returnCell.Value : null;
        }

        // Name => expr call args (TcXunit-mym.5) bind a VAR_OUTPUT param's
        // value back into the caller-side lvalue after the call returns -
        // BindParams only reads namedArgs for Input/InOut, so this is the
        // only place output binding happens.
        private void WriteBackOutputArgs(
            IReadOnlyList<VarDecl> paramDecls,
            IReadOnlyList<NamedArg> namedArgs,
            Frame newFrame,
            Frame callerFrame)
        {
            foreach (var arg in namedArgs)
            {
                if (!arg.IsOutput)
                    continue;

                var decl = paramDecls.FirstOrDefault(d => d.Name == arg.Name && d.Section == VarSection.Output);
                if (decl == null)
                    continue;

                if (newFrame.Locals.TryGetValue(decl.Name, out var outCell))
                    SetLValue(arg.Value, outCell.Value, callerFrame);
            }
        }

        // Resolves a bare-invocation callee cell by name, checking the caller
        // frame's Locals (METHOD-local VARs) before the instance's persisted
        // Fields (top-level VARs) - mirrors Frame.ResolveCell's precedence.
        // callerFrame is null for a few top-level entry points (e.g. FB_init),
        // so only instance.Fields applies there.
        //
        // The Locals-before-Fields precedence is only valid for a genuine
        // bare/self invocation, where instance is the same FbInstance as
        // callerFrame.Instance (call.Receiver is null/ThisRefExpr/
        // SuperRefExpr in EvaluateCall). For an explicit non-self receiver
        // (someObj.Foo()), instance is the receiver's own FbInstance, which
        // may differ from callerFrame.Instance - in that case the caller's
        // locals are irrelevant scope and must not be consulted, or a
        // same-named local in the calling METHOD could shadow/hijack
        // resolution of a call meant for the receiver (TcXunit-3zk).
        private static bool TryResolveCalleeCell(Frame callerFrame, FbInstance instance, string name, out Cell cell)
        {
            if (callerFrame != null && instance == callerFrame.Instance && callerFrame.Locals.TryGetValue(name, out cell))
                return true;

            return instance.Fields.TryGetValue(name, out cell);
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

        // R_TRIG/F_TRIG have a single VAR_INPUT (CLK), bound by position or
        // by name same as BindTimerInputs.
        private static readonly string[] EdgeTriggerPositionalParams = { "CLK" };

        private void BindEdgeTriggerInputs(
            FbInstance callee,
            IReadOnlyList<Expr> positionalArgs,
            IReadOnlyList<NamedArg> namedArgs,
            Frame callerFrame)
        {
            for (var i = 0; i < positionalArgs.Count && i < EdgeTriggerPositionalParams.Length; i++)
                callee.Fields[EdgeTriggerPositionalParams[i]].Value = Evaluate(positionalArgs[i], callerFrame);

            foreach (var arg in namedArgs)
                if (callee.Fields.TryGetValue(arg.Name, out var cell))
                    cell.Value = Evaluate(arg.Value, callerFrame);
        }

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
                result.AddRange(_registry.GetDecls(def.DeclarationText)
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
                if (inputDecls.Any(d => d.Name == arg.Name) && callee.Fields.TryGetValue(arg.Name, out var cell))
                    cell.Value = Evaluate(arg.Value, callerFrame);

            var def = _registry.Get(callee.ActualTypeName);
            ResetTopLevelTempFields(callee);
            var calleeFrame = new Frame(callee, callee.ActualTypeName, null, def.BodyStartLine);
            ExecuteBody(_registry.GetStatements(def.ImplementationText), calleeFrame);
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

                if ((decl.Section == VarSection.Input || decl.Section == VarSection.InOut) &&
                    ArgBinder.TryResolveArg(
                        decl.Name,
                        name => namedArgs.FirstOrDefault(a => a.Name == name)?.Value,
                        positionalArgs,
                        ref posIndex,
                        out var argExpr))
                {
                    value = Evaluate(argExpr, callerFrame);
                }
                else
                {
                    value = DefaultValue(decl, newFrame.Instance);
                }

                newFrame.Locals[decl.Name] = new Cell { Value = value, DeclaredTypeName = decl.TypeName };
                newFrame.LocalTypeNames[decl.Name] = decl.TypeName;
            }
        }
    }
}
