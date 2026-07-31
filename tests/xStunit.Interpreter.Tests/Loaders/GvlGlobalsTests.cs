using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    public class GvlGlobalsTests
    {
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
            var frame = new Frame(instance, "FB_Suite");

            engine.ExecuteStatements(Parser.ParseStatements("result := gCounters.count;"), frame);

            Assert.Equal(0, frame.Locals["result"].Value);
        }

        [Fact]
        public void QualifiedWrite_ScalarGvlMember_PersistsAcrossStatements()
        {
            var gvl = new GvlAst("gCounters", "VAR_GLOBAL\n\tcount : INT;\nEND_VAR");
            var engine = NewEngine("", new[] { gvl });
            var instance = engine.NewInstance("FB_Suite");
            var frame = new Frame(instance, "FB_Suite");

            engine.ExecuteStatements(Parser.ParseStatements("gCounters.count := 5;\nresult := gCounters.count + 1;"), frame);

            Assert.Equal(6, frame.Locals["result"].Value);
        }

        [Fact]
        public void QualifiedWrite_ConstantModifierGvl_StillResolvesAndCanBeRead()
        {
            var gvl = new GvlAst("cScratchConstants", "VAR_GLOBAL CONSTANT\n\tMAX_UNITS : UINT := 16;\nEND_VAR");
            var engine = NewEngine("", new[] { gvl });
            var instance = engine.NewInstance("FB_Suite");
            var frame = new Frame(instance, "FB_Suite");

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
            var frame = new Frame(instance, "FB_Suite");

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
            var frame = new Frame(instance, "FB_Suite");

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
            var frame = new Frame(instance, "FB_Suite");

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
            var frame = new Frame(instance, "FB_Suite");

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
            var frame = new Frame(instance, "FB_Suite");

            engine.ExecuteStatements(Parser.ParseStatements("result := gScratchGlobals.bufferSize;"), frame);

            Assert.Equal(16, frame.Locals["result"].Value);
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
            var frame = new Frame(instance, "FB_Suite");

            engine.ExecuteStatements(Parser.ParseStatements("result := gGood.count;"), frame);

            Assert.Equal(5, frame.Locals["result"].Value);
        }
    }
}
