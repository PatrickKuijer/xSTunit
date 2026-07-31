using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcXunit-0v1: VAR_INPUT/VAR_OUTPUT of a nested (non-native, non-suite) FB
    // instance must be materialized into FbInstance.Fields at NewInstance()
    // time, so dot-access and bare invocation of the FB both see the same
    // persisted cells.
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

        // TcXunit-21j: named args must only bind against the callee's own
        // VAR_INPUT/VAR_IN_OUT decls (mirrors BindParams), not the full
        // Fields set, else a named arg can silently clobber VAR_OUTPUT/Local
        // state before the body ever runs.
        [Fact]
        public void BareInvocation_NamedArgTargetingVarOutput_DoesNotOverwriteIt()
        {
            var engine = NewEngine("sfbAdder(A := 1, B := 2, Sum := 999);");
            var instance = engine.NewInstance("FB_Outer");

            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

            var nested = (FbInstance)instance.Fields["sfbAdder"].Value;
            Assert.Equal(3, nested.Fields["Sum"].Value);
        }

        // Real-world repro (TcXunit-996 follow-up): a TcUnit TEST case with a
        // METHOD-local VAR of a nested FB type (not a top-level FB field) that
        // bare-invokes it, e.g. FB_DigitalInputFilterTests declaring
        // `sfbDigitalInput : FB_DigitalInputFilter` inside the METHOD and
        // calling `sfbDigitalInput()`. The local var lives in the calling
        // Frame's Locals, not the suite instance's Fields, so bare-invocation
        // dispatch must also check the caller frame's locals, not just
        // instance.Fields.
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

        // TcXunit-3zk: same coverage gap as
        // BareInvocation_OfMethodLocalFbVar_BindsAndRunsInsteadOfFallingThroughToNative
        // above, but for the *native timer host* bare-invocation branch
        // (Engine.Invocation.cs's first TryResolveCalleeCell check, ~line 74)
        // rather than the ordinary-interpreted-FB branch (~line 101) - only
        // the latter had a METHOD-local-var regression test before this.
        // A METHOD-local `fbTimer : TON` bare-invoked as `fbTimer(...)` must
        // resolve via callerFrame.Locals (it isn't a field on the suite/outer
        // instance) and drive the native TimerHost, not silently no-op.
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

        // TcXunit-3zk: TryResolveCalleeCell must NOT consult the caller
        // frame's Locals for an explicit-receiver call (someObj.Foo()) -
        // that precedence is only correct for a genuine bare/self
        // invocation where instance == callerFrame.Instance. Here the
        // calling METHOD has its own unrelated local `DoStuff : FB_Adder`,
        // and the call is `sfbTarget.DoStuff(4, 5)` where sfbTarget's type
        // (FB_Target) has neither a method nor a field named DoStuff. Before
        // the fix, TryResolveCalleeCell would find the caller's local
        // `DoStuff` cell and silently bare-invoke it instead of failing;
        // after the fix this must throw "method not found" against the
        // receiver's own type, ignoring the caller's local entirely.
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

        // TcXunit-guo: IsPersistedField/GetOwnInputDecls both explicitly
        // include VarSection.InOut, but no existing test bare-invokes a
        // VAR_IN_OUT param. InvokeFbInstance binds by evaluating the arg
        // expression and assigning it into the callee's Cell (by value),
        // never by aliasing/sharing the caller's Cell - so mutations the
        // callee makes to its VAR_IN_OUT parameter must NOT propagate back
        // to the caller's variable. This documents that known limitation
        // for the bare-invocation path specifically.
        [Fact]
        public void BareInvocation_VarInOutParam_BindsByValue_MutationsDoNotPropagateBack()
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
            // By-value binding: the caller's own variable is untouched by
            // the callee's mutation of its VAR_IN_OUT parameter.
            Assert.Equal(5, instance.Fields["callerVal"].Value);
        }

        // TcXunit-guo: GetOwnInputDecls walks the BaseTypeName chain
        // (base-to-derived) to collect VAR_INPUT/VAR_IN_OUT decls, mirroring
        // NewInstance's Fields-materialization loop. No existing test
        // bare-invokes a nested FB whose VAR_INPUT is only declared on a
        // base type reached via EXTENDS - this proves the base-declared
        // input actually binds through the derived-type bare-invoke path.
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

        // TcXunit-guo: TryResolveCalleeCell checks callerFrame.Locals before
        // instance.Fields specifically to support a METHOD-local VAR naming
        // an ordinary interpreted FB (see comment near
        // Engine.Invocation.cs's InvokeFbInstance branch, ~line 96-101,
        // "sfbLoopback(ibEnable := TRUE) ... or method-local var"). All the
        // pre-existing BareInvocation_* tests above only exercise the
        // instance-Field-scoped case; this proves the same bare-invocation
        // binding/dispatch generalizes to a nested FB declared as a
        // METHOD-local VAR rather than a top-level instance field.
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

