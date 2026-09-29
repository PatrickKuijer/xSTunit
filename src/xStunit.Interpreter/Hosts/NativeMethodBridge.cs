using System;
using System.Collections.Generic;
using xStunit.Runner;
using xStunit.Runner.TcUnitStub;

namespace xStunit.Interpreter
{
    // Maps a call that reaches the TcUnit.FB_TestSuite native-stub boundary
    // onto SuiteHost. Grow-on-demand: only the surface the fixtures exercise.
    public static class NativeMethodBridge
    {
        // Whether Invoke implements methodName, which is a different question
        // from whether the receiver happens to be a suite. Engine gates on
        // this so a name the suite API does not implement still falls through
        // to the global-FUNCTION and native-function lookups that come after
        // it, instead of being claimed here and reported unsupported.
        //
        // MUST stay in agreement with Invoke's dispatch below; the two are
        // separate walks over the same set of names. Locked by
        // NativeMethodBridgeCanInvokeTests over every supported name.
        public static bool CanInvoke(string methodName)
        {
            if (methodName == null)
                return false;

            if (FixedNativeMethodName(methodName) != null)
                return true;

            if (HasPrefix(methodName, "AssertArrayEquals_"))
                return ArrayAssertSupportedTypes.Contains(methodName.Substring("AssertArrayEquals_".Length));

            if (HasPrefix(methodName, "AssertArray2dEquals_") || HasPrefix(methodName, "AssertArray3dEquals_"))
                return MultiDimArrayAssertSupportedTypes.Contains(methodName.Substring(methodName.IndexOf('_') + 1));

            if (HasPrefix(methodName, "AssertEquals_"))
                return ScalarAssertType.Registry.ContainsKey(methodName.Substring("AssertEquals_".Length));

            return false;
        }

        // The non-prefixed names Invoke's switch handles, each spelled the one
        // way its case label is.
        private static readonly string[] FixedNativeMethodNames =
        {
            "TEST", "TEST_ORDERED", "TEST_FINISHED", "TEST_FINISHED_NAMED", "IS_TEST_FINISHED",
            "AssertTrue", "AssertFalse", "AssertEquals",
        };

        // The suite API is a library FB's methods, so a call names one in any
        // case, as it would any other method. These map the spelling at the
        // call site onto the one Invoke dispatches on; null when it is none of
        // the fixed names.
        private static string FixedNativeMethodName(string methodName) =>
            Array.Find(FixedNativeMethodNames, name => string.Equals(name, methodName, StringComparison.OrdinalIgnoreCase));

        private static bool HasPrefix(string methodName, string prefix) =>
            methodName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        // Whether an unresolved call from a suite body is worth blaming on an
        // unwired external API (reported as unsupported-construct: an
        // interpreter gap, leave the POU alone) rather than on the PLC code
        // (reported as plc-fault: a real, fixable defect).
        //
        // Deliberately broader than CanInvoke. CanInvoke asks whether Invoke
        // implements the name - already answered "no" by the time this is
        // reached. This asks the softer question of whether the name looks
        // like it BELONGS to an external surface at all, and two qualify:
        //
        // 1. The upstream FB_TestSuite surface (mirrored by
        //    src/xStunit.Runner/TcUnitStub/FB_TestSuite.cs), which is entirely
        //    TEST*/IS_TEST*/Assert* by name. Upstream names not yet wired into
        //    CanInvoke/Invoke still match here, so they keep the "isn't
        //    supported yet" diagnostic instead of degrading to
        //    method-not-found.
        //
        // 2. IEC 61131-3 standard library functions (SEL, MUX, LIMIT, ...).
        //    There is no registry to check these against, only the convention
        //    that they are written in ALL CAPS - the same convention TcUnit's
        //    own TEST/IS_TEST_FINISHED follow. Suite-authored test-case and
        //    helper names are PascalCase throughout this codebase's fixtures,
        //    so the two populations do not overlap: an all-uppercase name is
        //    never one of the suite's own methods, and a misspelling of a
        //    PascalCase method ('CounterStartsAtZeroo') is never mistaken for
        //    a library call and correctly stays a plc-fault.
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

