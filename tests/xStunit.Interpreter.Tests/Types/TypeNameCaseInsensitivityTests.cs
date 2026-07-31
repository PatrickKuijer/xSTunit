using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcXunit-fzm: IEC 61131-3 type names are case-insensitive, but the
    // interpreter's type-name lookup sites (IecNumericType.Types,
    // Engine.DefaultValue, StringTypeInfo, ArrayTypeInfo, TypeRegistry)
    // all compared ordinally - a lowercase or mixed-case spelling of an
    // elementary type silently took a different path than the uppercase
    // spelling. E.g. 'nGain : lreal;' fell through DefaultValue's
    // IecNumericType lookup to the plain-scalar '0' (int) fallback instead
    // of 0d, and a lowercase-spelled METHOD return type wasn't seeded by
    // Engine.SeedReturnCell (TcXunit-cq6), reopening the narrowing bug that
    // ticket fixed.
    public class TypeNameCaseInsensitivityTests
    {
        [Fact]
        public void NewInstance_LowercaseLrealField_DefaultsToDoubleZero()
        {
            var pou = new PouAst(
                "FB_Gain",
                null,
                "VAR\n\tnGain : lreal;\nEND_VAR",
                "",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_Gain");

            Assert.IsType<double>(instance.Fields["nGain"].Value);
            Assert.Equal(0d, instance.Fields["nGain"].Value);
        }

        [Fact]
        public void NewInstance_MixedCaseLrealField_DefaultsToDoubleZero()
        {
            var pou = new PouAst(
                "FB_Gain",
                null,
                "VAR\n\tnGain : LReal;\nEND_VAR",
                "",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_Gain");

            Assert.IsType<double>(instance.Fields["nGain"].Value);
            Assert.Equal(0d, instance.Fields["nGain"].Value);
        }

        // Mirrors CallableReturnTypeSeedingTests'
        // LrealMethod_SeededByRealLiteralThenAssignedLreal_ReturnsTheLreal,
        // but with a lowercase-spelled return type: without a case-insensitive
        // IecNumericType lookup in SeedReturnCell, the return cell is never
        // seeded, 'M_Read := 0.0;' creates it as a REAL (bare decimal literals
        // lex as REAL, TcXunit-5qs), and the later LREAL assignment is
        // rejected as an implicit narrowing.
        [Fact]
        public void LowercaseLrealMethodReturnType_SeedsReturnCellAndReturnsTheLreal()
        {
            var read = new MethodAst(
                "M_Read",
                "METHOD PRIVATE M_Read : lreal\nVAR\n\ttfValue : LREAL;\nEND_VAR",
                "M_Read := 0.0;\ntfValue := 1.5;\nM_Read := tfValue;");

            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    read,
                    new MethodAst("M_Run", "METHOD M_Run : LREAL", "M_Run := M_Read();"),
                });

            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Widget");

            var result = engine.CallMethod(instance, "M_Run", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(1.5d, result);
        }

        // Same shape as the lowercase case above, but with a mixed-case
        // spelling ("LReal") - the lowercase test alone leaves open whether
        // the fix is really a case-insensitive comparison or just an
        // all-lowercase special case.
        [Fact]
        public void MixedCaseLrealMethodReturnType_SeedsReturnCellAndReturnsTheLreal()
        {
            var read = new MethodAst(
                "M_Read",
                "METHOD PRIVATE M_Read : LReal\nVAR\n\ttfValue : LREAL;\nEND_VAR",
                "M_Read := 0.0;\ntfValue := 1.5;\nM_Read := tfValue;");

            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    read,
                    new MethodAst("M_Run", "METHOD M_Run : LREAL", "M_Run := M_Read();"),
                });

            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Widget");

            var result = engine.CallMethod(instance, "M_Run", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(1.5d, result);
        }
    }
}
