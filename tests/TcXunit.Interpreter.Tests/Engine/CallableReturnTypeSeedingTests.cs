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
    }
}
