using System;
using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-7gz: __ISVALIDREF(ref) intrinsic - TRUE when a REFERENCE TO
    // variable currently aliases a valid target, FALSE when unassigned.
    // Mirrors a common guard-clause pattern seen in real PLC code:
    // IF __ISVALIDREF(refToSomeFb) THEN ... that this unblocks.
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
            // Mirrors a typical FB guard clause: bail out early unless the
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
            // TcXunit-t6p: the common real-world pattern binds an instance-level
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

        // TcXunit-cnn: every test above binds REFERENCE TO INT, but the
        // motivating real-world use case cited by this file's own header
        // comment (IF __ISVALIDREF(refToSomeFb) THEN ...) is a
        // REFERENCE TO of an FB/STRUCT container, not a scalar. The two
        // tests below cover that shape directly: a REFERENCE TO of a
        // user-defined FB type and of a STRUCT type, bound to an actual
        // FB instance/struct field rather than a plain scalar, mirroring
        // that same real-world guard shape.
        private static (Engine Engine, FbInstance Instance, Frame Frame) NewFbTargetHolder()
        {
            var target = new PouAst(
                "FB_Machine",
                null,
                "VAR\n\tState : INT := 42;\nEND_VAR",
                "",
                new List<MethodAst>());
            var holder = new PouAst(
                "FB_Holder2",
                null,
                "VAR\n\tmachine : FB_Machine;\n\trefMachine : REFERENCE TO FB_Machine;\nEND_VAR",
                "",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { target, holder }));
            var instance = engine.NewInstance("FB_Holder2");
            return (engine, instance, new Frame(instance, "FB_Holder2"));
        }

        private static (Engine Engine, FbInstance Instance, Frame Frame) NewStructTargetHolder()
        {
            var payloadStruct = new StructAst(
                "ST_Payload",
                new List<VarDecl> { new VarDecl("Value", "INT", "7", VarSection.Local) });
            var holder = new PouAst(
                "FB_Holder3",
                null,
                "VAR\n\tpayload : ST_Payload;\n\trefPayload : REFERENCE TO ST_Payload;\nEND_VAR",
                "",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { holder }, new[] { payloadStruct }));
            var instance = engine.NewInstance("FB_Holder3");
            return (engine, instance, new Frame(instance, "FB_Holder3"));
        }

        [Fact]
        public void IsValidRef_UnassignedFbReference_ReturnsFalse()
        {
            // Real-world-shaped REFERENCE TO of a user FB type, never bound.
            var (engine, _, frame) = NewFbTargetHolder();

            var result = engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(refMachine)"), frame);

            Assert.Equal(false, result);
        }

        [Fact]
        public void IsValidRef_FbReferenceBoundViaRefAssign_ReturnsTrueAndAliasesFields()
        {
            // Mirrors the actual real-world guard shape: a REFERENCE TO an FB
            // type (not REFERENCE TO INT), bound to a real FB instance
            // field via REF=.
            var (engine, instance, frame) = NewFbTargetHolder();

            engine.ExecuteStatements(Parser.ParseStatements("refMachine REF= machine;"), frame);
            var validity = engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(refMachine)"), frame);

            Assert.Equal(true, validity);

            // FieldsOf/Cell aliasing: the reference's Cell was replaced
            // wholesale with the target's own Cell (TcXunit-t6p), so a
            // field write through refMachine.State must be visible via
            // machine.State - same Cell, not a copy - exactly as it is for
            // the REFERENCE TO INT case.
            engine.ExecuteStatements(Parser.ParseStatements("refMachine.State := 99;"), frame);
            var machineState = engine.Evaluate(Parser.ParseExpression("machine.State"), frame);

            Assert.Equal(99, machineState);
            Assert.Same(instance.Fields["machine"].Value, frame.ResolveCell("refMachine").Value);
        }

        [Fact]
        public void IsValidRef_UnassignedStructReference_ReturnsFalse()
        {
            // Same shape as the motivating real-world guard, but the target
            // is a STRUCT container rather than an FB instance.
            var (engine, _, frame) = NewStructTargetHolder();

            var result = engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(refPayload)"), frame);

            Assert.Equal(false, result);
        }

        [Fact]
        public void IsValidRef_StructReferenceBoundViaRefAssign_ReturnsTrueAndAliasesFields()
        {
            var (engine, _, frame) = NewStructTargetHolder();

            engine.ExecuteStatements(Parser.ParseStatements("refPayload REF= payload;"), frame);
            var validity = engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(refPayload)"), frame);

            Assert.Equal(true, validity);

            // Same FieldsOf/Cell aliasing guarantee, but for a STRUCT
            // target rather than an FB instance - StructInstance shares
            // the FieldsOf(receiver) path with FbInstance (Engine.Cells.cs).
            engine.ExecuteStatements(Parser.ParseStatements("refPayload.Value := 123;"), frame);
            var payloadValue = engine.Evaluate(Parser.ParseExpression("payload.Value"), frame);

            Assert.Equal(123, payloadValue);
        }

        // TcXunit-6t0: REF= previously only accepted a plain identifier on
        // the left (RequireIdentifierName in the parser); a struct/FB member
        // or array-index target threw FormatException. That's the ordinary
        // shape of a base FB wiring up its own REFERENCE TO/interface
        // members inside FB_init (stWidget.ipHandler REF= fbHandler etc.) -
        // the four tests below cover each newly-accepted target shape.
        private static (Engine Engine, FbInstance Instance, Frame Frame) NewMemberRefHolder()
        {
            var handlerFb = new PouAst(
                "FB_Handler",
                null,
                "VAR\n\tValue : INT := 42;\nEND_VAR",
                "",
                new List<MethodAst>());
            var innerFb = new PouAst(
                "FB_Inner",
                null,
                "VAR\n\tistWidget : REFERENCE TO ST_Widget;\nEND_VAR",
                "",
                new List<MethodAst>());
            var nestedStruct = new StructAst(
                "ST_Nested",
                new List<VarDecl> { new VarDecl("Value", "INT", "7", VarSection.Local) });
            var widgetStruct = new StructAst(
                "ST_Widget",
                new List<VarDecl>
                {
                    new VarDecl("IpHandler", "REFERENCE TO FB_Handler", null, VarSection.Local),
                    new VarDecl("StNested", "REFERENCE TO ST_Nested", null, VarSection.Local),
                });
            var holder = new PouAst(
                "FB_HolderBase",
                null,
                "VAR\n\tstWidget : ST_Widget;\n\tfbHandler : FB_Handler;\n\tfbInner : FB_Inner;\n\tistSource : ST_Nested;\nEND_VAR",
                "",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(
                new[] { handlerFb, innerFb, holder },
                new[] { nestedStruct, widgetStruct }));
            var instance = engine.NewInstance("FB_HolderBase");
            return (engine, instance, new Frame(instance, "FB_HolderBase"));
        }

        [Fact]
        public void RefAssign_StructMemberFieldTarget_AliasesFieldsAndReportsValidRef()
        {
            // stWidget.IpHandler REF= fbHandler - struct-member LHS, the
            // exact real-world FB_init shape this bug blocked.
            var (engine, _, frame) = NewMemberRefHolder();

            engine.ExecuteStatements(Parser.ParseStatements("stWidget.IpHandler REF= fbHandler;"), frame);

            var validity = engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(stWidget.IpHandler)"), frame);
            Assert.Equal(true, validity);

            // Aliasing, not a copy: a write through the bound member must be
            // visible via the original fbHandler field.
            engine.ExecuteStatements(Parser.ParseStatements("stWidget.IpHandler.Value := 99;"), frame);
            var handlerValue = engine.Evaluate(Parser.ParseExpression("fbHandler.Value"), frame);
            Assert.Equal(99, handlerValue);
        }

        [Fact]
        public void RefAssign_StructMemberToNestedStructTarget_AliasesFieldsAndReportsValidRef()
        {
            // stWidget.StNested REF= istSource - struct-member LHS bound to
            // another STRUCT instance rather than an FB instance.
            var (engine, _, frame) = NewMemberRefHolder();

            engine.ExecuteStatements(Parser.ParseStatements("stWidget.StNested REF= istSource;"), frame);

            var validity = engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(stWidget.StNested)"), frame);
            Assert.Equal(true, validity);

            engine.ExecuteStatements(Parser.ParseStatements("stWidget.StNested.Value := 55;"), frame);
            var sourceValue = engine.Evaluate(Parser.ParseExpression("istSource.Value"), frame);
            Assert.Equal(55, sourceValue);
        }

        [Fact]
        public void RefAssign_FbInstanceFieldTarget_AliasesToStructInstance()
        {
            // fbInner.istWidget REF= stWidget - an FB-instance-member LHS
            // (rather than the receiver being a plain identifier).
            var (engine, _, frame) = NewMemberRefHolder();

            engine.ExecuteStatements(Parser.ParseStatements("fbInner.istWidget REF= stWidget;"), frame);

            var validity = engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(fbInner.istWidget)"), frame);
            Assert.Equal(true, validity);

            // The alias must be the very same StructInstance as stWidget -
            // binding through it must be visible from stWidget directly.
            engine.ExecuteStatements(Parser.ParseStatements("fbInner.istWidget.IpHandler REF= fbHandler;"), frame);
            var throughAliasValidity = engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(stWidget.IpHandler)"), frame);
            Assert.Equal(true, throughAliasValidity);
        }

        [Fact]
        public void RefAssign_ArrayIndexTarget_Binds()
        {
            // aRefs[1] REF= x - array-index LHS. Element type is a plain FB
            // (not "ARRAY OF REFERENCE TO ...") because VarBlockParser's
            // ARRAY-type regex only supports a single-word element type
            // today - a pre-existing, separate gap from this ticket. FB
            // instances are still CLR reference types, so binding one into
            // an array slot and writing through it is a genuine aliasing
            // check, not just a value copy.
            var handlerFb = new PouAst(
                "FB_Handler",
                null,
                "VAR\n\tValue : INT := 42;\nEND_VAR",
                "",
                new List<MethodAst>());
            var pou = new PouAst(
                "FB_ArrayRefHolder",
                null,
                "VAR\n\taRefs : ARRAY[1..3] OF FB_Handler;\n\tx : FB_Handler;\nEND_VAR",
                "",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { handlerFb, pou }));
            var instance = engine.NewInstance("FB_ArrayRefHolder");
            var frame = new Frame(instance, "FB_ArrayRefHolder");

            engine.ExecuteStatements(Parser.ParseStatements("aRefs[1] REF= x;"), frame);
            engine.ExecuteStatements(Parser.ParseStatements("aRefs[1].Value := 77;"), frame);

            var xValue = engine.Evaluate(Parser.ParseExpression("x.Value"), frame);
            Assert.Equal(77, xValue);
        }
    }
}
