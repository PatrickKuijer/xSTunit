using System;
using System.Collections.Generic;
using xStunit.Runner;
using xStunit.Runner.TcUnitStub;

namespace xStunit.Interpreter
{
    // Maps calls that fall through to the TcUnit.FB_TestSuite native-stub
    // boundary onto TcUnitSuiteHost. Grow-on-demand: only the surface the
    // FB_CounterTests fixture exercises (TcXunit-w5x.7/.8).
    public static class NativeMethodBridge
    {
        // Whether Invoke recognizes methodName, i.e. whether the TcUnit
        // native-stub boundary is the right place to send this call
        // (TcXunit-6k2).
        //
        // Exists because "the receiver is a suite" and "this call is a suite
        // API call" are different questions, and Engine used to conflate them:
        // a suite instance reaching an unresolved call sent it here
        // unconditionally, so the throw at the bottom of Invoke claimed every
        // name a suite ever failed to resolve - including global FUNCTION POUs
        // and native library functions, which are looked up *after* this in
        // Engine.CallMethod and so were unreachable from inside a suite. (The
        // pre-existing TcXunit-9su global-function tests all called from a
        // plain FUNCTION_BLOCK, whose NativeSuiteHost is null, which is why
        // nothing caught it.) Gating on this keeps the suite API's precedence
        // exactly as it was for names it actually implements, while letting
        // everything else fall through to the later lookups.
        //
        // MUST stay in agreement with Invoke's dispatch below. Locked by
        // NativeMethodBridgeCanInvokeTests, which asserts the two agree over
        // every supported name.
        public static bool CanInvoke(string methodName)
        {
            if (methodName == null)
                return false;

            if (FixedNativeMethodNames.Contains(methodName))
                return true;

            if (methodName.StartsWith("AssertArrayEquals_", StringComparison.Ordinal))
                return ArrayAssertSupportedTypes.Contains(methodName.Substring("AssertArrayEquals_".Length));

            if (methodName.StartsWith("AssertArray2dEquals_", StringComparison.Ordinal) ||
                methodName.StartsWith("AssertArray3dEquals_", StringComparison.Ordinal))
                return MultiDimArrayAssertSupportedTypes.Contains(methodName.Substring(methodName.IndexOf('_') + 1));

            if (methodName.StartsWith("AssertEquals_", StringComparison.Ordinal))
                return ScalarAssertType.Registry.ContainsKey(methodName.Substring("AssertEquals_".Length));

            return false;
        }

        // The non-prefixed names Invoke's switch handles by exact match.
        private static readonly HashSet<string> FixedNativeMethodNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "TEST", "TEST_ORDERED", "TEST_FINISHED", "TEST_FINISHED_NAMED", "IS_TEST_FINISHED",
            "AssertTrue", "AssertFalse", "AssertEquals",
        };

        // TcXunit-2o9.1: whether an unresolved unqualified call from a suite
        // body is worth claiming as "an unwired external API" (NotSupported,
        // classifies as unsupported-construct - STOP, don't touch the POU)
        // versus letting it fall through to the ordinary method-not-found
        // error (classifies as plc-fault - a real defect, fixable).
        //
        // Deliberately broader than CanInvoke: CanInvoke asks "does Invoke
        // actually implement this name", which by the time Engine.CallMethod
        // reaches its suite-receiver last resort has already been answered
        // "no" (the CanInvoke gate above would have dispatched it otherwise).
        // This asks the softer question "does this name look like it BELONGS
        // to some grow-on-demand external surface at all" - i.e. would a
        // human reading it assume it's a TcUnit assert/API or an IEC
        // standard-library function, rather than a typo of one of the
        // suite's own test-case or helper methods.
        //
        // Two surfaces qualify:
        //
        // 1. The upstream FB_TestSuite surface (see TcUnit's
        //    FB_TestSuite.TcPOU, mirrored by
        //    src/xStunit.Runner/TcUnitStub/FB_TestSuite.cs) - entirely
        //    TEST*/IS_TEST*/Assert* by name. Grow-on-demand names not yet
        //    wired into CanInvoke/Invoke (e.g. AssertArrayEquals_LWORD - see
        //    ArrayAssertSupportedTypes' comment) still match here, so they
        //    keep the "isn't supported yet" diagnostic rather than degrading
        //    to method-not-found.
        //
        // 2. IEC 61131-3 standard library functions (SEL, MUX, LIMIT, ... -
        //    the SEL repro pinned by CliRunnerFailureKindTests/
        //    CliRunnerTestBlastRadiusTests, TcXunit-w5x.12), which this
        //    interpreter has no per-function registry for (unlike the
        //    TcUnit surface, there's no CanInvoke-style table to check
        //    against) but which are conventionally written in ALL CAPS -
        //    same convention TcUnit's own TEST/IS_TEST_FINISHED intrinsics
        //    follow. Every suite-authored test-case/helper method name in
        //    this codebase's own fixtures is PascalCase (CounterStartsAtZero,
        //    UsesGlobalFunction, Passes, ...), so an all-uppercase unresolved
        //    name is never mistaken for one of those, and a mixed-case one
        //    (e.g. a misspelled 'CounterStartsAtZeroo' for
        //    'CounterStartsAtZero') is never mistaken for a standard-library
        //    call - it falls through to the ordinary method-not-found
        //    plc-fault instead.
        public static bool LooksLikeTcUnitApiName(string methodName)
        {
            if (string.IsNullOrEmpty(methodName))
                return false;

            if (CanInvoke(methodName))
                return true;

            if (methodName.StartsWith("Assert", StringComparison.Ordinal) ||
                methodName.StartsWith("TEST", StringComparison.Ordinal) ||
                methodName.StartsWith("IS_TEST", StringComparison.Ordinal))
                return true;

            return IsAllUppercaseIdentifier(methodName);
        }

