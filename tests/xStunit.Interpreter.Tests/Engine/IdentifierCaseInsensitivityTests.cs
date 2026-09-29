using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // IEC 61131-3 identifiers are case-insensitive, and TwinCAT compiles a
    // body that spells a name differently from its declaration without a
    // warning. Every test here declares a name in one spelling and uses it in
    // another, so an ordinal comparison at any single lookup site turns a
    // program TwinCAT accepts into "Unknown variable" or, worse, a silent
    // write into a fresh implicit local that nothing ever reads back.
    public class IdentifierCaseInsensitivityTests
    {
        private static readonly StructAst FlagsStruct = new StructAst(
            "ST_Flags",
            new[] { new VarDecl("bReady", "BOOL", null, VarSection.Local) });

        private static object StepOnce(Engine engine, FbInstance instance) =>
            engine.CallMethod(instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new NamedArg[0], null, null);

        private static object Call(Engine engine, FbInstance instance, string methodName) =>
            engine.CallMethod(instance, methodName, new Expr[0], new NamedArg[0], null, null);

        // The reported repro, verbatim: a VAR_INPUT struct and a VAR_OUTPUT
        // both referenced in a spelling their declaration does not use, and
        // the struct's field reached the same way.
        [Fact]
        public void VarInputStructAndVarOutput_ReferencedInOtherCase_PassTheValueThrough()
        {
            var tracker = new PouAst(
                "FB_StepTracker",
                null,
                "FUNCTION_BLOCK FB_StepTracker\nVAR_INPUT\n\tistFlags : ST_Flags;\nEND_VAR\nVAR_OUTPUT\n\tobReady : BOOL;\nEND_VAR",
                "obReady := istFLAGS.bREADY;",
                new List<MethodAst>());
            var host = new PouAst(
                "FB_Host",
                null,
                "VAR\n\tfbTracker : FB_StepTracker;\n\tstIn : ST_Flags;\n\tbResult : BOOL;\nEND_VAR",
                "stIn.bReady := TRUE;\nfbTracker(istFlags := stIn);\nbResult := fbTracker.obReady;",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { tracker, host }, new[] { FlagsStruct }));
            var instance = engine.NewInstance("FB_Host");

            StepOnce(engine, instance);

            Assert.Equal(true, instance.Fields["bResult"].Value);
        }

        // A read in the wrong case would fail loudly; a WRITE in the wrong
        // case is the dangerous half, because SetVariable declares an
        // implicit local for a name it cannot resolve, and the real field is
        // left at its default without a word.
        [Fact]
        public void MethodLocalAndInstanceField_ReadAndWrittenInOtherCase_ReachTheDeclaredStorage()
        {
            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tnTotal : INT;\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    new MethodAst(
                        "M_Compute",
                        "METHOD M_Compute : INT\nVAR\n\tnScratch : INT := 20;\nEND_VAR",
                        "NTOTAL := NSCRATCH + 1;\nM_Compute := ntotal;"),
                });

            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Widget");

            var result = Call(engine, instance, "M_Compute");

            Assert.Equal(21, result);
            Assert.Equal(21, instance.Fields["nTotal"].Value);
        }

        // Named arguments are matched against the callee's declarations, so a
        // miss there does not throw: the argument silently falls back to
        // positional binding and the parameter keeps its default. nA - nB is
        // chosen so that any such fallback gives a different answer.
        [Fact]
        public void VarInputAndVarInOut_BoundByNameInOtherCase_ReceiveTheArgument()
        {
            var fb = new PouAst(
                "FB_Calc",
                null,
                "VAR\n\tnSource : INT := 5;\n\tnResult : INT;\nEND_VAR",
                "nResult := m_subtract(NB := 2, IONA := nSource);",
                new List<MethodAst>
                {
                    new MethodAst(
                        "M_Subtract",
                        "METHOD M_Subtract : INT\nVAR_IN_OUT\n\tioNa : INT;\nEND_VAR\nVAR_INPUT\n\tnB : INT;\nEND_VAR",
                        "M_Subtract := IONA - nb;"),
                });

            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Calc");

            StepOnce(engine, instance);

            Assert.Equal(3, instance.Fields["nResult"].Value);
        }

        [Fact]
        public void VarOutput_BoundWithArrowInOtherCase_IsWrittenBackToTheCaller()
        {
            var fb = new PouAst(
                "FB_Calc",
                null,
                "VAR\n\tnHalf : INT;\nEND_VAR",
                "M_Split(NIN := 14, OHALF => nHalf);",
                new List<MethodAst>
                {
                    new MethodAst(
                        "M_Split",
                        "METHOD M_Split\nVAR_INPUT\n\tnIn : INT;\nEND_VAR\nVAR_OUTPUT\n\toHalf : INT;\nEND_VAR",
                        "ohalf := NIN / 2;"),
                });

            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Calc");

            StepOnce(engine, instance);

            Assert.Equal(7, instance.Fields["nHalf"].Value);
        }

        [Fact]
        public void GvlNameAndMember_ReferencedInOtherCase_ResolveQualifiedAndBare()
        {
            var gvl = new GvlAst("GVL_Machine", "VAR_GLOBAL\n\tnSpeed : INT := 42;\n\tnSetpoint : INT;\nEND_VAR");
            var fb = new PouAst(
                "FB_Reader",
                null,
                "VAR\n\tnQualified : INT;\n\tnBare : INT;\nEND_VAR",
                "nQualified := gvl_MACHINE.NSPEED;\nnBare := nspeed;\ngvl_machine.NSETPOINT := 9;",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { fb }, gvls: new[] { gvl }));
            var instance = engine.NewInstance("FB_Reader");

            StepOnce(engine, instance);

            Assert.Equal(42, instance.Fields["nQualified"].Value);
            Assert.Equal(42, instance.Fields["nBare"].Value);
            Assert.Equal(9, engine.Evaluate(Parser.ParseExpression("GVL_Machine.nSetpoint"), new Frame(instance, "FB_Reader")));
        }

        // A constant is most often read somewhere other than a body - an
        // ARRAY bound or another declaration's initializer - and those are
        // resolved at construction time, so this covers that path too.
        [Fact]
        public void GvlAndLocalConstants_ReferencedInOtherCase_ResolveInBodiesAndDeclarations()
        {
            var gvl = new GvlAst("GVL_Limits", "VAR_GLOBAL CONSTANT\n\tcMaxItems : INT := 4;\nEND_VAR");
            var fb = new PouAst(
                "FB_Buffer",
                null,
                "VAR CONSTANT\n\tcStep : INT := 3;\nEND_VAR\n" +
                "VAR\n\taItems : ARRAY[1..gvl_limits.CMAXITEMS] OF INT;\n\tnLimit : INT := CMAXITEMS;\n\tnNext : INT;\nEND_VAR",
                "nNext := nLimit + CSTEP;",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { fb }, gvls: new[] { gvl }));
            var instance = engine.NewInstance("FB_Buffer");

            StepOnce(engine, instance);

            Assert.Equal(4, ((ArrayValue)instance.Fields["aItems"].Value).Elements.Length);
            Assert.Equal(7, instance.Fields["nNext"].Value);
        }

        [Fact]
        public void StructFields_ReadWrittenAndInitializedInOtherCase_ReachTheDeclaredField()
        {
            var fb = new PouAst(
                "FB_Holder",
                null,
                "VAR\n\tstPreset : st_flags := (BREADY := TRUE);\n\tstLive : ST_FLAGS;\n\tbPreset : BOOL;\n\tbLive : BOOL;\nEND_VAR",
                "STLIVE.bready := TRUE;\nbPreset := stpreset.BReady;\nbLive := stLive.BREADY;",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { fb }, new[] { FlagsStruct }));
            var instance = engine.NewInstance("FB_Holder");

            StepOnce(engine, instance);

            Assert.Equal(true, instance.Fields["bPreset"].Value);
            Assert.Equal(true, instance.Fields["bLive"].Value);
        }

        [Fact]
        public void FbInstanceMembers_ReachedAndBareInvokedInOtherCase_ReachTheInnerInstance()
        {
            var inner = new PouAst(
                "FB_Inner",
                null,
                "VAR_INPUT\n\tnIn : INT;\nEND_VAR\nVAR_OUTPUT\n\tnOut : INT;\nEND_VAR\nVAR\n\tnValue : INT;\nEND_VAR",
                "NOUT := nin * 10;",
                new List<MethodAst>());
            var outer = new PouAst(
                "FB_Outer",
                null,
                "VAR\n\tfbInner : FB_Inner;\n\tnOut : INT;\n\tnValue : INT;\nEND_VAR",
                "FBINNER.NVALUE := 5;\nnValue := fbinner.nvalue;\nfbINNER(NIN := 3);\nnOut := FbInner.NOut;",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { inner, outer }));
            var instance = engine.NewInstance("FB_Outer");

            StepOnce(engine, instance);

            Assert.Equal(5, instance.Fields["nValue"].Value);
            Assert.Equal(30, instance.Fields["nOut"].Value);
        }

        // The return value is a Local named after the method, so the body's
        // assignment to it, in whatever case, has to land in the Cell the
        // caller reads back - or the call returns the seeded zero.
        [Fact]
        public void Methods_CalledAndReturnAssignedInOtherCase_DispatchAndReturnTheValue()
        {
            var baseFb = new PouAst(
                "FB_Base",
                null,
                "VAR\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    new MethodAst("M_Inherited", "METHOD M_Inherited : INT", "m_INHERITED := 4;"),
                });
            var widget = new PouAst(
                "FB_Widget",
                "fb_base",
                "FUNCTION_BLOCK FB_Widget EXTENDS fb_base\nVAR\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    new MethodAst("M_Helper", "METHOD M_Helper : INT", "m_helper := 10;"),
                    new MethodAst("M_Inherited", "METHOD M_Inherited : INT", "M_INHERITED := SUPER^.M_inherited() + 1;"),
                });
            var host = new PouAst(
                "FB_Host",
                null,
                "VAR\n\tfbWidget : FB_Widget;\n\tnHelper : INT;\n\tnInherited : INT;\nEND_VAR",
                "nHelper := FBWIDGET.m_HELPER();\nnInherited := fbwidget.M_INHERITED();",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { baseFb, widget, host }));
            var instance = engine.NewInstance("FB_Host");

            StepOnce(engine, instance);

            Assert.Equal(10, instance.Fields["nHelper"].Value);
            Assert.Equal(5, instance.Fields["nInherited"].Value);
        }

        [Fact]
        public void Properties_GotAndSetInOtherCase_RunTheirAccessors()
        {
            var drive = new PouAst(
                "FB_Drive",
                null,
                "VAR\n\tsnSpeed : INT;\nEND_VAR",
                "",
                new List<MethodAst>(),
                new List<PropertyAst>
                {
                    new PropertyAst("nSpeed", "PROPERTY nSpeed : INT", "NSPEED := SNSPEED * 2;", "snspeed := nspeed;"),
                });
            var host = new PouAst(
                "FB_Host",
                null,
                "VAR\n\tfbDrive : FB_Drive;\n\tnRead : INT;\nEND_VAR",
                "FBDRIVE.NSPEED := 6;\nnRead := fbdrive.nspeed;",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { drive, host }));
            var instance = engine.NewInstance("FB_Host");

            StepOnce(engine, instance);

            Assert.Equal(12, instance.Fields["nRead"].Value);
        }

        // The member table is handed in as an ordinary, ordinal dictionary on
        // purpose: that is what any caller building one naturally writes, so
        // the registry is the place that has to make member lookup
        // case-insensitive, not every caller.
        [Fact]
        public void EnumTypesAndMembers_ReferencedInOtherCase_ResolveToTheMemberValue()
        {
            var members = new Dictionary<string, IReadOnlyDictionary<string, int>>
            {
                ["E_Color"] = new Dictionary<string, int> { ["Red"] = 0, ["Green"] = 1, ["Blue"] = 2 },
            };
            var fb = new PouAst(
                "FB_Painter",
                null,
                "VAR\n\teColor : e_COLOR;\n\tnColor : INT;\n\tnSeverity : INT;\nEND_VAR",
                "eColor := e_color.BLUE;\nnColor := eCOLOR;\nnSeverity := tceventseverity.WARNING;",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(
                new[] { fb },
                aliases: new[] { new KeyValuePair<string, string>("E_Color", "INT") },
                enumMembers: members));
            var instance = engine.NewInstance("FB_Painter");

            StepOnce(engine, instance);

            Assert.Equal(2, instance.Fields["nColor"].Value);
            Assert.Equal(3, instance.Fields["nSeverity"].Value);
        }

        // DutEnumLoader builds the member table itself, so it is the other
        // source of one and has to hand back the same kind of lookup.
        [Fact]
        public void DutEnumLoader_ParsedMemberTable_MatchesMemberNamesInAnyCase()
        {
            Assert.True(DutEnumLoader.TryParseEnum(
                "TYPE E_Mode : (Idle, Running := 5); END_TYPE", out _, out _, out var members));

            Assert.Equal(5, members["RUNNING"]);
        }

        [Fact]
        public void PouNames_ReferencedInOtherCase_ResolveTypesAndGlobalFunctions()
        {
            var function = new PouAst(
                "F_Double",
                null,
                "FUNCTION F_Double : INT\nVAR_INPUT\n\tnIn : INT;\nEND_VAR",
                "f_DOUBLE := NIN * 2;",
                new List<MethodAst>());
            var inner = new PouAst(
                "FB_Inner",
                null,
                "VAR_OUTPUT\n\tnOut : INT := 8;\nEND_VAR",
                "",
                new List<MethodAst>());
            var host = new PouAst(
                "FB_Host",
                null,
                "VAR\n\tfbInner : fb_INNER;\n\tnDoubled : INT;\n\tnInner : INT;\nEND_VAR",
                "nDoubled := f_double(21);\nnInner := fbInner.nOut;",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { function, inner, host }));
            var instance = engine.NewInstance("FB_Host");

            StepOnce(engine, instance);

            Assert.Equal(42, instance.Fields["nDoubled"].Value);
            Assert.Equal(8, instance.Fields["nInner"].Value);
        }

        [Fact]
        public void StandardFunctionsAndConversions_CalledInOtherCase_AreRecognised()
        {
            var fb = new PouAst(
                "FB_Intrinsics",
                null,
                "VAR\n\tnValue : DINT := -7;\n\tnSize : DINT;\n\tnAbs : DINT;\n\tsJoined : STRING;\n\tfReal : REAL;\n\tnBack : INT;\nEND_VAR",
                "nSize := sizeof(NVALUE);\nnAbs := Abs(nvalue);\nsJoined := concat(str1 := 'ab', STR2 := 'cd');\n" +
                "fReal := int_to_real(3);\nnBack := To_Int(fReal);",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Intrinsics");

            StepOnce(engine, instance);

            Assert.Equal(4, instance.Fields["nSize"].Value);
            Assert.Equal(7, instance.Fields["nAbs"].Value);
            Assert.Equal("abcd", instance.Fields["sJoined"].Value);
            Assert.Equal(3f, instance.Fields["fReal"].Value);
            Assert.Equal(3, instance.Fields["nBack"].Value);
        }

        private sealed class DividingFunction : Extensibility.IXstunitNativeFunction
        {
            public string Name => "F_Divide";

            public object Invoke(Extensibility.NativeCallContext context) =>
                context.RequireInt32("nDividend", 0) / context.RequireInt32("nDivisor", 1);
        }

        // A plugin asks for its parameters by the names it declares, and the
        // caller is free to spell them any way. By-name misses fall back to
        // position, so the arguments are passed out of order: a miss then
        // swaps them rather than going unnoticed.
        [Fact]
        public void NativeFunctionNamedArgs_WrittenInOtherCase_BindByName()
        {
            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tnResult : INT;\nEND_VAR",
                "nResult := f_divide(NDIVISOR := 3, ndividend := 12);",
                new List<MethodAst>());

            var functions = new Extensibility.NativeFunctionRegistry();
            functions.RegisterAll(new[] { new DividingFunction() });
            var engine = new Engine(new TypeRegistry(new[] { fb }), functions);
            var instance = engine.NewInstance("FB_Widget");

            StepOnce(engine, instance);

            Assert.Equal(4, instance.Fields["nResult"].Value);
        }

        // The TcUnit surface is a library FB's methods, called from a suite
        // body like any other method - so its names, its named parameters and
        // the root type a suite EXTENDS are all identifiers too.
        [Fact]
        public void TcUnitSuite_RootAssertsAndParametersInOtherCase_DiscoverAndRun()
        {
            var suite = new PouAst(
                "FB_WidgetTests",
                "tcunit.fb_testsuite",
                "FUNCTION_BLOCK FB_WidgetTests EXTENDS tcunit.fb_testsuite\nVAR\n\tnValue : INT := 42;\nEND_VAR",
                "test('ValueIsFortyTwo');\nassertequals_int(expected := 42, ACTUAL := NVALUE, message := 'value');\n" +
                "Assertequals(EXPECTED := nValue, actual := 42, MESSAGE := 'any');\nTest_Finished();",
                new List<MethodAst>());
            var registry = new TypeRegistry(new[] { suite });

            Assert.True(SuiteDiscovery.IsSuiteType(registry, "FB_WidgetTests"));

            var result = Assert.Single(new Engine(registry).RunSuite("FB_WidgetTests"));
            Assert.True(result.Passed, string.Join("; ", result.Failures.Select(f => f.Message)));
            Assert.Equal("ValueIsFortyTwo", result.Name);
        }

        // Case-insensitive matching must not reach string literal CONTENTS:
        // those are data, and 'abc' and 'ABC' are different values.
        [Fact]
        public void StringLiteralContents_StillCompareCaseSensitively()
        {
            var fb = new PouAst(
                "FB_Strings",
                null,
                "VAR\n\tsValue : STRING := 'abc';\n\tbSame : BOOL;\nEND_VAR",
                "bSame := SVALUE = 'ABC';",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Strings");

            StepOnce(engine, instance);

            Assert.Equal(false, instance.Fields["bSame"].Value);
        }

        // A name that resolves nowhere is still reported the way the source
        // spells it: that is the text the reader will search for.
        [Fact]
        public void UnknownVariable_IsReportedInTheSpellingTheSourceUses()
        {
            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tnKnown : INT;\nEND_VAR",
                "",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Widget");

            var ex = Assert.Throws<InvalidOperationException>(
                () => engine.Evaluate(Parser.ParseExpression("nKNOWN + nMISSING"), new Frame(instance, "FB_Widget")));

            Assert.Contains("'nMISSING'", ex.Message);
        }

        // A fault is attributed to the POU and METHOD as they are DECLARED,
        // not as the call site happened to spell them: the location is what
        // the reader looks up in the project tree, and it is also the key the
        // CLI uses to find the .TcPOU file the fault is in.
        [Fact]
        public void FaultLocation_CalledThroughOtherSpellings_ReportsTheDeclaredNames()
        {
            var widget = new PouAst(
                "FB_Widget",
                null,
                "VAR\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    new MethodAst("M_Fault", "METHOD M_Fault", "NoSuchFunction();"),
                });
            var suite = new PouAst(
                "FB_WidgetTests",
                "TcUnit.FB_TestSuite",
                "VAR\n\tfbWidget : fb_WIDGET;\nEND_VAR",
                "fbwidget.m_FAULT();",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { widget, suite }));

            var ex = Assert.Throws<PlcSourceLocationException>(() => engine.RunSuite("FB_WidgetTests"));

            Assert.Equal("FB_Widget", ex.PouTypeName);
            Assert.Equal("M_Fault", ex.MethodName);
        }

        // REF= replaces the Fields entry wholesale rather than writing through
        // it, so it is the one path that re-stores a key. The reference has to
        // land on the declared field - not on a local that dies with the call -
        // and the entry has to keep the spelling it was declared with.
        [Fact]
        public void RefBinding_TargetAndReferenceInOtherCase_BindsTheDeclaredFieldUnderItsDeclaredName()
        {
            var fb = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tnTarget : INT := 3;\n\trefValue : REFERENCE TO INT;\nEND_VAR",
                "REFVALUE REF= ntarget;",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Widget");

            StepOnce(engine, instance);

            Assert.Same(instance.Fields["nTarget"], instance.Fields["refValue"]);
            Assert.Equal(new[] { "nTarget", "refValue" }, instance.Fields.Keys.ToArray());
        }
    }
}
