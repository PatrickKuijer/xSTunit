using System.Collections.Generic;
using System.Linq;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // POINTER TO ARRAY[..] OF x, REFERENCE TO ARRAY[..] OF x, and REFERENCE TO
    // a sized STRING all parse (VarBlockParserTests) but were previously
    // unreachable at runtime: a POU declaring one had its variable dropped
    // silently, so any body reading it failed with "Unknown variable" instead.
    // These pin the runtime behaviour a parsed declaration of each shape now
    // has to deliver - not just that the line is read, but that a body using
    // it actually works.
    public class AddressToArrayOrSizedStringTests
    {
        private static (Engine Engine, FbInstance Instance, Frame Frame) NewHolder(
            string varBlock, IEnumerable<GvlAst> gvls = null, IEnumerable<StructAst> structTypes = null)
        {
            var fb = new PouAst("FB_Holder", null, varBlock, "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }, structTypes, gvls));
            var instance = engine.NewInstance("FB_Holder");
            return (engine, instance, new Frame(instance, "FB_Holder"));
        }

        // A step-timer FB's history-buffer shape: a POINTER TO ARRAY
        // VAR_INPUT, guarded by '<> 0' and written through with '^[i]'.
        [Fact]
        public void PointerToArray_AdrThenDerefIndex_ReadsAndWritesTheBackingArray()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\tbuf : ARRAY[0..3] OF INT := [10, 20, 30, 40];\n\tipHistory : POINTER TO ARRAY[0..3] OF INT;\nEND_VAR");

            engine.ExecuteStatements(Parser.ParseStatements("ipHistory := ADR(buf);"), frame);

            Assert.True((bool)engine.Evaluate(Parser.ParseExpression("ipHistory <> 0"), frame));
            Assert.True((bool)engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(ipHistory)"), frame));
            Assert.Equal(30, engine.Evaluate(Parser.ParseExpression("ipHistory^[2]"), frame));

            engine.ExecuteStatements(Parser.ParseStatements("ipHistory^[0] := 99;"), frame);

            var buf = (ArrayValue)instance.Fields["buf"].Value;
            Assert.Equal(99, buf.Elements[0]);
            Assert.Equal(99, engine.Evaluate(Parser.ParseExpression("ipHistory^[0]"), frame));
        }

        // The standard history-buffer shift: MEMMOVE everything one element
        // towards the end, sized with SIZEOF off the pointer's own declaration,
        // then write the newest entry at the front. If either SIZEOF is wrong
        // the shift lands off-element and the history reads back scrambled.
        [Fact]
        public void PointerToArray_MemmoveShiftSizedBySizeOf_KeepsTheNewestStepFirst()
        {
            var guard = new PouAst(
                "FB_ProcessGuard",
                null,
                "FUNCTION_BLOCK FB_ProcessGuard\nVAR_INPUT\n\tinStep : INT;\n" +
                "\tipHistory : POINTER TO ARRAY[0..20] OF INT;\nEND_VAR",
                "IF ipHistory <> 0 THEN\n" +
                "\tMEMMOVE( destAddr := ipHistory + SIZEOF(ipHistory^[0]),\n" +
                "\t\tsrcAddr := ipHistory,\n" +
                "\t\tn := SIZEOF(ipHistory^) - SIZEOF(ipHistory^[0]) );\n" +
                "\tipHistory^[0] := inStep;\n" +
                "END_IF",
                new List<MethodAst>());
            var caller = new PouAst(
                "FB_Caller",
                null,
                "VAR\n\tsaHistory : ARRAY[0..20] OF INT;\n\tfbGuard : FB_ProcessGuard;\nEND_VAR",
                "",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { caller, guard }));
            var instance = engine.NewInstance("FB_Caller");
            var frame = new Frame(instance, "FB_Caller");

            engine.ExecuteStatements(Parser.ParseStatements(
                "fbGuard(inStep := 1, ipHistory := ADR(saHistory));\n" +
                "fbGuard(inStep := 2, ipHistory := ADR(saHistory));\n" +
                "fbGuard(inStep := 3, ipHistory := ADR(saHistory));"), frame);

            var history = (ArrayValue)instance.Fields["saHistory"].Value;
            Assert.Equal(new object[] { 3, 2, 1, 0 }, history.Elements.Take(4).ToArray());
        }

        [Fact]
        public void PointerToArray_Unbound_ComparesEqualToZeroAndIsNotAValidRef()
        {
            var (engine, _, frame) = NewHolder(
                "VAR\n\tbuf : ARRAY[0..3] OF INT;\n\tipHistory : POINTER TO ARRAY[0..3] OF INT;\nEND_VAR");

            Assert.False((bool)engine.Evaluate(Parser.ParseExpression("ipHistory <> 0"), frame));
            Assert.False((bool)engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(ipHistory)"), frame));
        }

        // A combine-style FUNCTION's shape: a REFERENCE TO ARRAY OF an
        // INTERFACE VAR_INPUT, which must alias the caller's own array (not
        // copy it) so a call through an element reaches the caller's instance.
        [Fact]
        public void ReferenceToArrayOfInterfaceType_AliasesCallerArray_AndIndexesThroughToInterfaceCalls()
        {
            var partInterface = new InterfaceAst(
                "ITF_Part",
                "INTERFACE ITF_Part",
                new List<MethodAst> { new MethodAst("GetValue", "METHOD GetValue : INT", string.Empty) },
                new List<PropertyAst>());

            var partImpl = new PouAst(
                "FB_Part",
                null,
                "FUNCTION_BLOCK FB_Part\nVAR\n\tValue : INT;\nEND_VAR",
                "",
                new List<MethodAst> { new MethodAst("GetValue", "METHOD GetValue : INT", "GetValue := Value;") });

            var combine = new PouAst(
                "F_CombineParts",
                null,
                "FUNCTION F_CombineParts : INT\nVAR_INPUT\n\tiaParts : REFERENCE TO ARRAY[1..2] OF ITF_Part;\nEND_VAR",
                "IF __ISVALIDREF(iaParts) THEN\n\tF_CombineParts := iaParts[1].GetValue() + iaParts[2].GetValue();\nEND_IF",
                new List<MethodAst>());

            var caller = new PouAst(
                "FB_Caller",
                null,
                "VAR\n\tparts : ARRAY[1..2] OF ITF_Part;\n\tp1 : FB_Part;\n\tp2 : FB_Part;\n\tresult : INT;\nEND_VAR",
                "",
                new List<MethodAst>());

            var registry = new TypeRegistry(
                new[] { caller, combine, partImpl }, interfaceTypes: new[] { partInterface });
            var engine = new Engine(registry);
            var instance = engine.NewInstance("FB_Caller");
            var frame = new Frame(instance, "FB_Caller");

            engine.ExecuteStatements(Parser.ParseStatements(
                "p1.Value := 3;\np2.Value := 4;\nparts[1] := p1;\nparts[2] := p2;\n" +
                "result := F_CombineParts(parts);"), frame);

            Assert.Equal(7, instance.Fields["result"].Value);
        }

        // A REFERENCE TO ARRAY bound by a qualified GVL constant rather than
        // a literal, of a DUT element type (uGadgetSettingValue's own shape),
        // bound via REF= and read back through the alias.
        [Fact]
        public void ReferenceToArrayBoundByQualifiedGvlConstant_OfDutElementType_RefAssignThenIndexes()
        {
            var gvl = new GvlAst("cScratchConstants", "VAR_GLOBAL CONSTANT\n\tMAX_GADGET_SETTINGS : UINT := 3;\nEND_VAR");
            var settingStruct = new StructAst(
                "uGadgetSettingValue",
                new List<VarDecl> { new VarDecl("Value", "INT", null, VarSection.Local) });
            var (engine, _, frame) = NewHolder(
                "VAR\n\tsettings : ARRAY[1..3] OF uGadgetSettingValue;\n" +
                "\tiaSettings : REFERENCE TO ARRAY[1..cScratchConstants.MAX_GADGET_SETTINGS] OF uGadgetSettingValue;\nEND_VAR",
                new[] { gvl },
                new[] { settingStruct });

            Assert.False((bool)engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(iaSettings)"), frame));

            engine.ExecuteStatements(Parser.ParseStatements(
                "settings[2].Value := 42;\niaSettings REF= settings;"), frame);

            Assert.True((bool)engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(iaSettings)"), frame));
            Assert.Equal(42, engine.Evaluate(Parser.ParseExpression("iaSettings[2].Value"), frame));
        }

        [Fact]
        public void ReferenceToSizedString_RefAssignThenReadsAndWritesThroughToTheTarget()
        {
            var (engine, instance, frame) = NewHolder(
                "VAR\n\ttarget : STRING(32) := 'hello';\n\tisUtc : REFERENCE TO STRING(32);\nEND_VAR");

            Assert.False((bool)engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(isUtc)"), frame));

            engine.ExecuteStatements(Parser.ParseStatements("isUtc REF= target;"), frame);

            Assert.True((bool)engine.Evaluate(Parser.ParseExpression("__ISVALIDREF(isUtc)"), frame));
            Assert.Equal("hello", engine.Evaluate(Parser.ParseExpression("isUtc"), frame));

            engine.ExecuteStatements(Parser.ParseStatements("isUtc := 'world';"), frame);

            Assert.Equal("world", instance.Fields["target"].Value);
        }
    }
}