        // True for an identifier with at least one letter and no lowercase
        // letters (e.g. "SEL", "F_TRIG", "MUX4") - the IEC standard-library
        // naming convention LooksLikeTcUnitApiName's case 2 above matches
        // against. Digits/underscores are allowed anywhere and don't affect
        // the verdict either way.
        private static bool IsAllUppercaseIdentifier(string name)
        {
            var sawLetter = false;
            foreach (var c in name)
            {
                if (char.IsLower(c))
                    return false;
                if (char.IsUpper(c))
                    sawLetter = true;
            }
            return sawLetter;
        }

        public static object Invoke(
            TcUnitSuiteHost host,
            string methodName,
            IReadOnlyList<object> positional,
            IReadOnlyDictionary<string, object> named,
            IReadOnlyDictionary<string, string> anyTypeNames = null)
        {
            switch (methodName)
            {
                case "TEST":
                    host.Test((string)positional[0]);
                    return null;
                case "TEST_ORDERED":
                    return host.TestOrdered(ResolveTestName(positional, named));
                case "TEST_FINISHED":
                    host.TestFinished();
                    return null;
                case "TEST_FINISHED_NAMED":
                    host.TestFinishedNamed(ResolveTestName(positional, named));
                    return null;
                case "IS_TEST_FINISHED":
                    return host.IsTestFinished(ResolveTestName(positional, named));
                case "AssertTrue":
                    {
                        var args = ResolveArgs(ConditionAssertParamNames, positional, named);
                        host.AssertTrueCall((bool)args["Condition"], (string)args["Message"]);
                        return null;
                    }
                case "AssertFalse":
                    {
                        var args = ResolveArgs(ConditionAssertParamNames, positional, named);
                        host.AssertFalseCall((bool)args["Condition"], (string)args["Message"]);
                        return null;
                    }
                case "AssertEquals":
                    {
                        // TcXunit-gd2.5: type-erased AssertEquals(Expected:
                        // ANY, Actual: ANY, Message) dispatcher. Expected/
                        // Actual's declared IEC type names are resolved by
                        // Engine.Invocation.cs (from the argument
                        // expression, before evaluation) and passed in via
                        // anyTypeNames, since NativeMethodBridge only ever
                        // sees the already-evaluated CLR values here -
                        // which, for several IEC scalar types, are
                        // ambiguous/shared CLR representations (see
                        // ScalarAssertType.cs) and can't be told apart on
                        // their own.
                        var args = ResolveArgs(ScalarAssertParamNames, positional, named);
                        string expectedTypeName = null;
                        string actualTypeName = null;
                        anyTypeNames?.TryGetValue("Expected", out expectedTypeName);
                        anyTypeNames?.TryGetValue("Actual", out actualTypeName);
                        host.AssertEqualsAnyCall(expectedTypeName, args["Expected"], actualTypeName, args["Actual"], (string)args["Message"]);
                        return null;
                    }
                default:
                    // TcXunit-gd2.6: table-driven ARRAY[*] equality dispatch,
                    // same shape as the scalar dispatch below but keyed off
                    // the AssertArrayEquals_<TYPE> suffix - checked first
                    // since it's the more specific prefix ("AssertEquals_"
                    // doesn't match an "AssertArrayEquals_..." name, but
                    // checking array first keeps the two dispatches visually
                    // paired). Restricted to ArrayAssertSupportedTypes rather
                    // than the full ScalarAssertType registry (grow-on-demand:
                    // LWORD is the one remaining upstream array assert not
                    // wired up yet). REAL/LREAL (TcXunit-gd2.7) take a Delta
                    // VAR_INPUT, same as their scalar AssertEquals_REAL/
                    // _LREAL counterparts - HasDelta picks the right param
                    // list, mirroring the scalar dispatch below.
                    if (methodName.StartsWith("AssertArrayEquals_", StringComparison.Ordinal))
                    {
                        var typeName = methodName.Substring("AssertArrayEquals_".Length);
                        if (ArrayAssertSupportedTypes.Contains(typeName))
                        {
                            var scalarType = ScalarAssertType.Registry[typeName];
                            var paramNames = scalarType.HasDelta ? ArrayAssertWithDeltaParamNames : ArrayAssertParamNames;
                            var args = ResolveArgs(paramNames, positional, named);
                            var delta = scalarType.HasDelta ? args["Delta"] : null;
                            host.AssertArrayEqualsCall(typeName, (ArrayValue)args["Expecteds"], (ArrayValue)args["Actuals"], delta, (string)args["Message"]);
                            return null;
                        }
                    }

                    // TcXunit-gd2.10: AssertArray2dEquals_<TYPE>/
                    // AssertArray3dEquals_<TYPE> (REAL/LREAL only, matching
                    // upstream - it has no non-float 2D/3D array asserts).
                    // Unlike AssertArrayEquals_<TYPE> above, the dimension
                    // count is baked into the method name itself ("2d"/"3d"
                    // between "Array" and "Equals", not just a type suffix),
                    // so the prefix is "AssertArray2dEquals_"/
                    // "AssertArray3dEquals_" rather than a single shared
                    // prefix - but both forward to the exact same host call
                    // as the 1D case. AssertArrayEqualsCall/AssertArrayEquals
                    // are already dimension-agnostic (ArrayValue.Dimensions
                    // is a per-dimension list, and the Runner's shape-check/
                    // UnflattenIndex loop over Count generically), confirmed
                    // by reading ArrayValue.cs/ArrayTypeInfo.cs and upstream's
                    // FB_TestSuite.TcPOU AssertArray2dEquals_REAL/
                    // AssertArray3dEquals_REAL: per-element mismatches format
                    // as "ARRAY[i,j]"/"ARRAY[i,j,k]" - identical in shape to
                    // this dispatcher's existing
                    // $"ARRAY[{string.Join(",", index)}]" - so no new
                    // dispatcher logic needed, just wider prefix matching.
                    // (Upstream's SIZE-mismatch message for 2D/3D is more
                    // verbose - "SIZE = [lo..hi,lo..hi] (WxH)" with bounds -
                    // than this dispatcher's flat "SIZE = WxH"; kept as-is
                    // for consistency with the existing 1D dispatcher rather
                    // than special-cased per dimension count.)
                    if (methodName.StartsWith("AssertArray2dEquals_", StringComparison.Ordinal) ||
                        methodName.StartsWith("AssertArray3dEquals_", StringComparison.Ordinal))
                    {
                        var typeName = methodName.Substring(methodName.IndexOf('_') + 1);
                        if (MultiDimArrayAssertSupportedTypes.Contains(typeName))
                        {
                            var scalarType = ScalarAssertType.Registry[typeName];
                            var paramNames = scalarType.HasDelta ? ArrayAssertWithDeltaParamNames : ArrayAssertParamNames;
                            var args = ResolveArgs(paramNames, positional, named);
                            var delta = scalarType.HasDelta ? args["Delta"] : null;
                            host.AssertArrayEqualsCall(typeName, (ArrayValue)args["Expecteds"], (ArrayValue)args["Actuals"], delta, (string)args["Message"]);
                            return null;
                        }
                    }

                    // TcXunit-gd2.11: table-driven scalar dispatch. Parses
                    // the AssertEquals_<TYPE> suffix from the native call
                    // name, looks up the ScalarAssertType registry to know
                    // whether a Delta arg is expected, and calls the one
                    // generic host method instead of switching per type -
                    // adding a new scalar type means adding a registry entry,
                    // not a new case here.
                    if (methodName.StartsWith("AssertEquals_", StringComparison.Ordinal))
                    {
                        var typeName = methodName.Substring("AssertEquals_".Length);
                        if (ScalarAssertType.Registry.TryGetValue(typeName, out var scalarType))
                        {
                            var paramNames = scalarType.HasDelta ? ScalarAssertWithDeltaParamNames : ScalarAssertParamNames;
                            var args = ResolveArgs(paramNames, positional, named);
                            var delta = scalarType.HasDelta ? args["Delta"] : null;
                            host.AssertEqualsScalar(typeName, args["Expected"], args["Actual"], delta, (string)args["Message"]);
                            return null;
                        }
                    }
                    throw NotSupported(methodName);
            }
        }

