using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Runner.TcUnitStub;

namespace xStunit.Interpreter
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
            // instance is null when the body making this call is a global
            // FUNCTION's rather than an FB method's (CallGlobalFunction builds
            // a Frame with no instance) - a FUNCTION has no `THIS`, so an
            // unqualified call from inside one has no ancestry to dispatch
            // against. Everything below therefore treats a null instance as
            // "no methods, no fields, no native host" and falls through to the
            // global-FUNCTION / native-function lookups at the bottom, instead
            // of dereferencing it and throwing a bare NullReferenceException
            // that names neither the call nor the missing function
            // (TcXunit-kii).
            var startType = dispatchStartTypeOverride ?? instance?.ActualTypeName;

            string definingType = null;
            xStunit.Parser.MethodAst methodDef = null;
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
                if (instance != null && methodName == "StepCycles" && positionalArgs.Count == 1)
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
                //
                // What the callee IS decides how it is invoked, and it already
                // knows: NewInstance stamped its NativeKind at construction
                // (TcXunit-kwv6), so this switches on that one discriminator
                // (TcXunit-fvp6) rather than re-deriving the classification
                // from an ordered chain of null checks over four host fields.
                // Loopback and Suite callees deliberately match no case: a
                // bare call on either is not an invocation at all, and falls
                // through to the loopback-fault and TcUnit-stub routing below
                // exactly as it did when they failed every null check.
                if (TryResolveCalleeCell(callerFrame, instance, methodName, out var calleeCell) &&
                    calleeCell.Value is FbInstance callee)
                {
                    switch (callee.NativeKind)
                    {
                        case NativeHostKind.Timer:
                            BindNativeInputs(callee, TimerPositionalParams, positionalArgs, namedArgs, callerFrame);
                            // Nanoseconds, not TotalMs: the LTIME timers
                            // (LTON/LTOF/LTP) count in ns, and the host scales
                            // back to its own PT/ET width (TcXunit-x5pt).
                            callee.NativeTimerHost.Update(callee, Clock.TotalNs);
                            return null;

                        // Native R_TRIG/F_TRIG, e.g. fbTrig(CLK:=x) - same
                        // precedent as the native timer above, but the host
                        // only tracks CLK->Q (no PT/ET, no clock dependency).
                        case NativeHostKind.Edge:
                            BindNativeInputs(callee, EdgeTriggerPositionalParams, positionalArgs, namedArgs, callerFrame);
                            callee.NativeEdgeTriggerHost.Update(callee);
                            return null;

                        // Native RS/SR, e.g. fbLatch(SET:=x, RESET1:=y) - like
                        // the edge trigger above, but with two inputs whose
                        // names differ between RS and SR, so the host supplies
                        // them (TcXunit-ejjl).
                        case NativeHostKind.BistableLatch:
                            BindNativeInputs(callee, callee.NativeBistableLatchHost.PositionalInputNames, positionalArgs, namedArgs, callerFrame);
                            callee.NativeBistableLatchHost.Update(callee);
                            return null;

                        // Native CTU/CTD/CTUD, e.g. fbCounter(CU:=x, PV:=3) -
                        // same shape as the latch above; the counters disagree
                        // on both the number and the names of their inputs, so
                        // the host supplies them (TcXunit-l64b).
                        case NativeHostKind.Counter:
                            BindNativeInputs(callee, callee.NativeCounterHost.PositionalInputNames, positionalArgs, namedArgs, callerFrame);
                            callee.NativeCounterHost.Update(callee);
                            return null;

                        // Ordinary interpreted (non-native) FB field or
                        // method-local var, e.g. sfbLoopback(ibEnable := TRUE)
                        // - generalizes the native-timer bare-invoke above:
                        // bind VAR_INPUT/VAR_IN_OUT args into the callee's
                        // persisted Fields, then run its top-level body once
                        // (TcXunit-0v1). The registry guard stays: None only
                        // says "no native stub", and an FbInstance whose type
                        // the registry doesn't know has no body to run.
                        case NativeHostKind.None when _registry.Get(callee.ActualTypeName) != null:
                            InvokeFbInstance(callee, positionalArgs, namedArgs, callerFrame);
                            return null;
                    }
                }

                // Method-name routing WITHIN the loopback host kind - a
                // different question from the host-kind classification above,
                // so it keeps its own switch; only its guard reads the shared
                // discriminator (TcXunit-fvp6).
                if (instance?.NativeKind == NativeHostKind.Loopback && IsLoopbackFaultMethod(methodName))
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

                // CanInvoke gate (TcXunit-6k2): route to the TcUnit native-stub
                // boundary only for names it actually implements. Without it, a
                // suite instance sent *every* unresolved call here and got
                // NativeMethodBridge's "isn't supported yet" throw, which made
                // the global-FUNCTION and native-function lookups below
                // unreachable from inside a suite - i.e. a suite could not call
                // a global FUNCTION POU at all.
                if (instance?.NativeKind == NativeHostKind.Suite && NativeMethodBridge.CanInvoke(methodName))
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

                    // TcXunit-3tx.2: hand the host the call's name and the
                    // caller frame's "you are here" position before dispatching,
                    // so a failure recorded inside can say which assert failed
                    // and where it is written. Announced here rather than
                    // inside NativeMethodBridge because this is the only side
                    // that has the Frame.
                    instance.NativeSuiteHost.EnterNativeCall(
                        methodName,
                        new AssertSite(
                            callerFrame.DeclaringTypeName,
                            callerFrame.MethodName,
                            callerFrame.CurrentFileLine,
                            callerFrame.CurrentLine));

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

                // Host-registered stand-in for a compiled-only TwinCAT library
                // function (TcXunit-6k2), e.g. Tc2_Utilities' F_CheckSum16 -
                // there is no .TcPOU anywhere to parse for these, so nothing
                // above could ever have resolved them.
                //
                // Deliberately the LAST thing tried, after the global-FUNCTION
                // POU lookup directly above: if the user's own tree really does
                // contain source for this name, that source wins. A plugin can
                // only fill a hole that would otherwise have been the error
                // below - it can never shadow interpreted code.
                if (_nativeFunctions.TryGet(methodName, out var nativeFunction))
                    return InvokeNativeFunction(nativeFunction, methodName, positionalArgs, namedArgs, callerFrame);

                // A suite receiver that got this far named something the TcUnit
                // stub doesn't implement AND that isn't a POU or native
                // function either - almost always a TcUnit assert/API, or an
                // unimplemented IEC standard-library function (e.g. SEL), not
                // wired up yet, so keep saying exactly that (TcXunit-6k2). But
                // only when the name actually looks like it belongs to one of
                // those external surfaces (see
                // NativeMethodBridge.LooksLikeTcUnitApiName for exactly what
                // that means) - otherwise this unconditionally classified
                // every unresolved unqualified suite-body call as an
                // interpreter gap (kind=unsupported-construct, "STOP, don't
                // edit the POU"), even a plain typo of one of the suite's own
                // methods (e.g. 'CounterStartsAtZeroo()' for
                // 'CounterStartsAtZero()'), which is a real, fixable defect
                // and belongs on the ordinary method-not-found path below
                // (kind=plc-fault) instead (TcXunit-2o9.1).
                if (instance?.NativeKind == NativeHostKind.Suite && NativeMethodBridge.LooksLikeTcUnitApiName(methodName))
                    throw NativeMethodBridge.NotSupported(methodName);

                // startType is null for a call made from a global FUNCTION body
                // (no instance, so no ancestry to have searched) - saying "from
                // type ''" there would be nonsense, so name the real situation
                // instead (TcXunit-kii).
                throw new InvalidOperationException(
                    startType != null
                        ? $"Method '{methodName}' not found starting from type '{startType}'"
                        : $"Function '{methodName}' not found: no FUNCTION/FUNCTION_BLOCK POU of that name was " +
                          "loaded, and no native function is registered for it. If it comes from a compiled-only " +
                          "TwinCAT library, supply it via a native-function plugin.");
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

            ExecuteBody(() => _registry.GetStatements(methodDef.ImplementationText), newFrame);

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
        // IEC numeric return types are seeded first, keyed off the narrowing/
        // widening rule they feed (their defaults are plain boxed zeros - see
        // TryGetNumericCallableType).
        //
        // TcXunit-qft: the remaining *elementary* return types
        // (BOOL/STRING/W?STRING(n)/TIME/LTIME/DATE/DATE_AND_TIME/
        // TIME_OF_DAY) carry none of that narrowing hazard, but they do have
        // a plain constant default already sitting in Engine.DefaultValue
        // (false/""/0u/0ul) - seeding them too is just as cheap, and means a
        // METHOD/FUNCTION that returns without assigning every path (e.g. a
        // BOOL-returning method with an early-out that never sets its own
        // name) reads its IEC default out of CallMethod instead of null,
        // which a caller's cast/comparison (IF bIsReady() THEN ...) can't
        // handle. Both the classification and the value come from
        // IecElementaryDefault (TcXunit-om7f), the same owner Engine.
        // DefaultValue defers to - nothing is re-derived here.
        //
        // DUT/FB/interface/POINTER return types are deliberately left
        // unseeded - no cell until first assignment, null out of CallMethod
        // when never assigned, exactly as before this ticket. Those would
        // need DefaultValue's full construction path (materializing a struct/
        // FB instance, or resolving a POINTER's null), which for a return
        // value that carries no narrowing hazard of its own is too much to
        // do speculatively just to seed it.
        private void SeedReturnCell(Frame frame, string name, string declarationText)
        {
            if (TryGetNumericCallableType(declarationText, out var numericType, out var zero))
            {
                frame.Locals[name] = new Cell { Value = zero, DeclaredTypeName = numericType };
                frame.LocalTypeNames[name] = numericType;
                return;
            }

            var declaredType = _registry.GetReturnTypeName(declarationText);
            if (declaredType == null)
                return;

            var resolvedType = _registry.ResolveAlias(declaredType);
            if (!IecElementaryDefault.TryGetDefault(resolvedType, out var value))
                return;

            frame.Locals[name] = new Cell { Value = value, DeclaredTypeName = declaredType };
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
            xStunit.Parser.PouAst functionDef,
            IReadOnlyList<Expr> positionalArgs,
            IReadOnlyList<NamedArg> namedArgs,
            Frame callerFrame)
        {
            var newFrame = new Frame(null, functionDef.Name);
            SeedReturnCell(newFrame, functionDef.Name, functionDef.DeclarationText);
            var paramDecls = _registry.GetDecls(functionDef.DeclarationText);
            BindParams(paramDecls, positionalArgs, namedArgs, callerFrame, newFrame);

            // TcXunit-n65: routed through ExecuteBody (rather than the old
            // hand-rolled try/catch(MethodReturnSignal)) so a lazy parse
            // failure in functionDef.ImplementationText is attributed to
            // this function's own frame, same as CallMethod/InvokeFbInstance
            // below.
            ExecuteBody(() => _registry.GetStatements(functionDef.ImplementationText), newFrame);

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

            // A global FUNCTION frame has no instance (TcXunit-kii) - its own
            // locals were already consulted above (null == null makes the
            // self-invocation test true), and there are no instance Fields
            // behind them.
            if (instance == null)
            {
                cell = null;
                return false;
            }

            return instance.Fields.TryGetValue(name, out cell);
        }

        // IN/PT bound by position (IEC order) or by name; unset args keep the
        // timer instance's current field value (e.g. a caller that only ever
        // passes IN relies on PT staying whatever it was last set to).
        private static readonly string[] TimerPositionalParams = { "IN", "PT" };

        // R_TRIG/F_TRIG have a single VAR_INPUT (CLK).
        private static readonly string[] EdgeTriggerPositionalParams = { "CLK" };

        // Binds a bare invocation's arguments into a native stub's already-
        // seeded VAR_INPUT Cells: positionally against inputNames (the
        // callee's VAR_INPUTs in IEC declaration order), then by name. Unset
        // params deliberately keep whatever the instance already held, so a
        // caller that passes only CU relies on PV staying where it was.
        //
        // One helper for all four native families (TcXunit-l64b): the loop is
        // identical, only the name list differs, and for RS/SR (TcXunit-ejjl)
        // and CTU/CTD/CTUD it isn't even a constant - the two latches and the
        // three counters each spell their inputs differently, so those call
        // sites pass the list straight off the callee's own host rather than
        // re-spelling it here.
        private void BindNativeInputs(
            FbInstance callee,
            IReadOnlyList<string> inputNames,
            IReadOnlyList<Expr> positionalArgs,
            IReadOnlyList<NamedArg> namedArgs,
            Frame callerFrame)
        {
            for (var i = 0; i < positionalArgs.Count && i < inputNames.Count; i++)
                callee.Fields[inputNames[i]].Value = Evaluate(positionalArgs[i], callerFrame);

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

        // Generalizes BindNativeInputs beyond the native TON/TOF/FB_Pulse
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
            ExecuteBody(() => _registry.GetStatements(def.ImplementationText), calleeFrame);
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
                    // TcXunit-3d1: DefaultValue runs here, BEFORE ExecuteBody
                    // is ever entered for newFrame - so its own try/catch
                    // (Engine.Diagnostics.cs) can't attribute a fault raised
                    // while constructing this callee's own local/unsupplied-
                    // param default (e.g. a nested FB's default-value
                    // construction hitting an unresolved identifier). Without
                    // this, the fault surfaces unattributed at whatever
                    // frame is still active further up the CLR stack -
                    // typically the caller - with a call stack of exactly one
                    // frame no matter how deep the real failure is. Argument
                    // *evaluation* just above (Evaluate(argExpr, callerFrame))
                    // is deliberately left outside this wrapper: that runs in
                    // the CALLER's frame/scope, and a fault there is
                    // legitimately the caller's, already covered by the
                    // caller's own ExecuteBody.
                    value = RunWithFaultAttribution(
                        () => DefaultValue(decl, newFrame.Instance),
                        newFrame);
                }

                newFrame.Locals[decl.Name] = new Cell { Value = value, DeclaredTypeName = decl.TypeName };
                newFrame.LocalTypeNames[decl.Name] = decl.TypeName;
            }
        }
    }
}
