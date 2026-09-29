using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    public class GvlGlobalsTests
    {
        // Assignment never creates a variable, so the scratch variable the
        // statements write their observation into is declared up front.
        private static Frame NewFrame(FbInstance instance)
        {
            var frame = new Frame(instance, "FB_Suite");
            frame.Locals["result"] = new Cell();
            return frame;
        }

        private static Engine NewEngine(string implementation, IReadOnlyList<GvlAst> gvls, IReadOnlyList<StructAst> structs = null)
        {
            var suite = new PouAst(
                "FB_Suite",
                null,
                "VAR\nEND_VAR",
                implementation,
                new List<MethodAst>());

            return new Engine(new TypeRegistry(new[] { suite }, structs, gvls));
        }

        [Fact]
        public void QualifiedRead_ScalarGvlMember_ReturnsZeroInitializedDefault()
        {
            var gvl = new GvlAst("gCounters", "VAR_GLOBAL\n\tcount : INT;\nEND_VAR");
            var engine = NewEngine("result := gCounters.count;", new[] { gvl });
            var instance = engine.NewInstance("FB_Suite");
            var frame = NewFrame(instance);

            engine.ExecuteStatements(Parser.ParseStatements("result := gCounters.count;"), frame);

            Assert.Equal(0, frame.Locals["result"].Value);
        }

        [Fact]
        public void QualifiedWrite_ScalarGvlMember_PersistsAcrossStatements()
        {
            var gvl = new GvlAst("gCounters", "VAR_GLOBAL\n\tcount : INT;\nEND_VAR");
            var engine = NewEngine("", new[] { gvl });
            var instance = engine.NewInstance("FB_Suite");
            var frame = NewFrame(instance);

            engine.ExecuteStatements(Parser.ParseStatements("gCounters.count := 5;\nresult := gCounters.count + 1;"), frame);

            Assert.Equal(6, frame.Locals["result"].Value);
        }

        [Fact]
        public void QualifiedWrite_ConstantModifierGvl_StillResolvesAndCanBeRead()
        {
            var gvl = new GvlAst("cScratchConstants", "VAR_GLOBAL CONSTANT\n\tMAX_UNITS : UINT := 16;\nEND_VAR");
            var engine = NewEngine("", new[] { gvl });
            var instance = engine.NewInstance("FB_Suite");
            var frame = NewFrame(instance);

            engine.ExecuteStatements(Parser.ParseStatements("result := cScratchConstants.MAX_UNITS;"), frame);

            Assert.Equal(16, frame.Locals["result"].Value);
        }

        [Fact]
        public void QualifiedRead_StructTypedGvlMember_ZeroInitializesDeclaredFields()
        {
            var structAst = new StructAst("uWidget", new[]
            {
                new VarDecl("state", "INT", null, VarSection.Local),
            });
            var gvl = new GvlAst("gScratchGlobals", "VAR_GLOBAL\n\tstWidget : uWidget;\nEND_VAR");
            var engine = NewEngine("", new[] { gvl }, new[] { structAst });
            var instance = engine.NewInstance("FB_Suite");
            var frame = NewFrame(instance);

            engine.ExecuteStatements(Parser.ParseStatements("result := gScratchGlobals.stWidget.state;"), frame);

            Assert.Equal(0, frame.Locals["result"].Value);
        }

        [Fact]
        public void RefAssign_ToGvlStructMember_AliasesTheSameCellAsQualifiedAccess()
        {
            var structAst = new StructAst("uWidget", new[]
            {
                new VarDecl("state", "INT", null, VarSection.Local),
            });
            var gvl = new GvlAst("gScratchGlobals", "VAR_GLOBAL\n\tstWidget : uWidget;\nEND_VAR");
            var engine = NewEngine("", new[] { gvl }, new[] { structAst });
            var instance = engine.NewInstance("FB_Suite");
            var frame = NewFrame(instance);

            frame.Locals["sstWidget"] = new Cell();
            engine.ExecuteStatements(Parser.ParseStatements(
                "sstWidget REF= gScratchGlobals.stWidget;\ngScratchGlobals.stWidget.state := 7;\nresult := sstWidget.state;"), frame);

            Assert.Equal(7, frame.Locals["result"].Value);
        }

        [Fact]
        public void UnqualifiedRead_GvlConstant_ResolvesWithoutGvlPrefix()
        {
            var gvl = new GvlAst("cScratchConstants", "VAR_GLOBAL CONSTANT\n\tMAX_UNITS : UINT := 16;\nEND_VAR");
            var engine = NewEngine("", new[] { gvl });
            var instance = engine.NewInstance("FB_Suite");
            var frame = NewFrame(instance);

            engine.ExecuteStatements(Parser.ParseStatements("result := MAX_UNITS;"), frame);

            Assert.Equal(16, frame.Locals["result"].Value);
        }

        // GVL default values are evaluated eagerly while the globals are still
        // being built, so a cross-GVL reference has to resolve against a
        // half-populated table rather than a finished one.
        [Fact]
        public void UnqualifiedRead_GvlConstant_ResolvesFromAnotherGvlDuringConstruction()
        {
            var cGvl = new GvlAst("cScratchConstants", "VAR_GLOBAL CONSTANT\n\tTCP_MESSAGE_SIZE : UINT := 16;\nEND_VAR");
            var gGvl = new GvlAst("gScratchGlobals", "VAR_GLOBAL\n\tbufferSize : UINT := TCP_MESSAGE_SIZE;\nEND_VAR");
            var engine = NewEngine("", new[] { cGvl, gGvl });
            var instance = engine.NewInstance("FB_Suite");
            var frame = NewFrame(instance);

            engine.ExecuteStatements(Parser.ParseStatements("result := gScratchGlobals.bufferSize;"), frame);

            Assert.Equal(16, frame.Locals["result"].Value);
        }

        // The same case with the GVLs declared the other way round, so the
        // referencing GVL is registered BEFORE the one defining the constant.
        // Not redundant with the test above: an implementation that resolves
        // eagerly in registration order passes that one and fails this one.
        [Fact]
        public void UnqualifiedRead_GvlConstant_ResolvesRegardlessOfGvlRegistrationOrder()
        {
            var gGvl = new GvlAst("gScratchGlobals", "VAR_GLOBAL\n\tbufferSize : UINT := TCP_MESSAGE_SIZE;\nEND_VAR");
            var cGvl = new GvlAst("cScratchConstants", "VAR_GLOBAL CONSTANT\n\tTCP_MESSAGE_SIZE : UINT := 16;\nEND_VAR");
            var engine = NewEngine("", new[] { gGvl, cGvl });
            var instance = engine.NewInstance("FB_Suite");
            var frame = NewFrame(instance);

            engine.ExecuteStatements(Parser.ParseStatements("result := gScratchGlobals.bufferSize;"), frame);

            Assert.Equal(16, frame.Locals["result"].Value);
        }

        // Settling a global's default takes one pass per link in the chain it
        // depends on, so a chain longer than the one or two links a hand-built
        // fixture usually has is the case separating "iterate until nothing
        // new settles" from "iterate a fixed couple of times". Declared back
        // to front as well, so no link can settle by luck of registration
        // order.
        [Fact]
        public void UnqualifiedRead_ChainOfGvlConstants_ResolvesEveryLinkRegardlessOfDepth()
        {
            var gvls = new[]
            {
                new GvlAst("cLevel4", "VAR_GLOBAL CONSTANT\n\tLEVEL_4 : UINT := LEVEL_3 + 1;\nEND_VAR"),
                new GvlAst("cLevel3", "VAR_GLOBAL CONSTANT\n\tLEVEL_3 : UINT := LEVEL_2 + 1;\nEND_VAR"),
                new GvlAst("cLevel2", "VAR_GLOBAL CONSTANT\n\tLEVEL_2 : UINT := LEVEL_1 + 1;\nEND_VAR"),
                new GvlAst("cLevel1", "VAR_GLOBAL CONSTANT\n\tLEVEL_1 : UINT := LEVEL_0 + 1;\nEND_VAR"),
                new GvlAst("cLevel0", "VAR_GLOBAL CONSTANT\n\tLEVEL_0 : UINT := 10;\nEND_VAR"),
            };
            var engine = NewEngine("", gvls);
            var instance = engine.NewInstance("FB_Suite");
            var frame = NewFrame(instance);

            engine.ExecuteStatements(Parser.ParseStatements("result := cLevel4.LEVEL_4;"), frame);

            Assert.Equal(14, frame.Locals["result"].Value);
        }

        // An ARRAY bound is settled by the same loop as the value, but reaches
        // the constant through the type text rather than an initializer
        // expression. A global sized by a constant a later GVL declares has to
        // wait for it just the same, and settling early is silent here: the
        // array simply comes out the length the constant read as before it
        // had one, so the top index is what catches it.
        [Fact]
        public void QualifiedRead_GvlArraySizedByLaterGvlConstant_BuildsArrayOfDeclaredLength()
        {
            var gvls = new[]
            {
                new GvlAst("gScratchGlobals", "VAR_GLOBAL\n\taUnits : ARRAY[1..cScratchConstants.MAX_UNITS] OF INT;\nEND_VAR"),
                new GvlAst("cScratchConstants", "VAR_GLOBAL CONSTANT\n\tMAX_UNITS : UINT := 12;\nEND_VAR"),
            };
            var engine = NewEngine("", gvls);
            var instance = engine.NewInstance("FB_Suite");
            var frame = NewFrame(instance);

            engine.ExecuteStatements(Parser.ParseStatements("result := gScratchGlobals.aUnits[12];"), frame);

            Assert.Equal(0, frame.Locals["result"].Value);
        }

        // STRING capacity settles through the same loop and is equally silent
        // when it settles early: an unresolved size seats an Unbounded cell,
        // which keeps a value longer than the declaration instead of dropping
        // what does not fit the way TwinCAT does.
        [Fact]
        public void QualifiedWrite_GvlStringSizedByLaterGvlConstant_ClampsToDeclaredCapacity()
        {
            var gvls = new[]
            {
                new GvlAst("gScratchGlobals", "VAR_GLOBAL\n\tsLabel : STRING(cScratchConstants.LABEL_LEN);\nEND_VAR"),
                new GvlAst("cScratchConstants", "VAR_GLOBAL CONSTANT\n\tLABEL_LEN : UINT := 4;\nEND_VAR"),
            };
            var engine = NewEngine("", gvls);
            var instance = engine.NewInstance("FB_Suite");
            var frame = NewFrame(instance);

            engine.ExecuteStatements(Parser.ParseStatements(
                "gScratchGlobals.sLabel := 'abcdefgh';\nresult := gScratchGlobals.sLabel;"), frame);

            Assert.Equal("abcd", frame.Locals["result"].Value);
        }

        // An ARRAY bound is any IEC 61131-3 constant expression, not just an
        // integer literal, so a GVL-qualified constant is legal there and has
        // to go through the GVL lookup instead of being parsed as a number.
        [Fact]
        public void NewInstance_ArrayFieldBoundByGvlQualifiedConstant_BuildsArrayOfDeclaredLength()
        {
            var gvl = new GvlAst("cRemoteClientConfig", "VAR_GLOBAL CONSTANT\n\tMAX_REMOTE_ITEMS : UINT := 10;\nEND_VAR");
            var fb = new PouAst(
                "FB_Holder",
                null,
                "VAR\n\taUnits : ARRAY[1..cRemoteClientConfig.MAX_REMOTE_ITEMS] OF INT;\nEND_VAR",
                "",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }, null, new[] { gvl }));

            var instance = engine.NewInstance("FB_Holder");
            var aUnits = Assert.IsType<ArrayValue>(instance.Fields["aUnits"].Value);

            Assert.Equal(10, aUnits.Elements.Length);
            Assert.All(aUnits.Elements, e => Assert.Equal(0, e));
        }

        // Same shape one level down, where the array is a DUT field: struct
        // defaults are built by a separate path from an FB's own fields, and
        // fixing only the FB path leaves this one throwing.
        [Fact]
        public void NewInstance_StructFieldArrayBoundByGvlQualifiedConstant_BuildsArrayOfDeclaredLength()
        {
            var gvl = new GvlAst("cRemoteClientConfig", "VAR_GLOBAL CONSTANT\n\tMAX_REMOTE_ITEMS : UINT := 10;\nEND_VAR");
            var structAst = new StructAst("uRemoteItemSet", new[]
            {
                new VarDecl("aItems", "ARRAY[1..cRemoteClientConfig.MAX_REMOTE_ITEMS] OF INT", null, VarSection.Local),
            });
            var fb = new PouAst("FB_Holder", null, "VAR\n\titem : uRemoteItemSet;\nEND_VAR", "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }, new[] { structAst }, new[] { gvl }));

            var instance = engine.NewInstance("FB_Holder");
            var item = Assert.IsType<StructInstance>(instance.Fields["item"].Value);
            var aItems = Assert.IsType<ArrayValue>(item.Fields["aItems"].Value);

            Assert.Equal(10, aItems.Elements.Length);
        }

        // One unresolvable default must cost only its own declaration. A
        // real project routinely references library constants this loader
        // knows nothing about, and failing the whole Engine construction over
        // one of them would make every suite in the project undiscoverable.
        [Fact]
        public void ConstructionResilience_BadGvlDefaultDoesNotBlockOtherGvls()
        {
            var badGvl = new GvlAst("gBad", "VAR_GLOBAL\n\tbroken : UINT := NOT_A_REAL_CONSTANT;\nEND_VAR");
            var goodGvl = new GvlAst("gGood", "VAR_GLOBAL\n\tcount : INT := 5;\nEND_VAR");
            var engine = NewEngine("", new[] { badGvl, goodGvl });
            var instance = engine.NewInstance("FB_Suite");
            var frame = NewFrame(instance);

            engine.ExecuteStatements(Parser.ParseStatements("result := gGood.count;"), frame);

            Assert.Equal(5, frame.Locals["result"].Value);
        }
    }
}
