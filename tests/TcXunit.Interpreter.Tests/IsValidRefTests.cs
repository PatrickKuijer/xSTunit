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
    }
}