        // True for an identifier with at least one letter and no lowercase one
        // ("SEL", "F_TRIG", "MUX4"). Digits and underscores are allowed
        // anywhere and never decide the verdict on their own - "_1" is not an
        // IEC library name.
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
            SuiteHost host,
            string methodName,
            IReadOnlyList<object> positional,
            IReadOnlyDictionary<string, object> named,
            IReadOnlyDictionary<string, string> anyTypeNames = null)
        {
            switch (FixedNativeMethodName(methodName))
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
                        // AssertEquals(Expected: ANY, Actual: ANY, Message).
                        // The declared IEC type names have to arrive in
                        // anyTypeNames because only evaluated CLR values reach
                        // here, and several IEC scalar types share a CLR
                        // representation (see ScalarAssertType.cs) - they
                        // cannot be told apart from the values alone.
                        var args = ResolveArgs(ScalarAssertParamNames, positional, named);
                        string expectedTypeName = null;
                        string actualTypeName = null;
                        anyTypeNames?.TryGetValue("Expected", out expectedTypeName);
                        anyTypeNames?.TryGetValue("Actual", out actualTypeName);
                        host.AssertEqualsAnyCall(expectedTypeName, args["Expected"], actualTypeName, args["Actual"], (string)args["Message"]);
                        return null;
                    }
                default:
                    // ARRAY[*] equality, keyed off the AssertArrayEquals_<TYPE>
                    // suffix. Restricted to ArrayAssertSupportedTypes rather
                    // than the whole ScalarAssertType registry, since not
                    // every scalar type upstream has an array assert. REAL and
                    // LREAL take a Delta VAR_INPUT just as their scalar
                    // counterparts do, so HasDelta picks the param list.
                    if (HasPrefix(methodName, "AssertArrayEquals_"))
                    {
                        var typeName = methodName.Substring("AssertArrayEquals_".Length);
                        if (ArrayAssertSupportedTypes.Contains(typeName))
                        {
                            var scalarType = ScalarAssertType.Registry[typeName];
                            var paramNames = scalarType.HasDelta ? ArrayAssertWithDeltaParamNames : ArrayAssertParamNames;
                            var args = ResolveArgs(paramNames, positional, named);
                            var delta = scalarType.HasDelta ? args["Delta"] : null;
                            host.AssertArrayEqualsCall(scalarType.Name, (ArrayValue)args["Expecteds"], (ArrayValue)args["Actuals"], delta, (string)args["Message"]);
                            return null;
                        }
                    }

                    // AssertArray2dEquals_<TYPE>/AssertArray3dEquals_<TYPE>,
                    // REAL/LREAL only because upstream has no non-float 2D/3D
                    // array asserts. The dimension count sits inside the
                    // method name rather than in the type suffix, hence two
                    // prefixes instead of one - but both forward to the same
                    // host call as the 1D case, because
                    // AssertArrayEqualsCall/AssertArrayEquals are already
                    // dimension-agnostic (ArrayValue.Dimensions is a
                    // per-dimension list and the Runner unflattens indices
                    // generically). Adding a dimension is therefore only ever
                    // wider prefix matching here.
                    //
                    // Upstream's SIZE-mismatch message for 2D/3D spells out
                    // bounds ("SIZE = [lo..hi,lo..hi] (WxH)") where this
                    // reports a flat "SIZE = WxH"; kept flat for consistency
                    // with the 1D dispatcher rather than special-cased per
                    // dimension count.
                    if (HasPrefix(methodName, "AssertArray2dEquals_") || HasPrefix(methodName, "AssertArray3dEquals_"))
                    {
                        var typeName = methodName.Substring(methodName.IndexOf('_') + 1);
                        if (MultiDimArrayAssertSupportedTypes.Contains(typeName))
                        {
                            var scalarType = ScalarAssertType.Registry[typeName];
                            var paramNames = scalarType.HasDelta ? ArrayAssertWithDeltaParamNames : ArrayAssertParamNames;
                            var args = ResolveArgs(paramNames, positional, named);
                            var delta = scalarType.HasDelta ? args["Delta"] : null;
                            host.AssertArrayEqualsCall(scalarType.Name, (ArrayValue)args["Expecteds"], (ArrayValue)args["Actuals"], delta, (string)args["Message"]);
                            return null;
                        }
                    }

