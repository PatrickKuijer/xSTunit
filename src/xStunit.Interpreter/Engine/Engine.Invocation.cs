using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Parser;
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
            bool optionalIfMissing = false,
            bool unqualified = false)
        {
            // instance is null when the calling body is a global FUNCTION's
            // rather than an FB method's: a FUNCTION has no THIS, so an
            // unqualified call from inside one has no ancestry to dispatch
            // against. Everything below therefore has to treat a null instance
            // as "no methods, no fields, no native host" and fall through to the
            // global-FUNCTION and native-function lookups at the bottom, rather
            // than dereferencing it and throwing a bare NullReferenceException
            // that names neither the call nor the missing function.
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

                // Bare FB invocation, e.g. fbTon(IN:=x, PT:=t): the parser sees a
                // receiverless call on the current instance, but methodName here
                // names a field holding another FbInstance to invoke directly.
                // That FB-typed variable may be a top-level instance field or a
                // METHOD-local VAR - a TEST case declaring
                // `sfbDigitalInput : FB_DigitalInputFilter` and calling
                // sfbDigitalInput() - so both are checked, locals first,
                // mirroring Frame.ResolveCell.
                //
                // How the callee is invoked follows from what it IS, which it
                // already knows: NewInstance stamped its NativeKind at
                // construction. Loopback and Suite callees deliberately match no
                // case - a bare call on either is not an invocation at all, and
                // falls through to the loopback-fault and TcUnit-stub routing
                // below.
                if (TryResolveCalleeCell(callerFrame, instance, methodName, out var calleeCell) &&
                    calleeCell.Value is FbInstance callee)
                {
                    switch (callee.NativeKind)
                    {
                        case NativeHostKind.Timer:
                            BindNativeInputs(callee, TimerPositionalParams, positionalArgs, namedArgs, callerFrame);
                            // Nanoseconds, not TotalMs: the LTIME timers
                            // (LTON/LTOF/LTP) count in ns, and the host scales
                            // back to its own PT/ET width.
                            callee.NativeTimerHost.Update(callee, Clock.TotalNs);
                            WriteBackNativeOutputArgs(callee, namedArgs, callerFrame);
                            return null;

                        // Like the timer above, but the host only tracks CLK->Q:
                        // no PT/ET, and no clock dependency.
                        case NativeHostKind.Edge:
                            BindNativeInputs(callee, EdgeTriggerPositionalParams, positionalArgs, namedArgs, callerFrame);
                            callee.NativeEdgeTriggerHost.Update(callee);
                            WriteBackNativeOutputArgs(callee, namedArgs, callerFrame);
                            return null;

                        // RS and SR disagree on their two input names, so the
                        // host supplies them.
                        case NativeHostKind.BistableLatch:
                            BindNativeInputs(callee, callee.NativeBistableLatchHost.PositionalInputNames, positionalArgs, namedArgs, callerFrame);
                            callee.NativeBistableLatchHost.Update(callee);
                            WriteBackNativeOutputArgs(callee, namedArgs, callerFrame);
                            return null;

                        // CTU/CTD/CTUD disagree on both the number and the names
                        // of their inputs, so again the host supplies them.
                        case NativeHostKind.Counter:
                            BindNativeInputs(callee, callee.NativeCounterHost.PositionalInputNames, positionalArgs, namedArgs, callerFrame);
                            callee.NativeCounterHost.Update(callee);
                            WriteBackNativeOutputArgs(callee, namedArgs, callerFrame);
                            return null;

                        // A stateful library FB supplied from outside this
                        // assembly. Its inputs bind exactly as an in-tree
                        // stub's do, off the names the plugin declares;
                        // everything the call then means is the plugin's own
                        // business.
                        case NativeHostKind.Plugin:
                            var pluginCallee = callee.NativePluginFunctionBlock;
                            BindNativeInputs(callee, pluginCallee.PositionalInputNames, positionalArgs, namedArgs, callerFrame);
                            pluginCallee.Invoke(NewFunctionBlockCall(callee, null, null, callerFrame));
                            WriteBackNativeOutputArgs(callee, namedArgs, callerFrame);
                            return null;

                        // Ordinary interpreted FB field or method-local var:
                        // bind VAR_INPUT/VAR_IN_OUT args into the callee's
                        // persisted Fields, then run its top-level body once.
                        // The registry guard is required - None only says "no
                        // native stub", and an FbInstance whose type the
                        // registry doesn't know has no body to run.
                        case NativeHostKind.None when _registry.Get(callee.ActualTypeName) != null:
                            InvokeFbInstance(callee, positionalArgs, namedArgs, callerFrame);
                            return null;
                    }
                }

                // Method-name routing WITHIN a plugin function block, the same
                // shape as the loopback block below. Which names belong to the
                // plugin is what the plugin declares, not what it happens to
                // handle: the FB_init NewInstance speculatively calls on every
                // instance must fall through to the optional-method path rather
                // than arriving at a plugin as if it were part of the contract.
                if (instance?.NativeKind == NativeHostKind.Plugin &&
                    DeclaresMethod(instance.NativePluginFunctionBlock, methodName))
                {
                    var pluginArgs = NewNativeCallContext(methodName, positionalArgs, namedArgs, callerFrame);
                    return instance.NativePluginFunctionBlock.Invoke(
                        NewFunctionBlockCall(instance, methodName, pluginArgs, callerFrame));
                }

                // Method-name routing WITHIN the loopback host kind - a
                // different question from the host-kind classification above, so
                // it keeps its own switch and only its guard reads the kind.
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

                // The CanInvoke gate routes to the TcUnit native-stub boundary
                // only for names it actually implements. Without it a suite
                // instance sends every unresolved call here and gets
                // NativeMethodBridge's "isn't supported yet" throw, which makes
                // the global-FUNCTION and native-function lookups below
                // unreachable from inside a suite.
                if (instance?.NativeKind == NativeHostKind.Suite && NativeMethodBridge.CanInvoke(methodName))
                {
                    var evaluatedPositional = positionalArgs.Select(e => Evaluate(e, callerFrame)).ToList();
                    var evaluatedNamed = namedArgs
                        .Where(a => !a.IsUnboundOutput)
                        .ToDictionary(a => a.Name, a => Evaluate(a.Value, callerFrame));

                    // AssertEquals(Expected: ANY, Actual: ANY, Message) needs its
                    // arguments' *declared* IEC type to pick the matching
                    // AssertEquals_<TYPE>, and the interpreter has no ANY value
                    // carrying a runtime type tag of its own (unlike a TwinCAT
                    // ANY struct's TypeClass/pValue/diSize). Several IEC scalar
                    // types share one CLR representation once evaluated, so the
                    // type has to come from the *expression*, the way SIZEOF()
                    // resolves it - and before evaluation discards it.
                    IReadOnlyDictionary<string, string> anyTypeNames = null;
                    if (methodName == "AssertEquals")
                    {
                        var expectedExpr = ResolveNamedOrPositionalArg("AssertEquals", "Expected", 0, positionalArgs, namedArgs);
                        var actualExpr = ResolveNamedOrPositionalArg("AssertEquals", "Actual", 1, positionalArgs, namedArgs);
                        var typeClasses = ResolveAnyTypeClasses(expectedExpr, actualExpr, callerFrame);
                        anyTypeNames = new Dictionary<string, string>
                        {
                            ["Expected"] = typeClasses.Expected,
                            ["Actual"] = typeClasses.Actual,
                        };
                    }

                    // Hands the host the call's name and the caller frame's "you
                    // are here" position before dispatching, so a failure
                    // recorded inside can say which assert failed and where it
                    // is written. Announced here rather than inside
                    // NativeMethodBridge because this is the only side that has
                    // the Frame.
                    instance.NativeSuiteHost.EnterNativeCall(
                        methodName,
                        new AssertSite(
                            callerFrame.DeclaringTypeName,
                            callerFrame.MethodName,
                            callerFrame.CurrentFileLine,
                            callerFrame.CurrentLine));

                    return NativeMethodBridge.Invoke(instance.NativeSuiteHost, methodName, evaluatedPositional, evaluatedNamed, anyTypeNames);
                }

                // An unqualified call naming neither an ancestor method nor a
                // callee field falls back to a top-level global FUNCTION POU of
                // the same name: a plain FUNCTION has no Method children and no
                // FUNCTION_BLOCK/PROGRAM declaration keyword, so it can never
                // have matched the ancestry walk above. It runs with no receiver
                // instance - a FUNCTION sees only its own params and locals,
                // plus GVLs.
                if (TryGetGlobalFunctionDef(methodName, out var globalFunctionDef))
                    return CallGlobalFunction(globalFunctionDef, positionalArgs, namedArgs, callerFrame);

                // Host-registered stand-in for a compiled-only TwinCAT library
                // function, e.g. Tc2_Utilities' F_CheckSum16 - there is no
                // .TcPOU anywhere to parse for these, so nothing above could
                // ever have resolved them.
                //
                // Deliberately the LAST thing tried, after the global-FUNCTION
                // POU lookup directly above: if the user's own tree really does
                // contain source for this name, that source wins. A plugin can
                // only fill a hole that would otherwise have been the error
                // below - it can never shadow interpreted code.
                if (_nativeFunctions.TryGet(methodName, out var nativeFunction))
                    return InvokeNativeFunction(nativeFunction, methodName, positionalArgs, namedArgs, callerFrame);

                // A suite receiver that got this far named something the TcUnit
                // stub doesn't implement and that isn't a POU or native function
                // either - almost always a TcUnit assert/API or an unimplemented
                // IEC standard-library function (SEL, say), so say exactly that.
                //
                // The LooksLikeTcUnitApiName guard is what keeps that claim
                // narrow. Without it every unresolved unqualified suite-body call
                // is reported as an interpreter gap ("STOP, don't edit the POU"),
                // including a plain typo of one of the suite's own methods -
                // 'CounterStartsAtZeroo()' for 'CounterStartsAtZero()' - which is
                // a real, fixable defect and belongs on the method-not-found path
                // below as a plc-fault.
                if (instance?.NativeKind == NativeHostKind.Suite && NativeMethodBridge.LooksLikeTcUnitApiName(methodName))
                    throw NativeMethodBridge.NotSupported(methodName);

                // Three shapes, because the reader needs a different next step
                // in each. A QUALIFIED call (fbWidget.Foo(), THIS^.Foo()) named
                // a method and only a method, so it gets the ancestry it was
                // searched against and nothing else.
                //
                // An UNQUALIFIED call could have meant either a method on THIS
                // or a bare FUNCTION, and once neither resolved there is no way
                // to tell which was intended - so it names both. The enclosing
                // FB alone would be the wrong place to point: the common cause
                // is a compiled-only vendor function with no .TcPOU to parse,
                // whose remedy is a plugin rather than a method on that type.
                //
                // startType is null for a call made from a global FUNCTION body -
                // no instance, so no ancestry was ever searched - and "from type
                // ''" would be nonsense there.
                if (startType != null && !unqualified)
                    throw new InvalidOperationException(
                        $"Method '{methodName}' not found starting from type '{startType}'");

                var searched = startType == null
                    ? $"Function '{methodName}' not found: "
                    : $"'{methodName}' not found: type '{startType}' and its bases declare no method of that name, ";

                throw new InvalidOperationException(
                    searched +
                    "no FUNCTION/FUNCTION_BLOCK POU of that name was loaded, and no native function is " +
                    "registered for it. If it comes from a compiled-only TwinCAT library, supply it via a " +
                    "native-function plugin.");
            }

            // definingType, not instance.ActualTypeName: it is the POU owning
            // the body about to run, so an inherited method is attributed to the
            // base FB that declares it. methodDef's own BodyStartLine travels
            // for the same reason - an override and the method it overrides
            // share nothing but a name, and TypeRegistry caches parsed
            // statements by body TEXT, so the offset has to ride the frame
            // rather than the statement list.
            var newFrame = new Frame(instance, definingType, methodName, methodDef.BodyStartLine);
            SeedReturnCell(newFrame, methodName, methodDef.DeclarationText);
            var paramDecls = _registry.GetDecls(methodDef.DeclarationText);
            BindParams(paramDecls, positionalArgs, namedArgs, callerFrame, newFrame);

            ExecuteBody(() => _registry.GetStatements(methodDef.ImplementationText), newFrame);

            WriteBackOutputArgs(paramDecls, namedArgs, newFrame, callerFrame);

            return newFrame.Locals.TryGetValue(methodName, out var returnCell) ? returnCell.Value : null;
        }

        private bool TryGetGlobalFunctionDef(string name, out PouAst def)
        {
            def = _registry.Get(name);
            return def != null &&
                GlobalFunctionDeclarationPattern.IsMatch(
                    CallableReturnTypeParser.StripLeadingComments(def.DeclarationText));
        }

        // The ^ anchor only works because callers strip the leading comment run
        // first (CallableReturnTypeParser.StripLeadingComments, shared with the
        // header-return-type parse): DeclarationText commonly opens with a
        // (* ... *) or // comment, which would otherwise sit between the start
        // of the string and the FUNCTION keyword.
        private static readonly System.Text.RegularExpressions.Regex GlobalFunctionDeclarationPattern =
            new System.Text.RegularExpressions.Regex(@"^\s*FUNCTION(?!_BLOCK)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // A callable's return value lives in a Local named after the callable
        // itself, and assignment compatibility is decided from the CLR type
        // already in that Cell (NumericCoercion.CoerceForAssignment). Left to be
        // created by its first assignment, an LREAL-returning method opening
        // with 'M_Read := 0.0;' would get a *REAL* cell - a bare decimal literal
        // lexes as REAL - and every later LREAL assignment into it would be
        // rejected as an implicit narrowing. Seeding from the declared return
        // type gives that rule the right type to judge against, as BindParams
        // already does for each param.
        //
        // The remaining elementary return types
        // (BOOL/STRING/W?STRING(n)/TIME/LTIME/DATE/DATE_AND_TIME/TIME_OF_DAY)
        // carry no narrowing hazard, but seeding them is just as cheap and means
        // a METHOD/FUNCTION that returns without assigning on every path - a
        // BOOL-returning method with an early-out that never sets its own name -
        // yields its IEC default rather than a null a caller's comparison
        // ('IF bIsReady() THEN ...') cannot handle. Both classification and value
        // come from IecElementaryDefault, the same owner Engine.DefaultValue
        // defers to.
        //
        // DUT/FB/interface/POINTER return types are deliberately left unseeded:
        // no cell until first assignment, null out of CallMethod when never
        // assigned. Seeding those needs DefaultValue's full construction path -
        // materializing a struct or FB instance - which is too much to do
        // speculatively for a return value with no narrowing hazard of its own.
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

            frame.Locals[name] = NewDeclaredCell(value, declaredType, frame.Instance);
            frame.LocalTypeNames[name] = declaredType;
        }

        // Shared by SeedReturnCell above and Engine.Properties' Get/Set accessor
        // seeding: resolves declarationText's header return/property type, but
        // reports it only when it is an IEC numeric type - the types the
        // narrowing rule above turns on.
        private bool TryGetNumericCallableType(string declarationText, out string declaredType, out object zero)
        {
            declaredType = _registry.GetReturnTypeName(declarationText);
            zero = null;
            if (declaredType == null)
                return false;

            return IecNumericType.TryGetDefault(_registry.ResolveAlias(declaredType), out zero);
        }

        // Same body-execution shape as the METHOD path above, but with no
        // receiver instance. A FUNCTION's return value goes to a Local named
        // after the function itself, the same IEC convention as a METHOD.
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

            ExecuteBody(() => _registry.GetStatements(functionDef.ImplementationText), newFrame);

            WriteBackOutputArgs(paramDecls, namedArgs, newFrame, callerFrame);

            return newFrame.Locals.TryGetValue(functionDef.Name, out var returnCell) ? returnCell.Value : null;
        }

        private void WriteBackOutputArgs(
            IReadOnlyList<VarDecl> paramDecls,
            IReadOnlyList<NamedArg> namedArgs,
            Frame newFrame,
            Frame callerFrame) =>
            WriteBackOutputArgs(namedArgs, name => IsOutputDecl(paramDecls, name), newFrame.Locals, callerFrame);

        private static bool IsOutputDecl(IReadOnlyList<VarDecl> decls, string name) =>
            decls.Any(d => d.Name == name && d.Section == VarSection.Output);

        // Name => expr call args bind a VAR_OUTPUT's value back into the
        // caller-side lvalue after the call returns. BindParams and the FB
        // input binders read namedArgs only for inputs, so this is the one
        // place output binding happens - for METHOD, FUNCTION and FB-instance
        // calls alike, which is what keeps the assignment rules (SetLValue's
        // coercion) identical across all three. calleeCells is wherever the
        // callee keeps its outputs: a call frame's Locals, or an FB
        // instance's persisted Fields.
        private void WriteBackOutputArgs(
            IReadOnlyList<NamedArg> namedArgs,
            Func<string, bool> isOutput,
            IReadOnlyDictionary<string, Cell> calleeCells,
            Frame callerFrame)
        {
            foreach (var arg in namedArgs)
            {
                if (!arg.IsOutput || arg.IsUnboundOutput || !isOutput(arg.Name))
                    continue;

                if (calleeCells.TryGetValue(arg.Name, out var outCell))
                    SetLValue(arg.Value, outCell.Value, callerFrame);
            }
        }

        // Resolves a bare-invocation callee cell by name, checking the caller
        // frame's Locals (METHOD-local VARs) before the instance's persisted
        // Fields (top-level VARs), mirroring Frame.ResolveCell's precedence.
        // callerFrame is null for a few top-level entry points such as FB_init,
        // where only instance.Fields applies.
        //
        // That Locals-before-Fields precedence is valid ONLY for a genuine
        // bare/self invocation, i.e. when instance is the same FbInstance as
        // callerFrame.Instance - hence the guard. For an explicit non-self
        // receiver (someObj.Foo()) the caller's locals are irrelevant scope, and
        // consulting them would let a same-named local in the calling METHOD
        // hijack a call meant for the receiver.
        private static bool TryResolveCalleeCell(Frame callerFrame, FbInstance instance, string name, out Cell cell)
        {
            if (callerFrame != null && instance == callerFrame.Instance && callerFrame.Locals.TryGetValue(name, out cell))
                return true;

            // A global FUNCTION frame has no instance: its own locals were
            // already consulted above (null == null makes the self-invocation
            // test true), and there are no instance Fields behind them.
            if (instance == null)
            {
                cell = null;
                return false;
            }

            return instance.Fields.TryGetValue(name, out cell);
        }

        // The TON/TOF/TP VAR_INPUTs, in IEC declaration order.
        private static readonly string[] TimerPositionalParams = { "IN", "PT" };

        // R_TRIG/F_TRIG have a single VAR_INPUT.
        private static readonly string[] EdgeTriggerPositionalParams = { "CLK" };

        // Binds a bare invocation's arguments into a native stub's
        // already-seeded VAR_INPUT Cells: positionally against inputNames (the
        // callee's VAR_INPUTs in IEC declaration order), then by name. Unset
        // params deliberately keep whatever the instance already held, so a
        // caller that passes only CU relies on PV staying where it was.
        //
        // One helper for all the native families, since only the name list
        // differs - and for RS/SR and CTU/CTD/CTUD that list isn't even a
        // constant, so those call sites take it straight off the callee's own
        // host rather than re-spelling it here.
        private void BindNativeInputs(
            FbInstance callee,
            IReadOnlyList<string> inputNames,
            IReadOnlyList<Expr> positionalArgs,
            IReadOnlyList<NamedArg> namedArgs,
            Frame callerFrame)
        {
            for (var i = 0; i < positionalArgs.Count && i < inputNames.Count; i++)
                callee.Fields[inputNames[i]].Value = Evaluate(positionalArgs[i], callerFrame);

            // An => arg names an output, not an input: evaluating its target
            // into the field would overwrite state the host reads back, such
            // as a counter's CV, before the update ever ran.
            foreach (var arg in namedArgs)
                if (!arg.IsOutput && callee.Fields.TryGetValue(arg.Name, out var cell))
                    cell.Value = Evaluate(arg.Value, callerFrame);
        }

        // A native host carries no VAR section metadata for its fields, so any
        // field an => arg names is taken as the output. That is safe because
        // => on an input is a compile error in TwinCAT, never valid source.
        private void WriteBackNativeOutputArgs(FbInstance callee, IReadOnlyList<NamedArg> namedArgs, Frame callerFrame) =>
            WriteBackOutputArgs(namedArgs, _ => true, callee.Fields, callerFrame);

        // Declared params in the given sections for a bare-invoked interpreted
        // FB, in base-to-derived declaration order (matches IEC positional
        // arg order and the Fields-materialization loop in NewInstance).
        private List<VarDecl> GetOwnParamDecls(FbInstance instance, params VarSection[] sections)
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
                    .Where(d => sections.Contains(d.Section)));
            }
            return result;
        }

        // BindNativeInputs' interpreted counterpart: binds bare-invocation args
        // into the callee's persisted Fields by name or IEC positional order,
        // runs the callee's own top-level body once, then copies its outputs
        // to the call's => targets.
        private void InvokeFbInstance(
            FbInstance callee,
            IReadOnlyList<Expr> positionalArgs,
            IReadOnlyList<NamedArg> namedArgs,
            Frame callerFrame)
        {
            var inputDecls = GetOwnParamDecls(callee, VarSection.Input, VarSection.InOut);

            for (var i = 0; i < positionalArgs.Count && i < inputDecls.Count; i++)
                callee.Fields[inputDecls[i].Name].Value = Evaluate(positionalArgs[i], callerFrame);

            foreach (var arg in namedArgs)
                if (inputDecls.Any(d => d.Name == arg.Name) && callee.Fields.TryGetValue(arg.Name, out var cell))
                    cell.Value = Evaluate(arg.Value, callerFrame);

            var def = _registry.Get(callee.ActualTypeName);
            ResetTopLevelTempFields(callee);
            var calleeFrame = new Frame(callee, callee.ActualTypeName, null, def.BodyStartLine);
            ExecuteBody(() => _registry.GetStatements(def.ImplementationText), calleeFrame);

            var outputDecls = GetOwnParamDecls(callee, VarSection.Output);
            WriteBackOutputArgs(namedArgs, name => IsOutputDecl(outputDecls, name), callee.Fields, callerFrame);
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
                    // DefaultValue runs BEFORE ExecuteBody is entered for
                    // newFrame, so ExecuteBody's own attribution cannot cover a
                    // fault raised while constructing this callee's unsupplied-
                    // param default - a nested FB's default-value construction
                    // hitting an unresolved identifier, say. Unwrapped, such a
                    // fault surfaces at whatever frame is still active further up
                    // the CLR stack, typically the caller, with a call stack of
                    // one frame however deep the real failure was.
                    //
                    // Argument *evaluation* just above stays outside this
                    // wrapper on purpose: it runs in the CALLER's scope, so a
                    // fault there is legitimately the caller's and is already
                    // covered by the caller's own ExecuteBody.
                    value = RunWithFaultAttribution(
                        () => DefaultValue(decl, newFrame.Instance),
                        newFrame);
                }

                newFrame.Locals[decl.Name] = NewDeclaredCell(value, decl.TypeName, newFrame.Instance);
                newFrame.LocalTypeNames[decl.Name] = decl.TypeName;
            }
        }
    }
}