        // The grow-on-demand diagnostic for a TcUnit-suite call this bridge
        // doesn't implement. Shared with Engine.CallMethod (TcXunit-6k2): since
        // the CanInvoke gate now lets unrecognized names fall through to the
        // global-FUNCTION/native-function lookups, a suite that exhausts those
        // too must still be told "this TcUnit API isn't wired up yet" rather
        // than the generic method-not-found error - the receiver being a suite
        // is what makes that the more useful of the two messages.
        //
        // TcXunit-3tx.1: an UnsupportedConstructException (still a
        // NotSupportedException, so every existing catch/assert is unaffected)
        // so the reported failure names the construct - methodName - as a field
        // rather than only inside prose a consumer would have to regex.
        public static NotSupportedException NotSupported(string methodName) =>
            new UnsupportedConstructException(
                methodName,
                $"TcUnit native call '{methodName}' isn't supported yet (grow-on-demand, TcXunit-w5x.12).");

        private static readonly string[] ConditionAssertParamNames = { "Condition", "Message" };
        private static readonly string[] ScalarAssertParamNames = { "Expected", "Actual", "Message" };
        private static readonly string[] ScalarAssertWithDeltaParamNames = { "Expected", "Actual", "Delta", "Message" };
        private static readonly string[] ArrayAssertParamNames = { "Expecteds", "Actuals", "Message" };
        private static readonly string[] ArrayAssertWithDeltaParamNames = { "Expecteds", "Actuals", "Delta", "Message" };

