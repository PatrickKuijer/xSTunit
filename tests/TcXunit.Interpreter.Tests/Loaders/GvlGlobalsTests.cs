using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-71o: GVL-qualified globals (GvlName.field) must resolve as
    // lvalues/rvalues anywhere in interpreted ST, backed by zero-initialized
    // storage of the declared type, and support REF= to a GVL member -
    // mirrors a common FB_init REF= pattern seen in real PLC code doing
    // "sstWidget REF= gScratchGlobals.stWidget" unconditionally.
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

        // TcXunit-09s: an unqualified (bare) reference to a GVL constant
        // must resolve via the same _globals lookup as GvlName.Member,
        // not just via frame.ResolveCell.
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

        // Mirrors the reported repro: one GVL's default-value expression
        // references another GVL's constant unqualified, evaluated eagerly
        // in Engine's constructor while building _globals.
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

        // Same as above but declared in the opposite GVL order, so the
        // referencing GVL is registered before the GVL that defines the
        // constant it depends on.
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

        // TcXunit-654: ARRAY bounds are IEC 61131-3 constant expressions, so
        // a GVL-qualified constant (e.g. cRemoteClientConfig.MAX_REMOTE_ITEMS,
        // mirroring the reported repro) is legal as a bound and must resolve
        // through the normal GVL lookup rather than crashing ArrayTypeInfo's
        // raw int.Parse with a FormatException.
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

        // Same shape, but the array is a STRUCT field (matching the reported
        // repro's BuildStructDefault call chain: a DUT field whose array
        // bound is a GVL-qualified constant, defaulted while building an
        // enclosing struct's defaults rather than an FB's own fields).
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

        // A single GVL whose default-value expression throws (unresolvable
        // reference) must not prevent other GVLs from being constructed -
        // Engine's constructor should skip just the bad GVL/decl.
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
