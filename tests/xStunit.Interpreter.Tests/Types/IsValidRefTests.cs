using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    public class IsValidRefTests
    {
        private static (Engine Engine, FbInstance Instance, Frame Frame) NewHolder(
            string varBlock,
            IEnumerable<KeyValuePair<string, string>> aliases = null)
        {
            var fb = new PouAst("FB_Holder", null, varBlock, "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }, aliases: aliases));
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
            // Validity is about whether the reference is bound, not about the
            // value it aliases: an INT target that happens to be 0 is valid.
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
            // Validity must be decided from the declared type, not from
            // whether the value is null: a plain INT is never null (it
            // defaults to 0), so a value-based check would answer TRUE and
            // hide the misuse instead of reporting it.
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
            // Same misuse on a field that holds a real object rather than a
            // scalar; TON stands in as a readily available FB type.
            var (engine, _, frame) = NewHolder("VAR\n\tmachine : TON;\nEND_VAR");

            var ex = Assert.Throws<InvalidOperationException>(
                () => engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(machine)"), frame));

            Assert.Contains("__ISVALIDREF", ex.Message);
        }

        // An ALIAS DUT names an address type without spelling POINTER TO or
        // REFERENCE TO at the declaration, and __ISVALIDREF must answer for
        // such a variable exactly as it does for the direct spelling. Anything
        // else splits one declaration three ways: SIZEOF sizes it as a
        // pointer, its default is the null that makes the validity test
        // meaningful, and __ISVALIDREF alone refuses to look at it.
        [Fact]
        public void IsValidRef_UnboundAliasDeclaredPointer_ReturnsFalse()
        {
            var (engine, _, frame) = NewHolder(
                "VAR\n\tpData : PT_Byte;\nEND_VAR",
                new[] { new KeyValuePair<string, string>("PT_Byte", "POINTER TO BYTE") });

            var result = engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(pData)"), frame);

            Assert.Equal(false, result);
        }

        [Fact]
        public void IsValidRef_AliasDeclaredPointerAssignedAnAddress_ReturnsTrue()
        {
            var (engine, _, frame) = NewHolder(
                "VAR\n\ttarget : BYTE := 7;\n\tpData : PT_Byte;\nEND_VAR",
                new[] { new KeyValuePair<string, string>("PT_Byte", "POINTER TO BYTE") });

            engine.ExecuteStatements(Parser.ParseStatements("pData := ADR(target);"), frame);
            var result = engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(pData)"), frame);

            Assert.Equal(true, result);
        }

        [Fact]
        public void IsValidRef_AliasDeclaredReferenceBoundViaRefAssign_ReturnsFalseThenTrue()
        {
            var (engine, _, frame) = NewHolder(
                "VAR\n\ttarget : INT := 5;\n\trefInt : T_IntRef;\nEND_VAR",
                new[] { new KeyValuePair<string, string>("T_IntRef", "REFERENCE TO INT") });

            Assert.Equal(false, engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(refInt)"), frame));

            engine.ExecuteStatements(Parser.ParseStatements("refInt REF= target;"), frame);

            Assert.Equal(true, engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(refInt)"), frame));
        }

        [Fact]
        public void IsValidRef_AliasChainEndingInAnAddressType_ReturnsFalse()
        {
            // Alias-of-alias resolves the whole way, so an indirection added
            // between the declaration and the address type cannot smuggle a
            // pointer past the check.
            var (engine, _, frame) = NewHolder(
                "VAR\n\tpData : PT_Outer;\nEND_VAR",
                new[]
                {
                    new KeyValuePair<string, string>("PT_Outer", "PT_Inner"),
                    new KeyValuePair<string, string>("PT_Inner", "POINTER TO BYTE"),
                });

            var result = engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(pData)"), frame);

            Assert.Equal(false, result);
        }

        [Fact]
        public void IsValidRef_AliasOfAPlainScalar_StillThrows()
        {
            // Resolving the alias widens what __ISVALIDREF accepts only as far
            // as the address types: an alias whose underlying type is an
            // ordinary scalar is the same misuse as naming that scalar
            // directly, and must still be reported rather than answered.
            var (engine, _, frame) = NewHolder(
                "VAR\n\tcount : T_Counter;\nEND_VAR",
                new[] { new KeyValuePair<string, string>("T_Counter", "INT") });

            var ex = Assert.Throws<InvalidOperationException>(
                () => engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(count)"), frame));

            Assert.Contains("__ISVALIDREF", ex.Message);
        }

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
            var (engine, _, frame) = NewFbTargetHolder();

            var result = engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(refMachine)"), frame);

            Assert.Equal(false, result);
        }

        [Fact]
        public void IsValidRef_FbReferenceBoundViaRefAssign_ReturnsTrueAndAliasesFields()
        {
            var (engine, instance, frame) = NewFbTargetHolder();

            engine.ExecuteStatements(Parser.ParseStatements("refMachine REF= machine;"), frame);
            var validity = engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(refMachine)"), frame);

            Assert.Equal(true, validity);

            // REF= replaces the reference's Cell with the target's own Cell,
            // so a write through refMachine.State lands in machine.State -
            // the same Cell, not a copy of it.
            engine.ExecuteStatements(Parser.ParseStatements("refMachine.State := 99;"), frame);
            var machineState = engine.Evaluate(Parser.ParseExpression("machine.State"), frame);

            Assert.Equal(99, machineState);
            Assert.Same(instance.Fields["machine"].Value, frame.ResolveCell("refMachine").Value);
        }

        [Fact]
        public void IsValidRef_UnassignedStructReference_ReturnsFalse()
        {
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

            engine.ExecuteStatements(Parser.ParseStatements("refPayload.Value := 123;"), frame);
            var payloadValue = engine.Evaluate(Parser.ParseExpression("payload.Value"), frame);

            Assert.Equal(123, payloadValue);
        }

        // The four tests below cover REF= targets that are not plain
        // identifiers - struct members, FB-instance members and array
        // elements - the shapes an FB uses to wire up its own REFERENCE TO
        // members inside FB_init.
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
            var (engine, _, frame) = NewMemberRefHolder();

            engine.ExecuteStatements(Parser.ParseStatements("stWidget.IpHandler REF= fbHandler;"), frame);

            var validity = engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(stWidget.IpHandler)"), frame);
            Assert.Equal(true, validity);

            engine.ExecuteStatements(Parser.ParseStatements("stWidget.IpHandler.Value := 99;"), frame);
            var handlerValue = engine.Evaluate(Parser.ParseExpression("fbHandler.Value"), frame);
            Assert.Equal(99, handlerValue);
        }

        [Fact]
        public void RefAssign_StructMemberToNestedStructTarget_AliasesFieldsAndReportsValidRef()
        {
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
            var (engine, _, frame) = NewMemberRefHolder();

            engine.ExecuteStatements(Parser.ParseStatements("fbInner.istWidget REF= stWidget;"), frame);

            var validity = engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(fbInner.istWidget)"), frame);
            Assert.Equal(true, validity);

            // The alias is the very same StructInstance as stWidget, so a
            // binding made through it is visible from stWidget directly.
            engine.ExecuteStatements(Parser.ParseStatements("fbInner.istWidget.IpHandler REF= fbHandler;"), frame);
            var throughAliasValidity = engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(stWidget.IpHandler)"), frame);
            Assert.Equal(true, throughAliasValidity);
        }

        [Fact]
        public void RefAssign_ArrayIndexTarget_Binds()
        {
            // The element type is a plain FB rather than
            // "ARRAY OF REFERENCE TO ..." because VarBlockParser's ARRAY
            // type pattern only accepts a single-word element type. An FB
            // instance is still a CLR reference, so writing through the
            // bound slot is a genuine aliasing check, not a value copy.
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
