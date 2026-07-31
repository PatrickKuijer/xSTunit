using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-cq6: a Cell carries no declared-type tag, so assignment
    // compatibility is inferred from the CLR type already sitting in it
    // (NumericCoercion.CoerceForAssignment). CallMethod used to let a
    // callable's return cell be created lazily by whatever its first
    // assignment happened to be, and a bare decimal literal lexes as
    // REAL/float - so 'M_Read := 0.0;' at the top of an LREAL-returning
    // method made the return cell a REAL, and every later LREAL assignment
    // into it threw "Implicit narrowing from LREAL to REAL". Valid ST that
    // TwinCAT compiles was rejected, taking the whole suite's load with it.
    // Covers seeding the return cell from the declared return type instead.
    public class CallableReturnTypeSeedingTests
    {
        // Every case here runs the method under test through an LREAL-returning
        // M_Run, so the value the caller actually receives is what's asserted -
        // the bug was only ever observable at the call boundary.
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
            // INT_TO_LREAL yields a double, so it hit the same narrowing
            // rejection as a plain LREAL variable did.
            var read = new MethodAst(
                "M_Read",
                "METHOD PRIVATE M_Read : LREAL\nVAR\n\tnValue : INT := 3;\nEND_VAR",
                "M_Read := 0.0;\nM_Read := INT_TO_LREAL(nValue);");

            Assert.Equal(3d, CallOn(WidgetWith(read)));
        }

        [Fact]
        public void LrealMethod_OnlyEverAssignedARealLiteral_WidensToLreal()
        {
            // The seeded LREAL cell also fixes the silent half of the bug:
            // 'M_Read := 1.0;' used to leave a boxed float behind, so the
            // caller got REAL precision out of an LREAL-returning method.
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
            // Seeding must not soften the narrowing rule it exists to feed:
            // an INT-returning method assigned a REAL literal is still an
            // error, exactly as it was before.
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

        // TcXunit-qft: SeedReturnCell extended beyond IEC numerics to the
        // remaining elementary return types (BOOL/STRING/W?STRING(n)/TIME/
        // LTIME/DATE/DATE_AND_TIME/TIME_OF_DAY), which have a plain constant
        // default in Engine.DefaultValue but none of the narrowing hazard
        // TcXunit-cq6 was fixing. Unlike the LREAL cases above (which go
        // through an LREAL-returning M_Run wrapper to observe the narrowing
        // rule), these call the method directly - there is no coercion rule
        // to exercise here, only "does the caller see the IEC default instead
        // of null".
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
            // Out of scope for TcXunit-qft, verified rather than assumed:
            // a POINTER-returning method still gets no seeded cell at all,
            // same as before this ticket - DefaultValue's null for POINTER
            // TO is not the same thing as "seeded", and SeedReturnCell must
            // not call DefaultValue for this case (that path also
            // materializes DUT/FB instances for other type names, which is
            // exactly the speculative work this ticket declines to do).
            var read = new MethodAst("M_Ptr", "METHOD PUBLIC M_Ptr : POINTER TO INT", "");

            Assert.Null(CallDirectly(WidgetWithOnly(read), "M_Ptr"));
        }

        [Fact]
        public void FbMethod_NeverAssignsItsReturn_StillReturnsNull()
        {
            // Same "still genuinely unseeded" check as the POINTER case
            // above, for an FB-typed return - DefaultValue *would*
            // materialize a full FB_Timer-like instance for this type name
            // if SeedReturnCell called it, which is the speculative
            // construction this ticket explicitly declines to do.
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
