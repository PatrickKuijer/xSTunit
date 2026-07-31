using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A Cell carries no declared-type tag, so assignment compatibility is
    // inferred from the CLR type already sitting in it. A callable's return
    // cell is therefore seeded from the declared return type: left to be
    // created by its first assignment, 'M_Read := 0.0;' in an LREAL-returning
    // method would fix the cell as REAL (a bare decimal literal lexes as REAL),
    // and every later LREAL assignment into it would be rejected as implicit
    // narrowing - failing ST that TwinCAT compiles, and taking the suite's load
    // with it.
    public class CallableReturnTypeSeedingTests
    {
        // Every case here runs the method under test through an LREAL-returning
        // M_Run: the narrowing rule is only observable in the value that
        // reaches the caller.
        private static object RunM_Run(TypeRegistry registry, string typeName = "FB_Widget")
        {
            var engine = new Engine(registry);
            var instance = engine.NewInstance(typeName);
            return engine.CallMethod(instance, "M_Run", new Expr[0], new NamedArg[0], null, null);
        }

        private static object CallOn(PouAst fb) => RunM_Run(new TypeRegistry(new[] { fb }), fb.Name);

        private static PouAst WidgetWith(MethodAst method) =>
            new PouAst(
                "FB_Widget",
                null,
                "VAR\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    method,
                    new MethodAst("M_Run", "METHOD M_Run : LREAL", "M_Run := M_Read();"),
                });

        [Fact]
        public void LrealMethod_SeededByRealLiteralThenAssignedLreal_ReturnsTheLreal()
        {
            var read = new MethodAst(
                "M_Read",
                "METHOD PRIVATE M_Read : LREAL\nVAR\n\ttfValue : LREAL;\nEND_VAR",
                "M_Read := 0.0;\ntfValue := 1.5;\nM_Read := tfValue;");

            Assert.Equal(1.5d, CallOn(WidgetWith(read)));
        }

        [Fact]
        public void LrealMethod_SeededByRealLiteralThenAssignedIntToLreal_ReturnsTheLreal()
        {
            // INT_TO_LREAL yields a boxed double, so it meets the same
            // narrowing rule a plain LREAL variable does.
            var read = new MethodAst(
                "M_Read",
                "METHOD PRIVATE M_Read : LREAL\nVAR\n\tnValue : INT := 3;\nEND_VAR",
                "M_Read := 0.0;\nM_Read := INT_TO_LREAL(nValue);");

            Assert.Equal(3d, CallOn(WidgetWith(read)));
        }

        [Fact]
        public void LrealMethod_OnlyEverAssignedARealLiteral_WidensToLreal()
        {
            // The silent half of the same rule: an unseeded cell would leave a
            // boxed float behind, handing the caller REAL precision out of an
            // LREAL-returning method.
            var read = new MethodAst(
                "M_Read",
                "METHOD PRIVATE M_Read : LREAL",
                "M_Read := 1.0;");

            Assert.Equal(1d, CallOn(WidgetWith(read)));
        }

        [Fact]
        public void LrealMethod_NeverAssignsItsReturn_ReturnsZeroNotNull()
        {
            var read = new MethodAst("M_Read", "METHOD PRIVATE M_Read : LREAL", "");

            Assert.Equal(0d, CallOn(WidgetWith(read)));
        }

        [Fact]
        public void LrealMethod_ReturnTypeIsAnAliasDut_StillSeedsAsLreal()
        {
            var read = new MethodAst(
                "M_Read",
                "METHOD PRIVATE M_Read : T_Gain\nVAR\n\ttfValue : LREAL;\nEND_VAR",
                "M_Read := 0.0;\ntfValue := 2.5;\nM_Read := tfValue;");

            var registry = new TypeRegistry(
                new[] { WidgetWith(read) },
                aliases: new[] { new KeyValuePair<string, string>("T_Gain", "LREAL") });

            Assert.Equal(2.5d, RunM_Run(registry));
        }

        [Fact]
        public void IntMethod_AssignedARealLiteral_StillRejectsTheNarrowing()
        {
            // Seeding must not soften the narrowing rule it exists to feed.
            var read = new MethodAst(
                "M_Read",
                "METHOD PRIVATE M_Read : INT",
                "M_Read := 1.5;");

            var ex = Assert.Throws<System.InvalidOperationException>(() => CallOn(WidgetWith(read)));
            Assert.Contains("Implicit narrowing from REAL to INT", ex.Message);
        }

        [Fact]
        public void VoidMethod_NeverAssignsItsReturn_StillReturnsNull()
        {
            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tnTouched : INT;\nEND_VAR",
                "",
                new List<MethodAst> { new MethodAst("M_Do", "METHOD PRIVATE M_Do", "nTouched := 1;") });

            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Widget");

            Assert.Null(engine.CallMethod(instance, "M_Do", new Expr[0], new NamedArg[0], null, null));
            Assert.Equal(1, instance.Fields["nTouched"].Value);
        }

        [Fact]
        public void GlobalFunction_SeededByRealLiteralThenAssignedLreal_ReturnsTheLreal()
        {
            var function = new PouAst(
                "F_Read",
                null,
                "FUNCTION F_Read : LREAL\nVAR_INPUT\n\tfIn : LREAL;\nEND_VAR",
                "F_Read := 0.0;\nF_Read := fIn * 2.0;",
                new List<MethodAst>());

            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\nEND_VAR",
                "",
                new List<MethodAst> { new MethodAst("M_Run", "METHOD M_Run : LREAL", "M_Run := F_Read(1.25);") });

            Assert.Equal(2.5d, RunM_Run(new TypeRegistry(new[] { fb, function })));
        }

        [Fact]
        public void LrealMethod_InheritedFromABaseFb_SeedsFromTheDeclaringTypesHeader()
        {
            var baseFb = new PouAst(
                "FB_Base",
                null,
                "VAR\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    new MethodAst(
                        "M_Read",
                        "METHOD PUBLIC M_Read : LREAL\nVAR\n\ttfValue : LREAL;\nEND_VAR",
                        "M_Read := 0.0;\ntfValue := 4.25;\nM_Read := tfValue;"),
                });

            var derived = new PouAst(
                "FB_Derived",
                "FB_Base",
                "VAR\nEND_VAR",
                "",
                new List<MethodAst> { new MethodAst("M_Run", "METHOD M_Run : LREAL", "M_Run := M_Read();") });

            Assert.Equal(4.25d, RunM_Run(new TypeRegistry(new[] { derived, baseFb }), "FB_Derived"));
        }

        // The non-numeric elementary return types (BOOL/STRING/WSTRING(n)/TIME/
        // LTIME/DATE/DATE_AND_TIME/TIME_OF_DAY) carry no narrowing hazard, so
        // these call the method directly rather than through the LREAL wrapper:
        // all that is pinned is that the caller sees the IEC default, not null.
        private static object CallDirectly(PouAst fb, string methodName)
        {
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance(fb.Name);
            return engine.CallMethod(instance, methodName, new Expr[0], new NamedArg[0], null, null);
        }

        private static PouAst WidgetWithOnly(MethodAst method) =>
            new PouAst("FB_Widget", null, "VAR\nEND_VAR", "", new List<MethodAst> { method });

        [Fact]
        public void BoolMethod_UnassignedOnAnUntakenPath_ReturnsFalseNotNull()
        {
            var read = new MethodAst(
                "M_IsReady",
                "METHOD PUBLIC M_IsReady : BOOL",
                "IF FALSE THEN\n\tM_IsReady := TRUE;\nEND_IF");

            Assert.Equal(false, CallDirectly(WidgetWithOnly(read), "M_IsReady"));
        }

        [Fact]
        public void StringMethod_UnassignedOnAnUntakenPath_ReturnsEmptyStringNotNull()
        {
            var read = new MethodAst(
                "M_Name",
                "METHOD PUBLIC M_Name : STRING",
                "IF FALSE THEN\n\tM_Name := 'unreachable';\nEND_IF");

            Assert.Equal("", CallDirectly(WidgetWithOnly(read), "M_Name"));
        }

        [Fact]
        public void SizedStringMethod_NeverAssignsItsReturn_ReturnsEmptyStringNotNull()
        {
            var read = new MethodAst("M_Name", "METHOD PUBLIC M_Name : STRING(35)", "");

            Assert.Equal("", CallDirectly(WidgetWithOnly(read), "M_Name"));
        }

        [Fact]
        public void TimeMethod_NeverAssignsItsReturn_ReturnsZeroTimeNotNull()
        {
            var read = new MethodAst("M_Elapsed", "METHOD PUBLIC M_Elapsed : TIME", "");

            Assert.Equal(0u, CallDirectly(WidgetWithOnly(read), "M_Elapsed"));
        }

        [Fact]
        public void LtimeMethod_NeverAssignsItsReturn_ReturnsZeroLtimeNotNull()
        {
            var read = new MethodAst("M_Elapsed", "METHOD PUBLIC M_Elapsed : LTIME", "");

            Assert.Equal(0ul, CallDirectly(WidgetWithOnly(read), "M_Elapsed"));
        }

        [Fact]
        public void DateAndTimeMethod_NeverAssignsItsReturn_ReturnsZeroNotNull()
        {
            var read = new MethodAst("M_Stamp", "METHOD PUBLIC M_Stamp : DATE_AND_TIME", "");

            Assert.Equal(0u, CallDirectly(WidgetWithOnly(read), "M_Stamp"));
        }

        [Fact]
        public void PointerMethod_NeverAssignsItsReturn_StillReturnsNull()
        {
            // Seeding is deliberately limited to the elementary types: a
            // POINTER return gets no cell at all. Routing it through
            // DefaultValue would also start materializing DUT/FB instances for
            // every other type name, which is speculative construction the
            // interpreter declines to do.
            var read = new MethodAst("M_Ptr", "METHOD PUBLIC M_Ptr : POINTER TO INT", "");

            Assert.Null(CallDirectly(WidgetWithOnly(read), "M_Ptr"));
        }

        [Fact]
        public void FbMethod_NeverAssignsItsReturn_StillReturnsNull()
        {
            // The FB-typed half of the same limit: no instance is constructed
            // just because a method declares one as its return type.
            var timer = new PouAst("FB_Sub", null, "VAR\nEND_VAR", "", new List<MethodAst>());
            var widget = new PouAst(
                "FB_Widget",
                null,
                "VAR\nEND_VAR",
                "",
                new List<MethodAst> { new MethodAst("M_Get", "METHOD PUBLIC M_Get : FB_Sub", "") });

            var engine = new Engine(new TypeRegistry(new[] { widget, timer }));
            var instance = engine.NewInstance("FB_Widget");

            Assert.Null(engine.CallMethod(instance, "M_Get", new Expr[0], new NamedArg[0], null, null));
        }
    }
}