        // The upstream AssertArrayEquals_<TYPE> overloads this dispatcher
        // backs: the 12 non-float types from TcXunit-gd2.6, plus REAL/LREAL
        // from TcXunit-gd2.7 - every scalar type in the registry except
        // LWORD (upstream has AssertArrayEquals_LWORD too, but it's not part
        // of this ticket's scope - grow-on-demand).
        private static readonly HashSet<string> ArrayAssertSupportedTypes = new HashSet<string>
        {
            "BOOL", "BYTE", "DINT", "DWORD", "INT", "LINT", "LREAL", "REAL", "SINT", "UDINT", "UINT", "ULINT", "USINT", "WORD",
        };

        // TcXunit-gd2.10: upstream only has AssertArray2dEquals_<TYPE>/
        // AssertArray3dEquals_<TYPE> for REAL/LREAL (no non-float 2D/3D
        // array asserts exist upstream), unlike the 1D array asserts above.
        private static readonly HashSet<string> MultiDimArrayAssertSupportedTypes = new HashSet<string>
        {
            "LREAL", "REAL",
        };

        // TEST_ORDERED/TEST_FINISHED_NAMED/IS_TEST_FINISHED take a single
        // TestName input in upstream - accept it either positionally or by
        // that name (TcXunit-k28.7).
        private static string ResolveTestName(IReadOnlyList<object> positional, IReadOnlyDictionary<string, object> named)
        {
            if (named.TryGetValue("TestName", out var byName))
                return (string)byName;
            return (string)positional[0];
        }

        // TcXunit-bda: Assert*/AssertEquals_* calls are native intrinsics
        // like MEMCPY/MEMSET (no VarBlockParser decls for BindParams to
        // reconcile named args against - see Engine.ResolveIntrinsicArgs),
        // so real ST callers that pass args positionally (e.g.
        // AssertTrue(cond, 'msg')) hit named[...] directly and throw
        // KeyNotFoundException. Resolve each declared param by name first,
        // falling back to positional args in left-to-right order for
        // params not given by name (mirrors BindParams' shared posIndex).
        private static IReadOnlyDictionary<string, object> ResolveArgs(
            IReadOnlyList<string> paramNamesInDeclOrder,
            IReadOnlyList<object> positional,
            IReadOnlyDictionary<string, object> named)
        {
            var resolved = new Dictionary<string, object>();
            var posIndex = 0;
            foreach (var paramName in paramNamesInDeclOrder)
            {
                if (ArgBinder.TryResolveArg(
                    paramName,
                    name => named.TryGetValue(name, out var v) ? v : null,
                    positional,
                    ref posIndex,
                    out var value))
                    resolved[paramName] = value;
            }
            return resolved;
        }
    }
}