                    // Scalar dispatch, driven by the ScalarAssertType registry
                    // rather than a case per type: adding a scalar type means
                    // adding a registry entry, not editing this switch.
                    if (HasPrefix(methodName, "AssertEquals_"))
                    {
                        var typeName = methodName.Substring("AssertEquals_".Length);
                        if (ScalarAssertType.Registry.TryGetValue(typeName, out var scalarType))
                        {
                            var paramNames = scalarType.HasDelta ? ScalarAssertWithDeltaParamNames : ScalarAssertParamNames;
                            var args = ResolveArgs(paramNames, positional, named);
                            var delta = scalarType.HasDelta ? args["Delta"] : null;
                            host.AssertEqualsScalar(scalarType.Name, args["Expected"], args["Actual"], delta, (string)args["Message"]);
                            return null;
                        }
                    }
                    throw NotSupported(methodName);
            }
        }

        // The diagnostic for a TcUnit-suite call this bridge doesn't
        // implement. Shared with Engine.CallMethod, which raises it only after
        // the global-FUNCTION and native-function lookups have also failed -
        // by then "this TcUnit API isn't wired up yet" is more useful to the
        // reader than a generic method-not-found.
        //
        // UnsupportedConstructException so the reported failure carries
        // methodName as a field rather than only inside prose a consumer would
        // have to parse. Still a NotSupportedException, so existing catches
        // are unaffected.
        public static NotSupportedException NotSupported(string methodName) =>
            new UnsupportedConstructException(
                methodName,
                $"TcUnit native call '{methodName}' isn't supported yet (grow-on-demand).");

        private static readonly string[] ConditionAssertParamNames = { "Condition", "Message" };
        private static readonly string[] ScalarAssertParamNames = { "Expected", "Actual", "Message" };
        private static readonly string[] ScalarAssertWithDeltaParamNames = { "Expected", "Actual", "Delta", "Message" };
        private static readonly string[] ArrayAssertParamNames = { "Expecteds", "Actuals", "Message" };
        private static readonly string[] ArrayAssertWithDeltaParamNames = { "Expecteds", "Actuals", "Delta", "Message" };

        // Every scalar type in the ScalarAssertType registry except LWORD,
        // which upstream has an AssertArrayEquals_ overload for but this
        // bridge has not needed yet.
        private static readonly HashSet<string> ArrayAssertSupportedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BOOL", "BYTE", "DINT", "DWORD", "INT", "LINT", "LREAL", "REAL", "SINT", "UDINT", "UINT", "ULINT", "USINT", "WORD",
        };

        // Narrower than the 1D set above because upstream declares 2D/3D array
        // asserts for the float types only.
        private static readonly HashSet<string> MultiDimArrayAssertSupportedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LREAL", "REAL",
        };

        // TEST_ORDERED/TEST_FINISHED_NAMED/IS_TEST_FINISHED each take a single
        // upstream input called TestName, accepted either positionally or by
        // that name.
        private static string ResolveTestName(IReadOnlyList<object> positional, IReadOnlyDictionary<string, object> named)
        {
            if (named.TryGetValue("TestName", out var byName))
                return (string)byName;
            return (string)positional[0];
        }

        // Assert*/AssertEquals_* are native intrinsics like MEMCPY/MEMSET:
        // there are no parsed VAR declarations for the Engine's ordinary
        // parameter binding to reconcile named arguments against, so this
        // bridge has to do it. Each declared parameter is resolved by name
        // first, then filled from the positional arguments left to right -
        // without which a perfectly ordinary positional call like
        // AssertTrue(cond, 'msg') would find nothing under either name.
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
