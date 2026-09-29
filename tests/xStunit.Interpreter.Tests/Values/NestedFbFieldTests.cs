using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A nested FB's VAR_INPUT/VAR_OUTPUT are persistent instance state, not
    // call parameters: they exist from instantiation and outlive each
    // invocation, so dot-access and bare invocation must reach the same
    // cells.
    public class NestedFbFieldTests
    {
        private static Engine NewEngine(string outerImplementation)
        {
            var adder = new PouAst(
                "FB_Adder",
                null,
                "VAR_INPUT\n\tA : INT;\n\tB : INT;\nEND_VAR\nVAR_OUTPUT\n\tSum : INT;\nEND_VAR",
                "Sum := A + B;",
                new List<MethodAst>());

            var outer = new PouAst(
                "FB_Outer",
                null,
                "VAR\n\tsfbAdder : FB_Adder;\nEND_VAR",
                outerImplementation,
                new List<MethodAst>());

            return new Engine(new TypeRegistry(new[] { adder, outer }));
        }

        [Fact]
        public void NewInstance_MaterializesNestedFbVarInputAndVarOutputFields()
        {
            var engine = NewEngine("");
            var instance = engine.NewInstance("FB_Outer");

            var nested = Assert.IsType<FbInstance>(instance.Fields["sfbAdder"].Value);
            Assert.True(nested.Fields.ContainsKey("A"));
            Assert.True(nested.Fields.ContainsKey("B"));
            Assert.True(nested.Fields.ContainsKey("Sum"));
        }

        [Fact]
        public void DotAccess_WritesNestedFbVarInputsBeforeAnyInvocation()
        {
            var engine = NewEngine("sfbAdder.A := 5;\nsfbAdder.B := 3;\nsfbAdder();");
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            var nested = (FbInstance)instance.Fields["sfbAdder"].Value;
            Assert.Equal(8, nested.Fields["Sum"].Value);
        }

        [Fact]
        public void BareInvocation_BindsPositionalArgsIntoPersistedFields()
        {
            var engine = NewEngine("sfbAdder(10, 20);");
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            var nested = (FbInstance)instance.Fields["sfbAdder"].Value;
            Assert.Equal(10, nested.Fields["A"].Value);
            Assert.Equal(20, nested.Fields["B"].Value);
            Assert.Equal(30, nested.Fields["Sum"].Value);
        }

        [Fact]
        public void BareInvocation_BindsNamedArgsIntoPersistedFields()
        {
            var engine = NewEngine("sfbAdder(B := 2, A := 7);");
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            var nested = (FbInstance)instance.Fields["sfbAdder"].Value;
            Assert.Equal(9, nested.Fields["Sum"].Value);
        }

        [Fact]
        public void DotAccess_ReadsNestedFbVarOutputAfterInvocation()
        {
            var engine = NewEngine("sfbAdder(1, 1);\nsfbAdder();");
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            var nested = (FbInstance)instance.Fields["sfbAdder"].Value;
            Assert.Equal(2, nested.Fields["Sum"].Value);
        }

        // Named args bind only against the callee's inputs. Binding against
        // the full field set instead would let a caller overwrite VAR_OUTPUT
        // or local state before the body ever runs.
        [Fact]
        public void BareInvocation_NamedArgTargetingVarOutput_DoesNotOverwriteIt()
        {
            var engine = NewEngine("sfbAdder(A := 1, B := 2, Sum := 999);");
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            var nested = (FbInstance)instance.Fields["sfbAdder"].Value;
            Assert.Equal(3, nested.Fields["Sum"].Value);
        }

        // An FB declared as a METHOD-local VAR lives in the calling frame's
        // locals rather than the instance's fields, so bare-invocation
        // dispatch has to look there too. Test methods commonly declare
        // the FB under test this way.
        [Fact]
        public void BareInvocation_OfMethodLocalFbVar_BindsAndRunsInsteadOfFallingThroughToNative()
        {
            var adder = new PouAst(
                "FB_Adder",
                null,
                "VAR_INPUT\n\tA : INT;\n\tB : INT;\nEND_VAR\nVAR_OUTPUT\n\tSum : INT;\nEND_VAR",
                "Sum := A + B;",
                new List<MethodAst>());

            var method = new MethodAst(
                "DoAdd",
                "METHOD PRIVATE DoAdd\nVAR\n\tsfbAdder : FB_Adder;\nEND_VAR",
                "sfbAdder(4, 5);");

            var outer = new PouAst("FB_Outer", null, "", "", new List<MethodAst> { method });

            var engine = new Engine(new TypeRegistry(new[] { adder, outer }));
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "DoAdd", new Expr[0], new NamedArg[0], null, null);
        }

        // Same METHOD-local resolution, but reaching a natively hosted FB
        // (TON) instead of an interpreted one - a separate dispatch branch,
        // where failing to find the local silently no-ops rather than
        // throwing.
        [Fact]
        public void BareInvocation_OfMethodLocalNativeTimer_BindsAndUpdatesInsteadOfNoOp()
        {
            var method = new MethodAst(
                "DoTimer",
                "METHOD PRIVATE DoTimer\nVAR\n\tfbTimer : TON;\nEND_VAR",
                "fbTimer(IN := TRUE, PT := T#0MS);\nmeasuredQ := fbTimer.Q;");

            var outer = new PouAst(
                "FB_Outer",
                null,
                "VAR\n\tmeasuredQ : BOOL;\nEND_VAR",
                "",
                new List<MethodAst> { method });

            var engine = new Engine(new TypeRegistry(new[] { outer }));
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "DoTimer", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(true, instance.Fields["measuredQ"].Value);
        }

        // The caller's locals are only in scope for a bare/self invocation.
        // With an explicit receiver, a same-named local must not be
        // mistaken for the receiver's member: the call has to fail against
        // the receiver's own type instead of silently invoking the local.
        [Fact]
        public void ExplicitReceiverCall_DoesNotFallBackToCallerFrameLocalOfSameName()
        {
            var adder = new PouAst(
                "FB_Adder",
                null,
                "VAR_INPUT\n\tA : INT;\n\tB : INT;\nEND_VAR\nVAR_OUTPUT\n\tSum : INT;\nEND_VAR",
                "Sum := A + B;",
                new List<MethodAst>());

            var target = new PouAst("FB_Target", null, "", "", new List<MethodAst>());

            var method = new MethodAst(
                "CallOnTarget",
                "METHOD PRIVATE CallOnTarget\nVAR\n\tDoStuff : FB_Adder;\nEND_VAR",
                "sfbTarget.DoStuff(4, 5);");

            var outer = new PouAst(
                "FB_Outer",
                null,
                "VAR\n\tsfbTarget : FB_Target;\nEND_VAR",
                "",
                new List<MethodAst> { method });

            var engine = new Engine(new TypeRegistry(new[] { adder, target, outer }));
            var instance = engine.NewInstance("FB_Outer");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                engine.CallMethod(instance, "CallOnTarget", new Expr[0], new NamedArg[0], null, null));

            Assert.Contains("DoStuff", ex.Message);
        }

        // VAR_IN_OUT is by reference: a write inside the FB body lands in the
        // caller's own variable, not in a private copy of it.
        [Fact]
        public void BareInvocation_VarInOutScalar_WriteInsideBodyReachesTheCaller()
        {
            var inOutFb = new PouAst(
                "FB_InOutFb",
                null,
                "VAR_IN_OUT\n\tioVal : INT;\nEND_VAR",
                "ioVal := ioVal + 100;",
                new List<MethodAst>());

            var outer = new PouAst(
                "FB_Outer",
                null,
                "VAR\n\tsfbInOut : FB_InOutFb;\n\tcallerVal : INT;\nEND_VAR",
                "callerVal := 5;\nsfbInOut(callerVal);",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { inOutFb, outer }));
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            var nested = (FbInstance)instance.Fields["sfbInOut"].Value;
            Assert.Equal(105, nested.Fields["ioVal"].Value);
            Assert.Equal(105, instance.Fields["callerVal"].Value);
        }

        [Fact]
        public void BareInvocation_NamedVarInOutScalar_WriteInsideBodyReachesTheCaller()
        {
            var inOutFb = new PouAst(
                "FB_InOutFb",
                null,
                "VAR_IN_OUT\n\tioVal : INT;\nEND_VAR",
                "ioVal := ioVal + 100;",
                new List<MethodAst>());

            var outer = new PouAst(
                "FB_Outer",
                null,
                "VAR\n\tsfbInOut : FB_InOutFb;\n\tcallerVal : INT;\nEND_VAR",
                "callerVal := 5;\nsfbInOut(ioVal := callerVal);",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { inOutFb, outer }));
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            Assert.Equal(105, instance.Fields["callerVal"].Value);
        }

        [Fact]
        public void BareInvocation_VarInOutStruct_WriteInsideBodyReachesTheCaller()
        {
            var item = StructDeclParser.Parse("TYPE ST_Item :\nSTRUCT\n\tnValue : INT;\nEND_STRUCT\nEND_TYPE");
            var inOutFb = new PouAst(
                "FB_InOutFb",
                null,
                "VAR_IN_OUT\n\tioItem : ST_Item;\nEND_VAR",
                "ioItem.nValue := 7;",
                new List<MethodAst>());

            var outer = new PouAst(
                "FB_Outer",
                null,
                "VAR\n\tsfbInOut : FB_InOutFb;\n\tstItem : ST_Item;\nEND_VAR",
                "sfbInOut(ioItem := stItem);",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { inOutFb, outer }, new[] { item }));
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            var caller = (StructInstance)instance.Fields["stItem"].Value;
            Assert.Equal(7, caller.Fields["nValue"].Value);
        }

        [Fact]
        public void BareInvocation_VarInOutArray_WriteInsideBodyReachesTheCaller()
        {
            var inOutFb = new PouAst(
                "FB_InOutFb",
                null,
                "VAR_IN_OUT\n\tioValues : ARRAY[1..3] OF INT;\nEND_VAR",
                "ioValues[2] := 9;",
                new List<MethodAst>());

            var outer = new PouAst(
                "FB_Outer",
                null,
                "VAR\n\tsfbInOut : FB_InOutFb;\n\taValues : ARRAY[1..3] OF INT;\nEND_VAR",
                "sfbInOut(ioValues := aValues);",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { inOutFb, outer }));
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            var caller = (ArrayValue)instance.Fields["aValues"].Value;
            Assert.Equal(9, caller.Elements[1]);
        }

        [Fact]
        public void BareInvocation_VarInOutBoundToArrayElement_WriteInsideBodyReachesTheCaller()
        {
            var inOutFb = new PouAst(
                "FB_InOutFb",
                null,
                "VAR_IN_OUT\n\tioVal : INT;\nEND_VAR",
                "ioVal := 42;",
                new List<MethodAst>());

            var outer = new PouAst(
                "FB_Outer",
                null,
                "VAR\n\tsfbInOut : FB_InOutFb;\n\taValues : ARRAY[1..3] OF INT;\nEND_VAR",
                "sfbInOut(ioVal := aValues[3]);",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { inOutFb, outer }));
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            var caller = (ArrayValue)instance.Fields["aValues"].Value;
            Assert.Equal(42, caller.Elements[2]);
        }

        [Fact]
        public void BareInvocation_WholeStructAssignedToVarInOut_ReachesTheCallerWithoutAliasingTheSource()
        {
            var item = StructDeclParser.Parse("TYPE ST_Item :\nSTRUCT\n\tnValue : INT;\nEND_STRUCT\nEND_TYPE");
            var replaceFb = new PouAst(
                "FB_Replace",
                null,
                "VAR_INPUT\n\tiSource : ST_Item;\nEND_VAR\nVAR_IN_OUT\n\tioItem : ST_Item;\nEND_VAR",
                "ioItem := iSource;",
                new List<MethodAst>());

            var outer = new PouAst(
                "FB_Outer",
                null,
                "VAR\n\tsfbReplace : FB_Replace;\n\tstSource : ST_Item;\n\tstItem : ST_Item;\nEND_VAR",
                "stSource.nValue := 4;\nsfbReplace(iSource := stSource, ioItem := stItem);\nstSource.nValue := 99;",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { replaceFb, outer }, new[] { item }));
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            var caller = (StructInstance)instance.Fields["stItem"].Value;
            Assert.Equal(4, caller.Fields["nValue"].Value);
        }

        // A VAR_INPUT stays a copy on the bare-invoke path: the FB body owns
        // its input.
        [Fact]
        public void BareInvocation_VarInputStruct_WriteInsideBodyDoesNotReachTheCaller()
        {
            var item = StructDeclParser.Parse("TYPE ST_Item :\nSTRUCT\n\tnValue : INT;\nEND_STRUCT\nEND_TYPE");
            var inputFb = new PouAst(
                "FB_InputFb",
                null,
                "VAR_INPUT\n\titem : ST_Item;\nEND_VAR",
                "item.nValue := 7;",
                new List<MethodAst>());

            var outer = new PouAst(
                "FB_Outer",
                null,
                "VAR\n\tsfbInput : FB_InputFb;\n\tstItem : ST_Item;\nEND_VAR",
                "sfbInput(item := stItem);",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { inputFb, outer }, new[] { item }));
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            var caller = (StructInstance)instance.Fields["stItem"].Value;
            Assert.Equal(0, caller.Fields["nValue"].Value);
        }

        [Fact]
        public void BareInvocation_OfExtendedNestedFb_BindsBaseDeclaredVarInput()
        {
            var baseFb = new PouAst(
                "FB_Base",
                null,
                "VAR_INPUT\n\tBaseVal : INT;\nEND_VAR",
                "",
                new List<MethodAst>());

            var derivedFb = new PouAst(
                "FB_Derived",
                "FB_Base",
                "VAR_OUTPUT\n\tDoubled : INT;\nEND_VAR",
                "Doubled := BaseVal * 2;",
                new List<MethodAst>());

            var outer = new PouAst(
                "FB_Outer",
                null,
                "VAR\n\tsfbDerived : FB_Derived;\nEND_VAR",
                "sfbDerived(21);",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { baseFb, derivedFb, outer }));
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            var nested = (FbInstance)instance.Fields["sfbDerived"].Value;
            Assert.Equal(21, nested.Fields["BaseVal"].Value);
            Assert.Equal(42, nested.Fields["Doubled"].Value);
        }

        [Fact]
        public void BareInvocation_OfMethodLocalNestedFb_BindsPersistedFieldsAndRuns()
        {
            var adder = new PouAst(
                "FB_Adder",
                null,
                "VAR_INPUT\n\tA : INT;\n\tB : INT;\nEND_VAR\nVAR_OUTPUT\n\tSum : INT;\nEND_VAR",
                "Sum := A + B;",
                new List<MethodAst>());

            var method = new MethodAst(
                "DoAdd",
                "METHOD PRIVATE DoAdd\nVAR\n\tsfbAdder : FB_Adder;\nEND_VAR",
                "sfbAdder(10, 20);\nresult := sfbAdder.Sum;");

            var outer = new PouAst(
                "FB_Outer",
                null,
                "VAR\n\tresult : INT;\nEND_VAR",
                "",
                new List<MethodAst> { method });

            var engine = new Engine(new TypeRegistry(new[] { adder, outer }));
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "DoAdd", new Expr[0], new NamedArg[0], null, null);

            Assert.Equal(30, instance.Fields["result"].Value);
        }
    }
}

