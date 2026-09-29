using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // A STRUCT (or ARRAY) is a value: := and a by-value VAR_INPUT hand the
    // target its own copy, while VAR_IN_OUT, REFERENCE TO and REF= are the only
    // ways two names share one storage.
    public class StructCopySemanticsTests
    {
        private const string ItemType = "TYPE ST_Item :\nSTRUCT\n\tbFlag : BOOL;\n\tnValue : INT;\nEND_STRUCT\nEND_TYPE";
        private const string BagType = "TYPE ST_Bag :\nSTRUCT\n\taItems : ARRAY[1..2] OF ST_Item;\nEND_STRUCT\nEND_TYPE";
        private const string NestedType = "TYPE ST_Nested :\nSTRUCT\n\tstInner : ST_Item;\nEND_STRUCT\nEND_TYPE";
        private const string HolderType = "TYPE ST_Holder :\nSTRUCT\n\trefItem : REFERENCE TO ST_Item;\nEND_STRUCT\nEND_TYPE";
        private const string WithFbType = "TYPE ST_WithFb :\nSTRUCT\n\tfbCounter : FB_Counter;\nEND_STRUCT\nEND_TYPE";
        private const string WithInterfaceType = "TYPE ST_WithItf :\nSTRUCT\n\tipCounter : I_Counter;\nEND_STRUCT\nEND_TYPE";

        private static FbInstance Run(
            string fields,
            string body,
            IEnumerable<MethodAst> methods = null,
            string[] extraTypes = null,
            IEnumerable<PouAst> extraPous = null,
            IEnumerable<InterfaceAst> interfaces = null,
            IEnumerable<KeyValuePair<string, IReadOnlyDictionary<string, int>>> enums = null,
            xStunit.Interpreter.Extensibility.NativePlugins plugins = null)
        {
            var structs = new List<StructAst>
            {
                StructDeclParser.Parse(ItemType),
                StructDeclParser.Parse(BagType),
                StructDeclParser.Parse(NestedType),
            };
            foreach (var text in extraTypes ?? Array.Empty<string>())
                structs.Add(StructDeclParser.Parse(text));

            var hostMethods = new List<MethodAst>(methods ?? Array.Empty<MethodAst>())
            {
                new MethodAst("Run", "METHOD Run", body),
            };
            var host = new PouAst("FB_Host", null, fields, "", hostMethods);

            var pous = new List<PouAst> { host };
            pous.AddRange(extraPous ?? Array.Empty<PouAst>());

            var registry = new TypeRegistry(pous, structs, enumMembers: enums, interfaceTypes: interfaces);
            var engine = new Engine(registry, plugins ?? new xStunit.Interpreter.Extensibility.NativePlugins());
            var instance = engine.NewInstance("FB_Host");
            engine.CallMethod(instance, "Run", new Expr[0], new NamedArg[0], null, null);
            return instance;
        }

        private static object Field(FbInstance instance, string name) => instance.Fields[name].Value;

        private static PouAst CounterFb() => new PouAst(
            "FB_Counter",
            null,
            "VAR_OUTPUT\n\tnCount : INT;\nEND_VAR",
            "nCount := nCount + 1;",
            new List<MethodAst>());

        [Fact]
        public void PlainStructAssignment_LaterWriteToCopyLeavesOriginalUntouched()
        {
            var host = Run(
                "VAR\n\tstA : ST_Item;\n\tstB : ST_Item;\n\tbResult : BOOL;\nEND_VAR",
                "stA.bFlag := TRUE;\nstB := stA;\nstB.bFlag := FALSE;\nbResult := stA.bFlag;");

            Assert.Equal(true, Field(host, "bResult"));
        }

        [Fact]
        public void PlainStructAssignment_LaterWriteToOriginalLeavesCopyUntouched()
        {
            var host = Run(
                "VAR\n\tstA : ST_Item;\n\tstB : ST_Item;\n\tbResult : BOOL;\nEND_VAR",
                "stB := stA;\nstA.bFlag := TRUE;\nbResult := stB.bFlag;");

            Assert.Equal(false, Field(host, "bResult"));
        }

        [Fact]
        public void WholeStructHoldingArray_AssignmentCopiesTheNestedArray()
        {
            var host = Run(
                "VAR\n\tstBag : ST_Bag;\n\tstCopy : ST_Bag;\n\tbResult : BOOL;\nEND_VAR",
                "stBag.aItems[1].bFlag := TRUE;\nstCopy := stBag;\nstCopy.aItems[1].bFlag := FALSE;\nbResult := stBag.aItems[1].bFlag;");

            Assert.Equal(true, Field(host, "bResult"));
        }

        [Fact]
        public void NestedStructField_AssignmentCopiesTheNestedStruct()
        {
            var host = Run(
                "VAR\n\tstOuter : ST_Nested;\n\tstCopy : ST_Nested;\n\tbResult : BOOL;\nEND_VAR",
                "stOuter.stInner.bFlag := TRUE;\nstCopy := stOuter;\nstCopy.stInner.bFlag := FALSE;\nbResult := stOuter.stInner.bFlag;");

            Assert.Equal(true, Field(host, "bResult"));
        }

        [Fact]
        public void ArrayElementToElement_AssignmentCopiesTheStruct()
        {
            var host = Run(
                "VAR\n\taItems : ARRAY[1..2] OF ST_Item;\n\tbResult : BOOL;\nEND_VAR",
                "aItems[1].bFlag := TRUE;\naItems[2] := aItems[1];\naItems[2].bFlag := FALSE;\nbResult := aItems[1].bFlag;");

            Assert.Equal(true, Field(host, "bResult"));
        }

        [Fact]
        public void ArrayFieldOfStruct_ElementAssignmentCopiesTheStruct()
        {
            var host = Run(
                "VAR\n\tstBag : ST_Bag;\n\tbResult : BOOL;\nEND_VAR",
                "stBag.aItems[1].bFlag := TRUE;\nstBag.aItems[2] := stBag.aItems[1];\nstBag.aItems[2].bFlag := FALSE;\nbResult := stBag.aItems[1].bFlag;");

            Assert.Equal(true, Field(host, "bResult"));
        }

        [Fact]
        public void WholeArrayAssignment_CopiesElements()
        {
            var host = Run(
                "VAR\n\taFrom : ARRAY[1..2] OF ST_Item;\n\taTo : ARRAY[1..2] OF ST_Item;\n\tbResult : BOOL;\nEND_VAR",
                "aFrom[1].bFlag := TRUE;\naTo := aFrom;\naTo[1].bFlag := FALSE;\nbResult := aFrom[1].bFlag;");

            Assert.Equal(true, Field(host, "bResult"));
        }

        [Fact]
        public void CalleeWritingItsOwnVarInput_DoesNotReachTheCaller()
        {
            var clear = new MethodAst(
                "Clear",
                "METHOD Clear\nVAR_INPUT\n\tiItem : ST_Item;\nEND_VAR",
                "iItem.bFlag := FALSE;");

            var host = Run(
                "VAR\n\tstItem : ST_Item;\n\tbResult : BOOL;\nEND_VAR",
                "stItem.bFlag := TRUE;\nClear(stItem);\nbResult := stItem.bFlag;",
                new[] { clear });

            Assert.Equal(true, Field(host, "bResult"));
        }

        [Fact]
        public void CalleeCopyingAnInputElement_DoesNotReachTheCallersArray()
        {
            var poke = new MethodAst(
                "Poke",
                "METHOD Poke\nVAR_INPUT\n\tiBag : ST_Bag;\nEND_VAR\nVAR\n\ttItem : ST_Item;\nEND_VAR",
                "tItem := iBag.aItems[1];\ntItem.bFlag := FALSE;");

            var host = Run(
                "VAR\n\tstBag : ST_Bag;\n\tbResult : BOOL;\nEND_VAR",
                "stBag.aItems[1].bFlag := TRUE;\nPoke(stBag);\nbResult := stBag.aItems[1].bFlag;",
                new[] { poke });

            Assert.Equal(true, Field(host, "bResult"));
        }

        [Fact]
        public void VarInOutStruct_StillAliasesTheCallersStruct()
        {
            var clear = new MethodAst(
                "Clear",
                "METHOD Clear\nVAR_IN_OUT\n\tioItem : ST_Item;\nEND_VAR",
                "ioItem.bFlag := FALSE;");

            var host = Run(
                "VAR\n\tstItem : ST_Item;\n\tbResult : BOOL;\nEND_VAR",
                "stItem.bFlag := TRUE;\nClear(stItem);\nbResult := stItem.bFlag;",
                new[] { clear });

            Assert.Equal(false, Field(host, "bResult"));
        }

        [Fact]
        public void VarInOutStruct_WholeAssignmentInsideCalleeReachesTheCaller()
        {
            var replace = new MethodAst(
                "Replace",
                "METHOD Replace\nVAR_INPUT\n\tiSource : ST_Item;\nEND_VAR\nVAR_IN_OUT\n\tioItem : ST_Item;\nEND_VAR",
                "ioItem := iSource;");

            var host = Run(
                "VAR\n\tstItem : ST_Item;\n\tstSource : ST_Item;\n\tbResult : BOOL;\nEND_VAR",
                "stSource.bFlag := TRUE;\nReplace(stSource, stItem);\nbResult := stItem.bFlag;",
                new[] { replace });

            Assert.Equal(true, Field(host, "bResult"));
        }

        [Fact]
        public void VarInOutScalar_WriteInsideMethodReachesTheCaller()
        {
            var bump = new MethodAst(
                "Bump",
                "METHOD Bump\nVAR_IN_OUT\n\tioValue : INT;\nEND_VAR",
                "ioValue := ioValue + 1;");

            var host = Run(
                "VAR\n\tnValue : INT := 4;\nEND_VAR",
                "Bump(nValue);",
                new[] { bump });

            Assert.Equal(5, Field(host, "nValue"));
        }

        [Fact]
        public void ReferenceToStruct_RefBindingStillAliasesTheTarget()
        {
            var host = Run(
                "VAR\n\tstItem : ST_Item;\n\trefItem : REFERENCE TO ST_Item;\n\tbResult : BOOL;\nEND_VAR",
                "stItem.bFlag := TRUE;\nrefItem REF= stItem;\nrefItem.bFlag := FALSE;\nbResult := stItem.bFlag;");

            Assert.Equal(false, Field(host, "bResult"));
        }

        [Fact]
        public void ReferenceToStruct_AssigningThroughTheReferenceCopiesIntoTheTarget()
        {
            var host = Run(
                "VAR\n\tstItem : ST_Item;\n\tstSource : ST_Item;\n\trefItem : REFERENCE TO ST_Item;\n\tbResult : BOOL;\nEND_VAR",
                "refItem REF= stItem;\nstSource.bFlag := TRUE;\nrefItem := stSource;\nstSource.bFlag := FALSE;\nbResult := stItem.bFlag;");

            Assert.Equal(true, Field(host, "bResult"));
        }

        [Fact]
        public void RefBindingOfStructMember_AliasesTheSource()
        {
            var host = Run(
                "VAR\n\tstItem : ST_Item;\n\tstHolder : ST_Holder;\n\tbResult : BOOL;\nEND_VAR",
                "stItem.bFlag := TRUE;\nstHolder.refItem REF= stItem;\nstHolder.refItem.bFlag := FALSE;\nbResult := stItem.bFlag;",
                extraTypes: new[] { HolderType });

            Assert.Equal(false, Field(host, "bResult"));
        }

        [Fact]
        public void CopiedStructWithReferenceMember_KeepsPointingAtTheSameTarget()
        {
            var host = Run(
                "VAR\n\tstItem : ST_Item;\n\tstHolder : ST_Holder;\n\tstHolderCopy : ST_Holder;\n\tbResult : BOOL;\nEND_VAR",
                "stItem.bFlag := TRUE;\nstHolder.refItem REF= stItem;\nstHolderCopy := stHolder;\nstHolderCopy.refItem.bFlag := FALSE;\nbResult := stItem.bFlag;",
                extraTypes: new[] { HolderType });

            Assert.Equal(false, Field(host, "bResult"));
        }

        // A copy shares its reference member's target, not the binding itself:
        // re-binding the copy's reference must leave the original bound where it was.
        [Fact]
        public void CopiedStructWithReferenceMember_RebindingTheCopyLeavesTheOriginalBound()
        {
            var host = Run(
                "VAR\n\tstItem : ST_Item;\n\tstOther : ST_Item;\n\tstHolder : ST_Holder;\n\tstHolderCopy : ST_Holder;\n\tbItem : BOOL;\n\tbOther : BOOL;\nEND_VAR",
                "stItem.bFlag := TRUE;\nstOther.bFlag := TRUE;\nstHolder.refItem REF= stItem;\nstHolderCopy := stHolder;\nstHolderCopy.refItem REF= stOther;\nstHolder.refItem.bFlag := FALSE;\nbItem := stItem.bFlag;\nbOther := stOther.bFlag;",
                extraTypes: new[] { HolderType });

            Assert.Equal(false, Field(host, "bItem"));
            Assert.Equal(true, Field(host, "bOther"));
        }

        [Fact]
        public void FbInstanceInsideCopiedStruct_IsCopiedByValue()
        {
            var host = Run(
                "VAR\n\tstA : ST_WithFb;\n\tstB : ST_WithFb;\n\tnA : INT;\n\tnB : INT;\nEND_VAR",
                "stA.fbCounter.nCount := 1;\nstB := stA;\nstB.fbCounter.nCount := 2;\nnA := stA.fbCounter.nCount;\nnB := stB.fbCounter.nCount;",
                extraTypes: new[] { WithFbType },
                extraPous: new[] { CounterFb() });

            Assert.Equal(1, Field(host, "nA"));
            Assert.Equal(2, Field(host, "nB"));
        }

        [Fact]
        public void InterfaceReferenceInsideCopiedStruct_StaysASharedReference()
        {
            var counterInterface = new InterfaceAst(
                "I_Counter",
                "INTERFACE I_Counter",
                new List<MethodAst>(),
                new List<PropertyAst>());

            var host = Run(
                "VAR\n\tfbShared : FB_Counter;\n\tstA : ST_WithItf;\n\tstB : ST_WithItf;\nEND_VAR",
                "stA.ipCounter := fbShared;\nstB := stA;",
                extraTypes: new[] { WithInterfaceType },
                extraPous: new[] { CounterFb() },
                interfaces: new[] { counterInterface });

            var copy = (StructInstance)Field(host, "stB");
            Assert.Same(Field(host, "fbShared"), copy.Fields["ipCounter"].Value);
        }

        private static PouAst LogFb() => new PouAst(
            "FB_Log",
            null,
            "VAR_IN_OUT CONSTANT\n\tsMsg : STRING;\nEND_VAR",
            "",
            new List<MethodAst>());

        // A VAR_IN_OUT binding persists across calls that pass no argument, but
        // a later by-value argument replaces it with storage of the FB's own:
        // that argument must never be written through into the earlier caller
        // variable.
        [Fact]
        public void BareInvocation_LaterByValueArgument_DoesNotWriteThroughAnEarlierInOutBinding()
        {
            var host = Run(
                "VAR\n\tfbLog : FB_Log;\n\tsBuf : STRING;\n\tsResult : STRING;\nEND_VAR",
                "sBuf := 'buf';\nfbLog(sMsg := sBuf);\nfbLog(sMsg := 'Done');\nsResult := sBuf;",
                extraPous: new[] { LogFb() });

            Assert.Equal("buf", Field(host, "sResult"));
        }

        // The value a callee publishes is the callee's own storage; the caller
        // holds a copy, so a later write to the caller's variable stays out of
        // the FB.
        [Fact]
        public void OutputWriteBack_CopiesTheStructIntoTheCallersVariable()
        {
            var outFb = new PouAst(
                "FB_Out",
                null,
                "VAR_OUTPUT\n\tstOut : ST_Item;\nEND_VAR",
                "stOut.bFlag := TRUE;",
                new List<MethodAst>());

            var host = Run(
                "VAR\n\tfbOut : FB_Out;\n\tstY : ST_Item;\n\tnY : INT;\n\tnFb : INT;\nEND_VAR",
                "fbOut(stOut => stY);\nstY.nValue := 50;\nfbOut();\nnFb := fbOut.stOut.nValue;\nnY := stY.nValue;",
                extraPous: new[] { outFb });

            Assert.Equal(0, Field(host, "nFb"));
            Assert.Equal(50, Field(host, "nY"));
        }

        [Fact]
        public void CopiedStructWithPointerMember_RepointingTheCopyLeavesTheOriginalPointer()
        {
            const string ptrType = "TYPE ST_Ptr :\nSTRUCT\n\tpVal : POINTER TO INT;\nEND_STRUCT\nEND_TYPE";
            var host = Run(
                "VAR\n\tn1 : INT := 1;\n\tn2 : INT := 2;\n\tstA : ST_Ptr;\n\tstB : ST_Ptr;\n\tnResult : INT;\nEND_VAR",
                "stA.pVal := ADR(n1);\nstB := stA;\nstB.pVal := ADR(n2);\nnResult := stA.pVal^;",
                extraTypes: new[] { ptrType });

            Assert.Equal(1, Field(host, "nResult"));
        }

        [Fact]
        public void EdgeTriggerInsideCopiedStruct_KeepsItsEdgeMemory()
        {
            const string type = "TYPE ST_WithTrig :\nSTRUCT\n\tfbTrig : R_TRIG;\nEND_STRUCT\nEND_TYPE";
            var host = Run(
                "VAR\n\tstA : ST_WithTrig;\n\tstB : ST_WithTrig;\n\trefA : REFERENCE TO R_TRIG;\n\trefB : REFERENCE TO R_TRIG;\n\tbQ : BOOL;\nEND_VAR",
                "refA REF= stA.fbTrig;\nrefA(CLK := FALSE);\nstB := stA;\nrefB REF= stB.fbTrig;\nrefB(CLK := TRUE);\nbQ := refB.Q;",
                extraTypes: new[] { type });

            Assert.Equal(true, Field(host, "bQ"));
        }

        [Fact]
        public void CounterInsideCopiedStruct_KeepsItsRisingEdgeMemory()
        {
            const string type = "TYPE ST_WithCtu :\nSTRUCT\n\tfbCtu : CTU;\nEND_STRUCT\nEND_TYPE";
            var host = Run(
                "VAR\n\tstA : ST_WithCtu;\n\tstB : ST_WithCtu;\n\trefA : REFERENCE TO CTU;\n\trefB : REFERENCE TO CTU;\n\tnCv : INT;\nEND_VAR",
                "refA REF= stA.fbCtu;\nrefA(CU := TRUE, RESET := FALSE, PV := 5);\nstB := stA;\nrefB REF= stB.fbCtu;\nrefB(CU := TRUE, RESET := FALSE, PV := 5);\nnCv := refB.CV;",
                extraTypes: new[] { type });

            Assert.Equal(1, Field(host, "nCv"));
        }

        [Fact]
        public void TimerInsideCopiedArray_KeepsItsElapsedTime()
        {
            var host = Run(
                "VAR\n\taA : ARRAY[1..1] OF TON;\n\taB : ARRAY[1..1] OF TON;\n\trefA : REFERENCE TO TON;\n\trefB : REFERENCE TO TON;\n\tbQ : BOOL;\nEND_VAR",
                "refA REF= aA[1];\nrefA(IN := TRUE, PT := T#1S);\nAdvanceClock(T#600ms);\nrefA(IN := TRUE, PT := T#1S);\naB := aA;\nAdvanceClock(T#600ms);\nrefB REF= aB[1];\nrefB(IN := TRUE, PT := T#1S);\nbQ := refB.Q;");

            Assert.Equal(true, Field(host, "bQ"));
        }

        private sealed class CountingBlock : xStunit.Interpreter.Extensibility.IXstunitNativeFunctionBlock
        {
            private int _calls;

            public string TypeName => "FB_Counting";

            public IReadOnlyList<xStunit.Interpreter.Extensibility.NativeFieldDeclaration> Fields => new[]
            {
                new xStunit.Interpreter.Extensibility.NativeFieldDeclaration("nCalls", 0),
            };

            public IReadOnlyList<string> PositionalInputNames => Array.Empty<string>();

            public IReadOnlyList<string> MethodNames => Array.Empty<string>();

            public xStunit.Interpreter.Extensibility.IXstunitNativeFunctionBlock CreateInstance() => new CountingBlock();

            public object Invoke(xStunit.Interpreter.Extensibility.NativeFunctionBlockCall call)
            {
                _calls++;
                call.SetField("nCalls", _calls);
                return null;
            }
        }

        // A plugin FB's private state is behind the public contract and cannot
        // be copied: the copy gets an independent instance, never the source's,
        // so the two cannot disturb each other.
        [Fact]
        public void PluginFbInsideCopiedStruct_GetsItsOwnIndependentInstance()
        {
            const string type = "TYPE ST_WithPlugin :\nSTRUCT\n\tfbCounting : FB_Counting;\nEND_STRUCT\nEND_TYPE";
            var plugins = new xStunit.Interpreter.Extensibility.NativePlugins();
            plugins.FunctionBlocks.Register(new CountingBlock());

            var host = Run(
                "VAR\n\tstA : ST_WithPlugin;\n\tstB : ST_WithPlugin;\n\trefA : REFERENCE TO FB_Counting;\n\trefB : REFERENCE TO FB_Counting;\n\tnA : INT;\n\tnB : INT;\nEND_VAR",
                "refA REF= stA.fbCounting;\nrefA();\nrefA();\nstB := stA;\nrefB REF= stB.fbCounting;\nrefB();\nrefA();\nnA := stA.fbCounting.nCalls;\nnB := stB.fbCounting.nCalls;",
                extraTypes: new[] { type },
                plugins: plugins);

            Assert.Equal(3, Field(host, "nA"));
            Assert.Equal(1, Field(host, "nB"));
        }

        private static MethodAst ReturnInOutConstant(string name, string type) => new MethodAst(
            name,
            $"METHOD {name} : {type}\nVAR_IN_OUT CONSTANT\n\tioValue : {type};\nEND_VAR",
            $"{name} := ioValue;");

        [Fact]
        public void VarInOutConstant_GivenUserEnumLiteral_BindsByValue()
        {
            var members = new Dictionary<string, int> { ["Auto"] = 1, ["Manual"] = 2 };
            var host = Run(
                "VAR\n\tnResult : INT;\nEND_VAR",
                "nResult := GetMode(E_Mode.Auto);",
                new[] { ReturnInOutConstant("GetMode", "INT") },
                enums: new[] { new KeyValuePair<string, IReadOnlyDictionary<string, int>>("E_Mode", members) });

            Assert.Equal(1, Field(host, "nResult"));
        }

        [Fact]
        public void VarInOutConstant_GivenBuiltInEnumLiteral_BindsByValue()
        {
            var host = Run(
                "VAR\n\tbResult : BOOL;\nEND_VAR",
                "bResult := GetSeverity(TcEventSeverity.Warning) = TcEventSeverity.Warning;",
                new[] { ReturnInOutConstant("GetSeverity", "INT") });

            Assert.Equal(true, Field(host, "bResult"));
        }

        [Fact]
        public void VarInOutConstant_GivenUnqualifiedPluginConstant_BindsByValue()
        {
            var plugins = new xStunit.Interpreter.Extensibility.NativePlugins();
            plugins.Constants.Register(new xStunit.Interpreter.Extensibility.NativeConstant("PLUGIN_LIMIT", 7));

            var host = Run(
                "VAR\n\tnResult : INT;\nEND_VAR",
                "nResult := GetLimit(PLUGIN_LIMIT);",
                new[] { ReturnInOutConstant("GetLimit", "INT") },
                plugins: plugins);

            Assert.Equal(7, Field(host, "nResult"));
        }

        [Fact]
        public void WholeStructAssignment_KeepsPointersIntoItsMembersValid()
        {
            var host = Run(
                "VAR\n\tstA : ST_Item;\n\tstB : ST_Item;\n\tpVal : POINTER TO INT;\n\tnResult : INT;\nEND_VAR",
                "stB.nValue := 7;\npVal := ADR(stA.nValue);\nstA := stB;\nnResult := pVal^;");

            Assert.Equal(7, Field(host, "nResult"));
        }

        [Fact]
        public void WholeStructAssignment_KeepsReferencesIntoItsMembersValid()
        {
            var host = Run(
                "VAR\n\tstA : ST_Item;\n\tstB : ST_Item;\n\trefVal : REFERENCE TO INT;\n\tnResult : INT;\nEND_VAR",
                "stB.nValue := 7;\nrefVal REF= stA.nValue;\nstA := stB;\nnResult := refVal;");

            Assert.Equal(7, Field(host, "nResult"));
        }

        [Fact]
        public void WholeStructAssignment_KeepsAPersistedInOutBindingIntoAMemberValid()
        {
            var inOutFb = new PouAst(
                "FB_InOutFb",
                null,
                "VAR_IN_OUT\n\tioVal : INT;\nEND_VAR",
                "",
                new List<MethodAst>());

            var host = Run(
                "VAR\n\tfbIo : FB_InOutFb;\n\tstA : ST_Item;\n\tstB : ST_Item;\n\tnResult : INT;\nEND_VAR",
                "stB.nValue := 7;\nfbIo(ioVal := stA.nValue);\nstA := stB;\nnResult := fbIo.ioVal;",
                extraPous: new[] { inOutFb });

            Assert.Equal(7, Field(host, "nResult"));
        }

        [Fact]
        public void WholeArrayAssignment_KeepsReferencesIntoItsElementsValid()
        {
            var host = Run(
                "VAR\n\taItems : ARRAY[1..2] OF ST_Item;\n\taOther : ARRAY[1..2] OF ST_Item;\n\trefItem : REFERENCE TO ST_Item;\n\tnResult : INT;\nEND_VAR",
                "aOther[1].nValue := 8;\nrefItem REF= aItems[1];\naItems := aOther;\nnResult := refItem.nValue;");

            Assert.Equal(8, Field(host, "nResult"));
        }

        [Fact]
        public void FbToFbAssignment_CopiesInsteadOfAliasing()
        {
            var host = Run(
                "VAR\n\tfbA : FB_Counter;\n\tfbB : FB_Counter;\n\tnResult : INT;\nEND_VAR",
                "fbA.nCount := 3;\nfbB := fbA;\nfbB.nCount := 9;\nnResult := fbA.nCount;",
                extraPous: new[] { CounterFb() });

            Assert.Equal(3, Field(host, "nResult"));
        }

        [Fact]
        public void FbMemberToFbMemberAssignment_CopiesInsteadOfAliasing()
        {
            var host = Run(
                "VAR\n\tstA : ST_WithFb;\n\tstB : ST_WithFb;\n\tnResult : INT;\nEND_VAR",
                "stA.fbCounter.nCount := 3;\nstB.fbCounter := stA.fbCounter;\nstB.fbCounter.nCount := 9;\nnResult := stA.fbCounter.nCount;",
                extraTypes: new[] { WithFbType },
                extraPous: new[] { CounterFb() });

            Assert.Equal(3, Field(host, "nResult"));
        }

        [Fact]
        public void FbTypedVarInput_ReceivesACopy()
        {
            var poke = new MethodAst(
                "Poke",
                "METHOD Poke\nVAR_INPUT\n\tiCounter : FB_Counter;\nEND_VAR",
                "iCounter.nCount := 9;");

            var host = Run(
                "VAR\n\tfbA : FB_Counter;\n\tnResult : INT;\nEND_VAR",
                "fbA.nCount := 3;\nPoke(fbA);\nnResult := fbA.nCount;",
                new[] { poke },
                extraPous: new[] { CounterFb() });

            Assert.Equal(3, Field(host, "nResult"));
        }

        [Fact]
        public void CopiedFb_KeepsItsVarInOutBinding()
        {
            const string type = "TYPE ST_WithIo :\nSTRUCT\n\tfbIo : FB_InOutFb;\nEND_STRUCT\nEND_TYPE";
            var inOutFb = new PouAst(
                "FB_InOutFb",
                null,
                "VAR_IN_OUT\n\tioVal : INT;\nEND_VAR",
                "",
                new List<MethodAst>());

            var host = Run(
                "VAR\n\tstA : ST_WithIo;\n\tstB : ST_WithIo;\n\trefA : REFERENCE TO FB_InOutFb;\n\tnX : INT;\n\tnResult : INT;\nEND_VAR",
                "refA REF= stA.fbIo;\nrefA(ioVal := nX);\nstB := stA;\nstB.fbIo.ioVal := 5;\nnResult := nX;",
                extraTypes: new[] { type },
                extraPous: new[] { inOutFb });

            Assert.Equal(5, Field(host, "nResult"));
        }

        [Fact]
        public void CopiedFb_KeepsItsMethodInstanceState()
        {
            const string type = "TYPE ST_WithInst :\nSTRUCT\n\tfbInst : FB_Inst;\nEND_STRUCT\nEND_TYPE";
            var instFb = new PouAst(
                "FB_Inst",
                null,
                "",
                "",
                new List<MethodAst>
                {
                    new MethodAst(
                        "M",
                        "METHOD M : INT\nVAR_INST\n\tnCalls : INT;\nEND_VAR",
                        "nCalls := nCalls + 1;\nM := nCalls;"),
                });

            var host = Run(
                "VAR\n\tstA : ST_WithInst;\n\tstB : ST_WithInst;\n\trefA : REFERENCE TO FB_Inst;\n\trefB : REFERENCE TO FB_Inst;\n\tnDummy : INT;\n\tnResult : INT;\nEND_VAR",
                "refA REF= stA.fbInst;\nnDummy := refA.M();\nnDummy := refA.M();\nstB := stA;\nrefB REF= stB.fbInst;\nnResult := refB.M();",
                extraTypes: new[] { type },
                extraPous: new[] { instFb });

            Assert.Equal(3, Field(host, "nResult"));
        }
    }
}
