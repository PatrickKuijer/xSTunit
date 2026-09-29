using System.Collections.Generic;
using System.Linq;
using xStunit.Parser;
using xStunit.Runner.TcUnitStub;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    public class UndeclaredAssignmentTests
    {
        private static readonly StructAst PairStruct = new StructAst(
            "ST_Pair",
            new[] { new VarDecl("nA", "DINT", null, VarSection.Local) });

        private static PouAst Suite(string body, string vars = "VAR\n\tn : DINT;\nEND_VAR", params MethodAst[] methods) =>
            new PouAst("FB_MySuite", "TcUnit.FB_TestSuite", vars, body, methods.ToList());

        private static TestCaseResult RunSingle(PouAst suite, GvlAst[] gvls = null, StructAst[] structs = null)
        {
            var registry = new TypeRegistry(new[] { suite }, structs ?? new StructAst[0], gvls: gvls);
            return Assert.Single(new Engine(registry).RunSuite("FB_MySuite"));
        }

        private static string Failures(TestCaseResult result) =>
            string.Join("; ", result.Failures.Select(f => f.Message));

        // An assignment must never create a variable: TwinCAT rejects it at
        // compile time, and a lost declaration would otherwise go unnoticed.
        [Fact]
        public void Assignment_ToNameNoScopeDeclares_FailsTestNamingTheVariable()
        {
            var result = RunSingle(Suite("TEST('Undeclared');\nzzNeverDeclared := 2;\nTEST_FINISHED();"));

            var failure = Assert.Single(result.Failures);
            Assert.Contains("zzNeverDeclared", failure.Message);
            Assert.Equal(2, failure.Site.BodyLine);
        }

        // The undeclared-read diagnostic and the undeclared-write diagnostic
        // must both name the variable.
        [Fact]
        public void Read_OfUndeclaredName_StillFailsNamingTheVariable()
        {
            var result = RunSingle(Suite("TEST('r');\nn := zzMissing;\nTEST_FINISHED();"));

            Assert.False(result.Passed);
            Assert.Contains("zzMissing", Failures(result));
        }

        // A bare GVL member is visible everywhere; writing it must hit the
        // global cell, not a shadowing local and not the undeclared error.
        [Fact]
        public void Assignment_ToBareGvlMember_WritesTheGlobalSeenFromAnotherScope()
        {
            var gvl = new GvlAst("GVL_Machine", "VAR_GLOBAL\n\tnShared : DINT;\nEND_VAR");
            var suite = Suite(
                "TEST('g');\nnShared := 7;\nn := M_Read();\nAssertEquals_DINT( Expected := 7, Actual := n, Message := 'x' );\n" +
                "AssertEquals_DINT( Expected := 7, Actual := GVL_Machine.nShared, Message := 'y' );\nTEST_FINISHED();",
                "VAR\n\tn : DINT;\nEND_VAR",
                new MethodAst("M_Read", "METHOD M_Read : DINT", "M_Read := nShared;"));

            var result = RunSingle(suite, new[] { gvl });

            Assert.True(result.Passed, Failures(result));
        }

        // A callable's own name must resolve as an assignment target even when
        // its return type has no default value to seed, so the first whole-value
        // write to a struct return variable stays legal.
        [Fact]
        public void Method_ReturningStruct_CanAssignItsReturnVariable()
        {
            var suite = Suite(
                "TEST('s');\nn := M_Make().nA;\nAssertEquals_DINT( Expected := 5, Actual := n, Message := 'x' );\nTEST_FINISHED();",
                "VAR\n\tn : DINT;\n\tstLocal : ST_Pair;\nEND_VAR",
                new MethodAst("M_Make", "METHOD M_Make : ST_Pair\nVAR\n\tstTmp : ST_Pair;\nEND_VAR", "stTmp.nA := 5;\nM_Make := stTmp;"));

            var result = RunSingle(suite, structs: new[] { PairStruct });

            Assert.True(result.Passed, Failures(result));
        }

        [Fact]
        public void Function_ReturningStruct_CanAssignItsReturnVariable()
        {
            var function = new PouAst(
                "F_Make",
                null,
                "FUNCTION F_Make : ST_Pair\nVAR\n\tstTmp : ST_Pair;\nEND_VAR",
                "stTmp.nA := 6;\nF_Make := stTmp;",
                new List<MethodAst>());
            var suite = Suite("TEST('s');\nn := F_Make().nA;\nAssertEquals_DINT( Expected := 6, Actual := n, Message := 'x' );\nTEST_FINISHED();");
            var registry = new TypeRegistry(new[] { suite, function }, new[] { PairStruct });

            var result = Assert.Single(new Engine(registry).RunSuite("FB_MySuite"));

            Assert.True(result.Passed, Failures(result));
        }

        // A setter's own name must be writable and readable as the incoming value.
        [Fact]
        public void PropertySetter_ReadsItsOwnNameAsTheIncomingValue()
        {
            var widget = new PouAst(
                "FB_Widget",
                null,
                "VAR\n\tnStore : DINT;\nEND_VAR",
                "",
                new List<MethodAst>(),
                new List<PropertyAst>
                {
                    new PropertyAst("nValue", "PROPERTY nValue : DINT", "nValue := nStore;", "nStore := nValue;"),
                });
            var suite = Suite(
                "TEST('p');\nfbW.nValue := 9;\nn := fbW.nValue;\nAssertEquals_DINT( Expected := 9, Actual := n, Message := 'x' );\nTEST_FINISHED();",
                "VAR\n\tn : DINT;\n\tfbW : FB_Widget;\nEND_VAR");
            var registry = new TypeRegistry(new[] { suite, widget });

            var result = Assert.Single(new Engine(registry).RunSuite("FB_MySuite"));

            Assert.True(result.Passed, Failures(result));
        }

        // A getter whose return type has no seedable default must still accept
        // the first whole-value write to its own name.
        [Fact]
        public void PropertyGetter_ReturningStruct_CanAssignItsOwnName()
        {
            var widget = new PouAst(
                "FB_Widget",
                null,
                "VAR\nEND_VAR",
                "",
                new List<MethodAst>(),
                new List<PropertyAst>
                {
                    new PropertyAst(
                        "stPair",
                        "PROPERTY stPair : ST_Pair",
                        "stTmp.nA := 3;\nstPair := stTmp;",
                        null,
                        "VAR\n	stTmp : ST_Pair;\nEND_VAR",
                        string.Empty),
                });
            var suite = Suite(
                "TEST('p');\nn := fbW.stPair.nA;\nAssertEquals_DINT( Expected := 3, Actual := n, Message := 'x' );\nTEST_FINISHED();",
                "VAR\n	n : DINT;\n	fbW : FB_Widget;\nEND_VAR");
            var registry = new TypeRegistry(new[] { suite, widget }, new[] { PairStruct });

            var result = Assert.Single(new Engine(registry).RunSuite("FB_MySuite"));

            Assert.True(result.Passed, Failures(result));
        }

        // An accessor's own VAR block declares locals the body may write; losing
        // the block turns every such write into an undeclared-variable failure.
        [Fact]
        public void PropertyGetterAndSetter_WriteTheirOwnDeclaredLocals()
        {
            var widget = new PouAst(
                "FB_Widget",
                null,
                "VAR\n	nStore : DINT := 4;\nEND_VAR",
                "",
                new List<MethodAst>(),
                new List<PropertyAst>
                {
                    new PropertyAst(
                        "nDoubled",
                        "PROPERTY nDoubled : DINT",
                        "nTmp := nStore * 2;\nnDoubled := nTmp;",
                        "nHalf := nDoubled / 2;\nnStore := nHalf;",
                        "VAR\n	nTmp : DINT;\nEND_VAR",
                        "VAR\n	nHalf : DINT;\nEND_VAR"),
                });
            var suite = Suite(
                "TEST('p');\nfbW.nDoubled := 20;\nn := fbW.nDoubled;\nAssertEquals_DINT( Expected := 20, Actual := n, Message := 'x' );\nTEST_FINISHED();",
                "VAR\n	n : DINT;\n	fbW : FB_Widget;\nEND_VAR");
            var registry = new TypeRegistry(new[] { suite, widget });

            var result = Assert.Single(new Engine(registry).RunSuite("FB_MySuite"));

            Assert.True(result.Passed, Failures(result));
        }

        // REF= is an assignment: it must not create the variable it binds.
        [Fact]
        public void RefAssign_ToUndeclaredName_FailsNamingTheVariable()
        {
            var result = RunSingle(Suite("TEST('r');\nzzNeverDeclared REF= n;\nTEST_FINISHED();"));

            var failure = Assert.Single(result.Failures);
            Assert.Contains("zzNeverDeclared", failure.Message);
            Assert.Equal(2, failure.Site.BodyLine);
        }

        // A bare GVL REFERENCE TO member must be rebound in the global table, not
        // shadowed by a local.
        [Fact]
        public void RefAssign_ToBareGvlReferenceMember_RebindsTheGlobal()
        {
            var gvl = new GvlAst("GVL_Machine", "VAR_GLOBAL\n	refN : REFERENCE TO DINT;\nEND_VAR");
            var suite = Suite(
                "TEST('r');\nrefN REF= n;\nn := 8;\nAssertEquals_DINT( Expected := 8, Actual := GVL_Machine.refN, Message := 'x' );\nTEST_FINISHED();");

            var result = RunSingle(suite, new[] { gvl });

            Assert.True(result.Passed, Failures(result));
        }

        // REF= replaces the global's Cell, so __ISVALIDREF must read the
        // member's declared type from a table REF= cannot overwrite, in both the
        // bare and the qualified spelling.
        [Fact]
        public void IsValidRef_OnGvlReferenceAfterRefAssign_ReadsTheDeclaredType()
        {
            var gvl = new GvlAst("GVL_Machine", "VAR_GLOBAL\n\tgRef : REFERENCE TO DINT;\nEND_VAR");
            var suite = Suite(
                "TEST('v');\ngRef REF= n;\nbBare := __ISVALIDREF(gRef);\nbQualified := __ISVALIDREF(GVL_Machine.gRef);\n" +
                "AssertTrue(Condition := bBare, Message := 'bare');\nAssertTrue(Condition := bQualified, Message := 'qualified');\nTEST_FINISHED();",
                "VAR\n\tn : DINT;\n\tbBare : BOOL;\n\tbQualified : BOOL;\nEND_VAR");

            var result = RunSingle(suite, new[] { gvl });

            Assert.True(result.Passed, Failures(result));
        }

        // A DUT/ARRAY return variable holds no value until assigned whole;
        // writing into a member of it must say so by name instead of leaking an
        // interpreter-internal exception.
        [Fact]
        public void Method_WritingFieldOfUnassignedStructReturn_FailsNamingTheVariable()
        {
            var suite = Suite(
                "TEST('f');\nstLocal := M_Field();",
                "VAR\n\tstLocal : ST_Pair;\nEND_VAR",
                new MethodAst("M_Field", "METHOD M_Field : ST_Pair", "M_Field.nA := 5;"));

            var failure = Assert.Single(RunSingle(suite, structs: new[] { PairStruct }).Failures);

            Assert.Contains("M_Field", failure.Message);
            Assert.DoesNotContain("Object reference", failure.Message);
            Assert.DoesNotContain("Cannot access fields on", failure.Message);
        }

        [Fact]
        public void Method_WritingElementOfUnassignedArrayReturn_FailsNamingTheVariable()
        {
            var suite = Suite(
                "TEST('a');\nn := M_Arr()[1];",
                "VAR\n\tn : DINT;\nEND_VAR",
                new MethodAst("M_Arr", "METHOD M_Arr : ARRAY[1..2] OF DINT", "M_Arr[1] := 7;"));

            var failure = Assert.Single(RunSingle(suite).Failures);

            Assert.Contains("M_Arr", failure.Message);
            Assert.DoesNotContain("Object reference", failure.Message);
        }

        [Fact]
        public void LegitimateTargets_MethodReturnForCounterInputOutputInOutAndMethodLocal_StillAssign()
        {
            var suite = Suite(
                "TEST('t');\nn := M_All( nIn := 1, nInOut := n );\nAssertEquals_DINT( Expected := 13, Actual := n, Message := 'x' );\nTEST_FINISHED();",
                "VAR\n\tn : DINT;\nEND_VAR",
                new MethodAst(
                    "M_All",
                    "METHOD M_All : DINT\nVAR_INPUT\n\tnIn : DINT;\nEND_VAR\nVAR_IN_OUT\n\tnInOut : DINT;\nEND_VAR\nVAR_OUTPUT\n\tnOut : DINT;\nEND_VAR\nVAR\n\ti : DINT;\n\tnSum : DINT;\nEND_VAR",
                    "nOut := 2;\nnInOut := 10;\nFOR i := 1 TO 2 DO\n\tnSum := nSum + nIn;\nEND_FOR\nnIn := nIn + 1;\nM_All := nSum + nOut + nInOut - 1;"));

            var result = RunSingle(suite);

            Assert.True(result.Passed, Failures(result));
        }
    }
}
