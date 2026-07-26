using System;
using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-7gz: __ISVALIDREF(ref) intrinsic - TRUE when a REFERENCE TO
    // variable currently aliases a valid target, FALSE when unassigned.
    // Mirrors the framework guard-clause pattern (e.g. FB_SFC2's
    // IF __ISVALIDREF(istMachine) THEN) that this unblocks.
    public class IsValidRefTests
    {
        private static (Engine Engine, FbInstance Instance, Frame Frame) NewHolder(string varBlock)
        {
            var fb = new PouAst("FB_Holder", null, varBlock, "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            return (engine, instance, new Frame(instance, "FB_Holder"));
        }

        [Fact]
        public void IsValidRef_UnassignedReference_ReturnsFalse()
        {
            var (engine, _, frame) = NewHolder("VAR\n\trefInt : REFERENCE TO INT;\nEND_VAR");

            var result = engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(refInt)"), frame);

            Assert.Equal(false, result);
        }

        [Fact]
        public void IsValidRef_ReferenceBoundViaRefAssign_ReturnsTrue()
        {
            var (engine, _, frame) = NewHolder(
                "VAR\n\ttarget : INT := 5;\n\trefInt : REFERENCE TO INT;\nEND_VAR");

            engine.ExecuteStatements(Parser.ParseStatements("refInt REF= target;"), frame);
            var result = engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(refInt)"), frame);

            Assert.Equal(true, result);
        }

        [Fact]
        public void IsValidRef_ReferenceBoundToZeroValuedTarget_ReturnsTrue()
        {
            // Validity is about whether the reference is bound, not the pointed-to
            // value - a reference aliasing an INT that happens to be 0 is valid.
            var (engine, _, frame) = NewHolder(
                "VAR\n\ttarget : INT;\n\trefInt : REFERENCE TO INT;\nEND_VAR");

            engine.ExecuteStatements(Parser.ParseStatements("refInt REF= target;"), frame);
            var result = engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(refInt)"), frame);

            Assert.Equal(true, result);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        public void IsValidRef_GuardClause_SkipsBodyWhenReferenceInvalid(bool bindReference, bool expectedFlag)
        {
            // Mirrors FB_SFC2's guard: bail out early unless the machine
            // reference is valid, otherwise run the trailing work.
            var bind = bindReference ? "refInt REF= target;\n" : "";
            var body =
                bind +
                "IF NOT(__ISVALIDREF(refInt)) THEN\n" +
                "\tRETURN;\n" +
                "END_IF\n" +
                "flag := TRUE;";

            var method = new MethodAst(
                "DoWork",
                "METHOD DoWork\nVAR\n\ttarget : INT;\n\trefInt : REFERENCE TO INT;\nEND_VAR",
                body);
            var pou = new PouAst(
                "FB_Guard",
                null,
                "VAR\n\tflag : BOOL;\nEND_VAR",
                "",
                new List<MethodAst> { method });
            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_Guard");

            engine.CallMethod(instance, "DoWork", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(expectedFlag, instance.Fields["flag"].Value);
        }

        [Fact]
        public void IsValidRef_ReferenceBoundInOneCall_PersistsAcrossLaterCall()
        {
            // TcXunit-t6p: the FB_SFC2 pattern binds an instance-level
            // REFERENCE TO once (e.g. in a setter) and guards on it in a
            // later, separate method call - the binding must survive past
            // the Frame that performed the REF=, not just within it.
            var bindMethod = new MethodAst("Bind", "METHOD Bind", "refInt REF= target;");
            var checkMethod = new MethodAst(
                "Check",
                "METHOD Check : BOOL",
                "Check := __ISVALIDREF(refInt);");
            var pou = new PouAst(
                "FB_Guard",
                null,
                "VAR\n\ttarget : INT := 5;\n\trefInt : REFERENCE TO INT;\nEND_VAR",
                "",
                new List<MethodAst> { bindMethod, checkMethod });
            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_Guard");

            engine.CallMethod(instance, "Bind", new Expr[0], new NamedArg[0], null, null);
            var result = engine.CallMethod(instance, "Check", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(true, result);
        }

        [Fact]
        public void IsValidRef_PlainIntVariable_Throws()
        {
            // TcXunit-6lh: __ISVALIDREF is only meaningful on POINTER TO/
            // REFERENCE TO variables. A plain INT always has a non-null
            // DefaultValue (0), so without a declared-type check this would
            // silently evaluate to TRUE instead of surfacing the misuse -
            // exactly the kind of copy-paste/typo mistake in test ST code
            // this framework exists to catch.
            var (engine, _, frame) = NewHolder("VAR\n\tplainInt : INT;\nEND_VAR");

            var ex = Assert.Throws<InvalidOperationException>(
                () => engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(plainInt)"), frame));

            Assert.Contains("__ISVALIDREF", ex.Message);
            Assert.Contains("POINTER TO", ex.Message);
            Assert.Contains("REFERENCE TO", ex.Message);
        }

        [Fact]
        public void IsValidRef_PlainFbInstanceField_Throws()
        {
            // Same misuse, but on an FB instance field rather than a scalar -
            // e.g. __ISVALIDREF(machine) where machine should have been
            // declared REFERENCE TO/POINTER TO but is a plain FB instance
            // (using the native TON type as a stand-in FB instance field).
            var (engine, _, frame) = NewHolder("VAR\n\tmachine : TON;\nEND_VAR");

            var ex = Assert.Throws<InvalidOperationException>(
                () => engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(machine)"), frame));

            Assert.Contains("__ISVALIDREF", ex.Message);
        }
    }
}
