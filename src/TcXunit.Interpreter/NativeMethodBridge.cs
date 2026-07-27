using System;
using System.Collections.Generic;
using TcXunit.Runner.TcUnitStub;

namespace TcXunit.Interpreter
{
    // Maps calls that fall through to the TcUnit.FB_TestSuite native-stub
    // boundary onto TcUnitSuiteHost. Grow-on-demand: only the surface the
    // FB_CounterTests fixture exercises (TcXunit-w5x.7/.8).
    public static class NativeMethodBridge
    {
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
                    throw new NotSupportedException(
                        $"TcUnit native call '{methodName}' isn't supported yet (grow-on-demand, TcXunit-w5x.12).");
            }
        }

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
