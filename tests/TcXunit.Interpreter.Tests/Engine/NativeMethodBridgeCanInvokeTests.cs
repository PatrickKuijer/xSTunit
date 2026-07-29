using System;
using System.Collections.Generic;
using System.Linq;
using TcXunit.Interpreter;
using TcXunit.Runner.TcUnitStub;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-6k2: locks the invariant NativeMethodBridge.CanInvoke's comment
    // promises - CanInvoke must recognize exactly the names Invoke dispatches.
    //
    // The two are separate code paths (a predicate over tables vs. a switch
    // plus prefix checks), so they can drift. Drift is silently harmful in one
    // direction: a name Invoke handles but CanInvoke rejects stops being routed
    // to the suite host and instead falls through to the global-FUNCTION and
    // native-function lookups, ending as a confusing "not found" for what is
    // really an implemented TcUnit assert. Rather than eyeballing the two
    // lists, this drives every name through Invoke and asserts that "Invoke
    // did not reject it as unknown" agrees with CanInvoke.
    public class NativeMethodBridgeCanInvokeTests
    {
        // Every name Invoke is known to dispatch, built the same table-driven
        // way Invoke itself derives them so a newly registered scalar/array
        // type is covered automatically rather than needing a new literal here.
        public static IEnumerable<object[]> DispatchedNames()
        {
            var fixedNames = new[]
            {
                "TEST", "TEST_ORDERED", "TEST_FINISHED", "TEST_FINISHED_NAMED", "IS_TEST_FINISHED",
                "AssertTrue", "AssertFalse", "AssertEquals",
            };

            foreach (var name in fixedNames)
                yield return new object[] { name };

            foreach (var typeName in ScalarAssertType.Registry.Keys)
                yield return new object[] { $"AssertEquals_{typeName}" };

            foreach (var typeName in new[] { "BOOL", "BYTE", "DINT", "INT", "REAL", "LREAL", "WORD" })
                yield return new object[] { $"AssertArrayEquals_{typeName}" };

            foreach (var typeName in new[] { "REAL", "LREAL" })
            {
                yield return new object[] { $"AssertArray2dEquals_{typeName}" };
                yield return new object[] { $"AssertArray3dEquals_{typeName}" };
            }
        }

        [Theory]
        [MemberData(nameof(DispatchedNames))]
        public void CanInvoke_AgreesWithInvoke_ForEveryDispatchedName(string methodName)
        {
            Assert.True(
                NativeMethodBridge.CanInvoke(methodName),
                $"Invoke dispatches '{methodName}' but CanInvoke rejects it - a suite calling it would fall " +
                "through to the global-FUNCTION/native-function lookups and fail with a misleading error.");

            // And confirm from the other side that Invoke really does claim it:
            // called with no arguments it will fault in some way, but never
            // with the specific "isn't supported yet" rejection.
            var host = new TcUnitSuiteHost();
            var ex = Record.Exception(() => NativeMethodBridge.Invoke(
                host, methodName, Array.Empty<object>(), new Dictionary<string, object>()));

            if (ex != null)
                Assert.DoesNotContain("isn't supported yet", ex.Message);
        }

        [Theory]
        [InlineData("F_CheckSum16")]
        [InlineData("F_Double")]
        [InlineData("AssertEquals_NOSUCHTYPE")]
        [InlineData("AssertArrayEquals_NOSUCHTYPE")]
        [InlineData("AssertArray2dEquals_INT")] // 2d/3d exist for REAL/LREAL only
        [InlineData("")]
        public void CanInvoke_RejectsNamesInvokeDoesNotImplement(string methodName)
        {
            Assert.False(NativeMethodBridge.CanInvoke(methodName));

            var ex = Assert.ThrowsAny<Exception>(() => NativeMethodBridge.Invoke(
                new TcUnitSuiteHost(), methodName, Array.Empty<object>(), new Dictionary<string, object>()));
            Assert.Contains("isn't supported yet", ex.Message);
        }

        [Fact]
        public void CanInvoke_NullNameIsRejectedRatherThanThrowing()
        {
            Assert.False(NativeMethodBridge.CanInvoke(null));
        }
    }
}
