using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // An instance declaration that passes arguments to FB_init must still
    // create the instance: dropping the line makes every later use of the name
    // fail as an unknown variable, far from the declaration that caused it.
    public class FbInitArgumentTests
    {
        private static PouAst GadgetSetting() =>
            new PouAst(
                "FB_GadgetSetting",
                null,
                "FUNCTION_BLOCK FB_GadgetSetting\nVAR_INPUT\n\tfValue : LREAL;\nEND_VAR\nVAR_OUTPUT\n\tsLabel : STRING;\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    new MethodAst(
                        "FB_init",
                        "METHOD FB_init : BOOL\nVAR_INPUT\n\tbInitRetains : BOOL;\n\tbInCopyCode : BOOL;\n\tifDefault : LREAL;\n\tisLabel : STRING;\nEND_VAR",
                        "fValue := ifDefault;\nsLabel := isLabel;"),
                });

        private static PouAst HolderFb(string varLines) =>
            new PouAst(
                "FB_Holder",
                null,
                "FUNCTION_BLOCK FB_Holder\nVAR\n" + varLines + "\nEND_VAR",
                "",
                new List<MethodAst>());

        private static FbInstance Gadget(FbInstance holder, string field) =>
            (FbInstance)holder.Fields[field].Value;

        [Fact]
        public void PositionalInitArguments_BindToInputsAfterRuntimeSuppliedFlags()
        {
            var registry = new TypeRegistry(new[]
            {
                GadgetSetting(),
                HolderFb("\tfbSetting : FB_GadgetSetting(1.5, 'Speed');"),
            });
            var engine = new Engine(registry);

            var gadget = Gadget(engine.NewInstance("FB_Holder"), "fbSetting");

            Assert.Equal(1.5, System.Convert.ToDouble(gadget.Fields["fValue"].Value));
            Assert.Equal("Speed", gadget.Fields["sLabel"].Value);
        }

        [Fact]
        public void NamedInitArguments_BindByName()
        {
            var registry = new TypeRegistry(new[]
            {
                GadgetSetting(),
                HolderFb("\tfbNamed : FB_GadgetSetting(ifDefault := 2.5, isLabel := 'Force');"),
            });
            var engine = new Engine(registry);

            var gadget = Gadget(engine.NewInstance("FB_Holder"), "fbNamed");

            Assert.Equal(2.5, System.Convert.ToDouble(gadget.Fields["fValue"].Value));
            Assert.Equal("Force", gadget.Fields["sLabel"].Value);
        }

        [Fact]
        public void RunSuite_SuiteVarWithInitArguments_InstanceExistsAndIsInitialized()
        {
            var suite = new PouAst(
                "FB_GadgetTests",
                "TcUnit.FB_TestSuite",
                "FUNCTION_BLOCK FB_GadgetTests EXTENDS TcUnit.FB_TestSuite\nVAR\n\tfbSetting : FB_GadgetSetting(1.5, 'Speed');\n\tfbNamed : FB_GadgetSetting(ifDefault := 2.5, isLabel := 'Force');\nEND_VAR",
                "M_Check();",
                new List<MethodAst>
                {
                    new MethodAst(
                        "M_Check",
                        "METHOD PRIVATE M_Check",
                        "TEST('M_Check');\n"
                        + "AssertEquals_LREAL(Expected := 1.5, Actual := fbSetting.fValue, Delta := 0.0001, Message := 'positional');\n"
                        + "AssertEquals_LREAL(Expected := 2.5, Actual := fbNamed.fValue, Delta := 0.0001, Message := 'named');\n"
                        + "TEST_FINISHED();"),
                });
            var engine = new Engine(new TypeRegistry(new[] { GadgetSetting(), suite }));

            var results = engine.RunSuite("FB_GadgetTests");

            var result = Assert.Single(results);
            Assert.True(result.Passed, result.ToString());
        }

        private static PouAst HolderFbRaw(string declarationBody) =>
            new PouAst("FB_Holder", null, "FUNCTION_BLOCK FB_Holder\n" + declarationBody, "", new List<MethodAst>());

        private static double FValue(FbInstance gadget) =>
            System.Convert.ToDouble(gadget.Fields["fValue"].Value);

        [Fact]
        public void InitArgument_ReadingSiblingField_SeesItsValue()
        {
            var registry = new TypeRegistry(new[]
            {
                GadgetSetting(),
                HolderFbRaw("VAR\n\tfBase : LREAL := 7.5;\n\tfbSetting : FB_GadgetSetting(fBase, 's');\nEND_VAR"),
            });

            var gadget = Gadget(new Engine(registry).NewInstance("FB_Holder"), "fbSetting");

            Assert.Equal(7.5, FValue(gadget));
        }

        [Fact]
        public void InitArgument_ReadingOwnerConstant_SeesItsValue()
        {
            var registry = new TypeRegistry(new[]
            {
                GadgetSetting(),
                HolderFbRaw("VAR CONSTANT\n\tcDefault : LREAL := 6.5;\nEND_VAR\nVAR\n\tfbSetting : FB_GadgetSetting(cDefault, 's');\nEND_VAR"),
            });

            var gadget = Gadget(new Engine(registry).NewInstance("FB_Holder"), "fbSetting");

            Assert.Equal(6.5, FValue(gadget));
        }

        [Fact]
        public void InitArgument_ReadingGvlConstant_SeesItsValue()
        {
            var gvl = new GvlAst("GVL_Limits", "VAR_GLOBAL CONSTANT\n\tcSpeed : LREAL := 9.5;\nEND_VAR");
            var registry = new TypeRegistry(
                new[] { GadgetSetting(), HolderFb("\tfbSetting : FB_GadgetSetting(GVL_Limits.cSpeed, 's');") },
                gvls: new[] { gvl });

            var gadget = Gadget(new Engine(registry).NewInstance("FB_Holder"), "fbSetting");

            Assert.Equal(9.5, FValue(gadget));
        }

        [Fact]
        public void InitArgument_InMethodLocalDeclaration_ReadsEarlierMethodLocal()
        {
            var suite = new PouAst(
                "FB_GadgetTests",
                "TcUnit.FB_TestSuite",
                "FUNCTION_BLOCK FB_GadgetTests EXTENDS TcUnit.FB_TestSuite",
                "M_Check();",
                new List<MethodAst>
                {
                    new MethodAst(
                        "M_Check",
                        "METHOD PRIVATE M_Check\nVAR\n\tfLocal : LREAL := 4.5;\n\tfbLocal : FB_GadgetSetting(fLocal, 'm');\nEND_VAR",
                        "TEST('M_Check');\nAssertEquals_LREAL(Expected := 4.5, Actual := fbLocal.fValue, Delta := 0.0001, Message := 'local');\nTEST_FINISHED();"),
                });
            var engine = new Engine(new TypeRegistry(new[] { GadgetSetting(), suite }));

            var result = Assert.Single(engine.RunSuite("FB_GadgetTests"));

            Assert.True(result.Passed, result.ToString());
        }

        // FB_init is found through the ancestry, not only on the instance's
        // own type.
        [Fact]
        public void FbInitDeclaredOnBaseOnly_ReceivesInitArguments()
        {
            var derived = new PouAst("FB_DerivedSetting", "FB_GadgetSetting", "FUNCTION_BLOCK FB_DerivedSetting EXTENDS FB_GadgetSetting", "", new List<MethodAst>());
            var registry = new TypeRegistry(new[]
            {
                GadgetSetting(),
                derived,
                HolderFb("\tfbDerived : FB_DerivedSetting(3.5, 'd');"),
            });

            var gadget = Gadget(new Engine(registry).NewInstance("FB_Holder"), "fbDerived");

            Assert.Equal(3.5, FValue(gadget));
            Assert.Equal("d", gadget.Fields["sLabel"].Value);
        }

        private static PouAst CountedFb() =>
            new PouAst(
                "FB_Counted",
                null,
                "FUNCTION_BLOCK FB_Counted\nVAR\n\tnInits : INT;\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    new MethodAst(
                        "FB_init",
                        "METHOD FB_init : BOOL\nVAR_INPUT\n\tbInitRetains : BOOL;\n\tbInCopyCode : BOOL;\nEND_VAR",
                        "nInits := nInits + 1;"),
                });

        [Theory]
        [InlineData("FB_Counted")]
        [InlineData("FB_Counted()")]
        public void FbInit_RunsExactlyOnce(string declaredType)
        {
            var registry = new TypeRegistry(new[] { CountedFb(), HolderFb("\tfbCounted : " + declaredType + ";") });

            var counted = Gadget(new Engine(registry).NewInstance("FB_Holder"), "fbCounted");

            Assert.Equal(1, System.Convert.ToInt32(counted.Fields["nInits"].Value));
        }

        // The argument list ends at its own parenthesis, so the struct-literal
        // initializer after it is not read as more arguments.
        [Fact]
        public void InitArgumentsBeforeInitializer_ConstructInstanceWithoutReadingInitializerAsArguments()
        {
            var registry = new TypeRegistry(new[]
            {
                GadgetSetting(),
                HolderFb("\tfbSetting : FB_GadgetSetting(1.5, 'Speed') := (fValue := 3.0);"),
            });

            var gadget = Gadget(new Engine(registry).NewInstance("FB_Holder"), "fbSetting");

            Assert.Equal("Speed", gadget.Fields["sLabel"].Value);
        }

        // A declaration without arguments must not have runtime flags pushed
        // into an FB_init whose first input is something else.
        [Fact]
        public void NoArguments_LeavesNonFlagFbInitInputAtItsDefault()
        {
            var oddInit = new PouAst(
                "FB_Odd",
                null,
                "FUNCTION_BLOCK FB_Odd\nVAR_OUTPUT\n\tnGot : INT;\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    new MethodAst("FB_init", "METHOD FB_init : BOOL\nVAR_INPUT\n\tnOnly : INT := 5;\nEND_VAR", "nGot := nOnly;"),
                });

            var odd = new Engine(new TypeRegistry(new[] { oddInit })).NewInstance("FB_Odd");

            Assert.Equal(5, System.Convert.ToInt32(odd.Fields["nGot"].Value));
        }

        // An argument list that cannot be bound is a fault naming the
        // instance, never a silent misbinding or a bare parser message.
        [Theory]
        [InlineData("\tfb : FB_GadgetSetting(1.5, 'p', ifDefault := 9.0);", "cannot be mixed")]
        [InlineData("\tfb : FB_GadgetSetting(1.0, *);", "cannot be read")]
        [InlineData("\tfb : FB_GadgetSetting(ifNope := 1.0);", "ifNope")]
        [InlineData("\tfb : FB_GadgetSetting(1.0, 'a', 3);", "3 were given")]
        [InlineData("\tfb : FB_NoInit(1.0);", "declare an FB_init")]
        [InlineData("\tfb : FB_ShortInit(1.0);", "bInitRetains")]
        [InlineData("\tfb : TON(PT := T#1S);", "native or plugin")]
        public void InvalidInitArguments_FaultNamingTheInstance(string declaration, string expectedFragment)
        {
            var noInit = new PouAst("FB_NoInit", null, "FUNCTION_BLOCK FB_NoInit", "", new List<MethodAst>());
            var shortInit = new PouAst(
                "FB_ShortInit",
                null,
                "FUNCTION_BLOCK FB_ShortInit",
                "",
                new List<MethodAst>
                {
                    new MethodAst("FB_init", "METHOD FB_init : BOOL\nVAR_INPUT\n\tnOnly : INT;\nEND_VAR", ""),
                });
            var registry = new TypeRegistry(new[] { GadgetSetting(), noInit, shortInit, HolderFb(declaration) });
            var engine = new Engine(registry);

            var ex = Assert.ThrowsAny<System.InvalidOperationException>(() => engine.NewInstance("FB_Holder").Fields["fb"].Value);

            Assert.Contains("Instance 'fb'", ex.Message);
            Assert.Contains(expectedFragment, ex.Message);
        }

        // Identifiers are case-insensitive in IEC 61131-3, so the method name
        // FB_Init is the same method as FB_init.
        [Fact]
        public void FbInitDeclaredWithDifferentCase_ReceivesInitArguments()
        {
            var gadget = new PouAst(
                "FB_CaseGadget",
                null,
                "FUNCTION_BLOCK FB_CaseGadget\nVAR_OUTPUT\n\tfValue : LREAL;\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    new MethodAst(
                        "FB_Init",
                        "METHOD FB_Init : BOOL\nVAR_INPUT\n\tbInitRetains : BOOL;\n\tbInCopyCode : BOOL;\n\tifDefault : LREAL;\nEND_VAR",
                        "fValue := ifDefault;"),
                });
            var registry = new TypeRegistry(new[] { gadget, HolderFb("\tfb : FB_CaseGadget(2.5);") });

            Assert.Equal(2.5, FValue(Gadget(new Engine(registry).NewInstance("FB_Holder"), "fb")));
        }

        // BindParams binds VAR_IN_OUT inputs like VAR_INPUT ones, so the
        // validation must accept the same set of names.
        [Fact]
        public void NamedInitArgument_TargetingInOutInput_IsAccepted()
        {
            var gadget = new PouAst(
                "FB_InOutGadget",
                null,
                "FUNCTION_BLOCK FB_InOutGadget\nVAR_OUTPUT\n\tfValue : LREAL;\nEND_VAR",
                "",
                new List<MethodAst>
                {
                    new MethodAst(
                        "FB_init",
                        "METHOD FB_init : BOOL\nVAR_INPUT\n\tbInitRetains : BOOL;\n\tbInCopyCode : BOOL;\nEND_VAR\nVAR_IN_OUT\n\tfShared : LREAL;\nEND_VAR",
                        "fValue := fShared;"),
                });
            var registry = new TypeRegistry(new[]
            {
                gadget,
                HolderFb("\tfSource : LREAL := 8.5;\n\tfb : FB_InOutGadget(fShared := fSource);"),
            });

            Assert.Equal(8.5, FValue(Gadget(new Engine(registry).NewInstance("FB_Holder"), "fb")));
        }

        // Empty parentheses mean no arguments, so they must not demand an
        // FB_init the FB does not have.
        [Fact]
        public void EmptyParentheses_OnFbWithoutFbInit_ConstructsInstance()
        {
            var noInit = new PouAst("FB_NoInit", null, "FUNCTION_BLOCK FB_NoInit", "", new List<MethodAst>());
            var registry = new TypeRegistry(new[] { noInit, HolderFb("\tfb : FB_NoInit();") });

            Assert.NotNull(Gadget(new Engine(registry).NewInstance("FB_Holder"), "fb"));
        }

        private static PouAst ReadingSuite(string name, string readExpression) =>
            new PouAst(
                name,
                "TcUnit.FB_TestSuite",
                "FUNCTION_BLOCK " + name + " EXTENDS TcUnit.FB_TestSuite",
                "M_Check();",
                new List<MethodAst>
                {
                    new MethodAst(
                        "M_Check",
                        "METHOD PRIVATE M_Check",
                        "TEST('M_Check');\nAssertEquals_LREAL(Expected := 1.0, Actual := " + readExpression + ", Delta := 0.0001, Message := 'read');\nTEST_FINISHED();"),
                });

        private static TypeRegistry RegistryWithBadGlobal() =>
            new TypeRegistry(
                new[] { GadgetSetting(), ReadingSuite("FB_ReadsBad", "GVL_Gadgets.gGadget.fValue"), ReadingSuite("FB_ReadsNothingBad", "1.0") },
                gvls: new[] { new GvlAst("GVL_Gadgets", "VAR_GLOBAL\n\tgGadget:FB_GadgetSetting( ifNope:=1.0 ); // note\nEND_VAR") });

        // One bad global must not take the workspace with it: the engine is
        // still built, and the fault is reported instead of thrown.
        [Fact]
        public void GlobalInstance_WithInvalidInitArguments_EngineStillConstructsAndWarnsNamingGvlAndInstance()
        {
            var engine = new Engine(RegistryWithBadGlobal());

            var warning = Assert.Single(engine.GlobalInitWarnings);
            Assert.Equal("GVL_Gadgets", warning.FileKey);
            var line = Assert.Single(warning.Lines);
            Assert.Equal("gGadget:FB_GadgetSetting( ifNope:=1.0 );", line);
            var rejection = Assert.Single(warning.Rejections);
            Assert.Equal(line, rejection.Line);
            Assert.Contains("Instance 'gGadget'", rejection.Reason);
            Assert.Contains("ifNope", rejection.Reason);
        }

        // The fault belongs to whoever reads the global, and it must be the
        // precise reason rather than a null-global access error.
        [Fact]
        public void RunSuite_ReadingFaultedGlobal_FailsWithThePreciseMessage()
        {
            var engine = new Engine(RegistryWithBadGlobal());

            var result = Assert.Single(engine.RunSuite("FB_ReadsBad"));

            Assert.False(result.Passed);
            var text = result.ToString();
            Assert.Contains("Instance 'gGadget'", text);
            Assert.Contains("ifNope", text);
            Assert.DoesNotContain("Cannot access fields", text);
        }

        // Fault-site data is stamped onto the exception as it unwinds. Each
        // read of the faulted global must therefore raise its own exception,
        // or a later suite would be told it failed in an earlier one, with a
        // call stack that grows with every reader.
        [Fact]
        public void RunSuite_TwoSuitesReadingSameFaultedGlobal_EachReportsItsOwnSite()
        {
            var registry = new TypeRegistry(
                new[]
                {
                    GadgetSetting(),
                    ReadingSuite("FB_AlphaTests", "GVL_Gadgets.gGadget.fValue"),
                    ReadingSuite("FB_BetaTests", "GVL_Gadgets.gGadget.fValue"),
                },
                gvls: new[] { new GvlAst("GVL_Gadgets", "VAR_GLOBAL\n\tgGadget : FB_GadgetSetting(ifNope := 1.0);\nEND_VAR") });
            var engine = new Engine(registry);

            var alpha = Assert.Single(Assert.Single(engine.RunSuite("FB_AlphaTests")).Failures);
            var beta = Assert.Single(Assert.Single(engine.RunSuite("FB_BetaTests")).Failures);

            Assert.Equal("FB_AlphaTests", alpha.Site.PouTypeName);
            Assert.Equal("FB_BetaTests", beta.Site.PouTypeName);
            Assert.Equal("M_Check", beta.Site.MethodName);
            Assert.Equal(alpha.Site.Line, beta.Site.Line);
            Assert.Equal(alpha.CallStack.Count, beta.CallStack.Count);
            Assert.All(beta.CallStack, frame => Assert.NotEqual("FB_AlphaTests", frame.PouTypeName));
        }

        [Fact]
        public void RunSuite_NotReadingFaultedGlobal_Passes()
        {
            var engine = new Engine(RegistryWithBadGlobal());

            var result = Assert.Single(engine.RunSuite("FB_ReadsNothingBad"));

            Assert.True(result.Passed, result.ToString());
        }

        // Each independently bad global is its own finding; reporting only the
        // first would make the user fix and rerun once per global.
        [Fact]
        public void GlobalInstances_WithInvalidInitArguments_AreAllReported()
        {
            var gvl = new GvlAst(
                "GVL_Gadgets",
                "VAR_GLOBAL\n\tgOne : FB_GadgetSetting(ifNope := 1.0);\n\tgTwo : FB_GadgetSetting(1.0, 'a', 3);\nEND_VAR");
            var engine = new Engine(new TypeRegistry(new[] { GadgetSetting() }, gvls: new[] { gvl }));

            Assert.Equal(2, engine.GlobalInitWarnings.Count);
        }

        // Only the global whose own arguments are bad is reported. One that
        // faults merely because it read the bad one stays faulted with the
        // root's message but adds no warning of its own, or the user would be
        // sent to fix a declaration that is fine.
        [Theory]
        [InlineData("gRoot")]
        [InlineData("gDependent")]
        public void DependentGlobal_OfFaultedGlobal_IsNotReportedSeparatelyAndReadsRootMessage(string readName)
        {
            var gvl = new GvlAst(
                "GVL_Gadgets",
                "VAR_GLOBAL\n\tgRoot : FB_GadgetSetting(ifNope := 1.0);\n\tgDependent : FB_GadgetSetting(gRoot.fValue, 'd');\nEND_VAR");
            var suite = ReadingSuite("FB_ReadsGlobal", "GVL_Gadgets." + readName + ".fValue");
            var engine = new Engine(new TypeRegistry(new[] { GadgetSetting(), suite }, gvls: new[] { gvl }));

            var warning = Assert.Single(engine.GlobalInitWarnings);
            Assert.Contains("gRoot", Assert.Single(warning.Lines));

            var result = Assert.Single(engine.RunSuite("FB_ReadsGlobal"));
            Assert.False(result.Passed);
            Assert.Contains("Instance 'gRoot'", result.ToString());
        }

        [Fact]
        public void GlobalInstance_WithInitArguments_IsInitialized()
        {
            var gvl = new GvlAst("GVL_Gadgets", "VAR_GLOBAL\n\tgGadget : FB_GadgetSetting(4.5, 'g');\nEND_VAR");
            var registry = new TypeRegistry(new[] { GadgetSetting() }, gvls: new[] { gvl });
            var engine = new Engine(registry);

            var value = engine.Evaluate(Parser.ParseExpression("GVL_Gadgets.gGadget.fValue"), new Frame(null, "Test"));

            Assert.Equal(4.5, System.Convert.ToDouble(value));
        }
    }
}
