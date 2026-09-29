using System;
using System.Collections.Generic;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    public class LiteralAndBoundsAssignmentTests
    {
        private const string ItemType =
            "TYPE ST_Item :\nSTRUCT\n\tbFlag : BOOL;\n\tnValue : INT := 7;\nEND_STRUCT\nEND_TYPE";

        private const string OuterType =
            "TYPE ST_Outer :\nSTRUCT\n\tnTop : INT := 3;\n\taArr : ARRAY[1..3] OF INT := [9, 9, 9];\nEND_STRUCT\nEND_TYPE";

        private static FbInstance Run(
            string fields, string body, IEnumerable<MethodAst> methods = null, IEnumerable<PouAst> extraPous = null)
        {
            var hostMethods = new List<MethodAst>(methods ?? Array.Empty<MethodAst>())
            {
                new MethodAst("Run", "METHOD Run", body),
            };
            var host = new PouAst("FB_Host", null, fields, "", hostMethods);
            var pous = new List<PouAst> { host };
            pous.AddRange(extraPous ?? Array.Empty<PouAst>());
            var registry = new TypeRegistry(
                pous, new[] { StructDeclParser.Parse(ItemType), StructDeclParser.Parse(OuterType) });
            var engine = new Engine(registry);
            var instance = engine.NewInstance("FB_Host");
            engine.CallMethod(instance, "Run", new Expr[0], new NamedArg[0], null, null);
            return instance;
        }

        private static object Field(FbInstance instance, string name) => instance.Fields[name].Value;

        // A struct literal assignment must leave the target a full instance of
        // its declared type: fields the literal does not name read as their
        // declared defaults, never as unknown fields.
        [Fact]
        public void StructLiteral_UnnamedFieldsReadAsDeclaredDefaults()
        {
            var host = Run(
                "VAR\n\tstA : ST_Item;\n\tbResult : BOOL := TRUE;\n\tnResult : INT;\nEND_VAR",
                "stA := (bFlag := TRUE);\nbResult := stA.bFlag;\nnResult := stA.nValue;");

            Assert.Equal(true, Field(host, "bResult"));
            Assert.Equal(7, Field(host, "nResult"));
        }

        // Unnamed fields are reset, not left at whatever the target held
        // before the literal was assigned.
        [Fact]
        public void StructLiteral_ResetsUnnamedFieldsPreviouslyWritten()
        {
            var host = Run(
                "VAR\n\tstA : ST_Item;\n\tbResult : BOOL := TRUE;\n\tnResult : INT;\nEND_VAR",
                "stA.bFlag := TRUE;\nstA.nValue := 99;\nstA := (nValue := 5);\nbResult := stA.bFlag;\nnResult := stA.nValue;");

            Assert.Equal(false, Field(host, "bResult"));
            Assert.Equal(5, Field(host, "nResult"));
        }

        // Assigning a literal to a struct field or array element fills that
        // slot's declared shape the same way a plain variable does.
        [Fact]
        public void StructLiteral_AssignedToArrayElement_ResetsUnnamedFields()
        {
            var host = Run(
                "VAR\n\taItems : ARRAY[1..2] OF ST_Item;\n\tbResult : BOOL := TRUE;\n\tnResult : INT;\nEND_VAR",
                "aItems[2].bFlag := TRUE;\naItems[2] := (nValue := 5);\nbResult := aItems[2].bFlag;\nnResult := aItems[2].nValue;");

            Assert.Equal(false, Field(host, "bResult"));
            Assert.Equal(5, Field(host, "nResult"));
        }

        // The target keeps its declared bounds after a shorter literal, so an
        // index inside the declared range stays valid.
        [Fact]
        public void ArrayLiteral_ShorterThanTarget_KeepsDeclaredBounds()
        {
            var host = Run(
                "VAR\n\taA : ARRAY[1..5] OF INT;\n\tn1 : INT;\n\tn3 : INT;\n\tn5 : INT := 42;\nEND_VAR",
                "aA := [1, 2, 3];\nn1 := aA[1];\nn3 := aA[3];\nn5 := aA[5];");

            Assert.Equal(1, Field(host, "n1"));
            Assert.Equal(3, Field(host, "n3"));
            Assert.Equal(0, Field(host, "n5"));
        }

        // Elements past the literal are reset to defaults rather than keeping
        // their earlier values.
        [Fact]
        public void ArrayLiteral_ResetsElementsPastTheLiteral()
        {
            var host = Run(
                "VAR\n\taA : ARRAY[1..5] OF INT;\n\tn4 : INT := 42;\nEND_VAR",
                "aA[4] := 9;\naA := [1, 2, 3];\nn4 := aA[4];");

            Assert.Equal(0, Field(host, "n4"));
        }

        [Fact]
        public void ArrayLiteral_LongerThanTarget_ThrowsNamingTheVariable()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Run(
                "VAR\n\taShort : ARRAY[1..2] OF INT;\nEND_VAR",
                "aShort := [1, 2, 3];"));

            Assert.Contains("aShort", ex.Message);
        }

        [Fact]
        public void ArrayAssignment_DifferentBounds_ThrowsNamingBothShapes()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Run(
                "VAR\n\taDst : ARRAY[1..3] OF INT;\n\taSrc : ARRAY[0..2] OF INT;\nEND_VAR",
                "aDst := aSrc;"));

            Assert.Contains("aDst", ex.Message);
            Assert.Contains("1..3", ex.Message);
            Assert.Contains("0..2", ex.Message);
        }

        [Fact]
        public void ArrayAssignment_DifferentLength_ThrowsNamingBothShapes()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Run(
                "VAR\n\taDst : ARRAY[1..3] OF INT;\n\taSrc : ARRAY[1..4] OF INT;\nEND_VAR",
                "aDst := aSrc;"));

            Assert.Contains("ARRAY[1..4]", ex.Message);
            Assert.Contains("ARRAY[1..3]", ex.Message);
        }

        [Fact]
        public void ArrayAssignment_SameBounds_StillCopies()
        {
            var host = Run(
                "VAR\n\taDst : ARRAY[1..3] OF INT;\n\taSrc : ARRAY[1..3] OF INT := [4, 5, 6];\n\tn : INT;\nEND_VAR",
                "aDst := aSrc;\nn := aDst[3];");

            Assert.Equal(6, Field(host, "n"));
        }

        private static MethodAst NextMethod() => new MethodAst(
            "M_Next", "METHOD M_Next : INT", "nCalls := nCalls + 1;\nM_Next := 1;");

        // The target's index expression is evaluated exactly once, and the
        // literal lands in the slot that index selected.
        [Theory]
        [InlineData("ARRAY[1..2] OF ST_Item")]
        [InlineData("ARRAY[1..1] OF ST_Item")]
        public void StructLiteral_ToIndexedTarget_EvaluatesTheIndexOnce(string arrayType)
        {
            var host = Run(
                $"VAR\n\taItems : {arrayType};\n\tnCalls : INT;\n\tnResult : INT;\nEND_VAR",
                "aItems[M_Next()] := (nValue := 5);\nnResult := aItems[1].nValue;",
                new[] { NextMethod() });

            Assert.Equal(1, Field(host, "nCalls"));
            Assert.Equal(5, Field(host, "nResult"));
        }

        private static PouAst PropertyFb() => new PouAst(
            "FB_P",
            null,
            "VAR\n\t_st : ST_Item;\n\t_a : ARRAY[1..3] OF INT;\n\tnGets : INT;\nEND_VAR",
            "",
            new List<MethodAst>(),
            new List<PropertyAst>
            {
                new PropertyAst("stSetOnly", "PROPERTY stSetOnly : ST_Item", null, "_st := stSetOnly;"),
                new PropertyAst("aSetOnly", "PROPERTY aSetOnly : ARRAY[1..3] OF INT", null, "_a := aSetOnly;"),
                new PropertyAst(
                    "stBoth",
                    "PROPERTY stBoth : ST_Item",
                    "nGets := nGets + 1;\nstBoth := _st;",
                    "_st := stBoth;"),
                new PropertyAst(
                    "aVals",
                    "PROPERTY aVals : ARRAY[1..3] OF INT",
                    "aVals := [1, 2, 3];",
                    null),
            });

        // Assigning through a property must not read it first: a Get accessor
        // has side effects and a Set-only property has no Get to run.
        [Fact]
        public void StructLiteral_ToGetAndSetProperty_DoesNotRunGet()
        {
            var host = Run(
                "VAR\n\tfb : FB_P;\n\tnGets : INT := 99;\n\tnResult : INT;\nEND_VAR",
                "fb.stBoth := (nValue := 5);\nnGets := fb.nGets;\nnResult := fb._st.nValue;",
                extraPous: new[] { PropertyFb() });

            Assert.Equal(0, Field(host, "nGets"));
            Assert.Equal(5, Field(host, "nResult"));
        }

        [Fact]
        public void StructLiteral_ToSetOnlyProperty_Assigns()
        {
            var host = Run(
                "VAR\n\tfb : FB_P;\n\tbResult : BOOL := TRUE;\n\tnResult : INT;\nEND_VAR",
                "fb.stSetOnly := (nValue := 5);\nbResult := fb._st.bFlag;\nnResult := fb._st.nValue;",
                extraPous: new[] { PropertyFb() });

            Assert.Equal(false, Field(host, "bResult"));
            Assert.Equal(5, Field(host, "nResult"));
        }

        [Fact]
        public void ArrayLiteral_ToSetOnlyProperty_Assigns()
        {
            var host = Run(
                "VAR\n\tfb : FB_P;\n\tn : INT;\nEND_VAR",
                "fb.aSetOnly := [4, 5];\nn := fb._a[2];",
                extraPous: new[] { PropertyFb() });

            Assert.Equal(5, Field(host, "n"));
        }

        [Fact]
        public void ArrayLiteral_AssignedToPropertyReturn_KeepsDeclaredBounds()
        {
            var host = Run(
                "VAR\n\tfb : FB_P;\n\taDst : ARRAY[1..3] OF INT;\n\tn : INT;\nEND_VAR",
                "aDst := fb.aVals;\nn := aDst[3];",
                extraPous: new[] { PropertyFb() });

            Assert.Equal(3, Field(host, "n"));
        }

        [Fact]
        public void ArrayLiteral_AssignedToReturnVariable_CopiesIntoTargetWithSameBounds()
        {
            var host = Run(
                "VAR\n\taDst : ARRAY[1..3] OF INT;\n\tn : INT;\nEND_VAR",
                "aDst := M_Arr();\nn := aDst[3];",
                new[] { new MethodAst("M_Arr", "METHOD M_Arr : ARRAY[1..3] OF INT", "M_Arr := [1, 2, 3];") });

            Assert.Equal(3, Field(host, "n"));
        }

        [Fact]
        public void ArrayLiteral_AssignedTwiceToReturnVariable_KeepsLatestValues()
        {
            var host = Run(
                "VAR\n\taDst : ARRAY[1..3] OF INT;\n\tn : INT;\nEND_VAR",
                "aDst := M_Arr();\nn := aDst[1];",
                new[] { new MethodAst("M_Arr", "METHOD M_Arr : ARRAY[1..3] OF INT", "M_Arr := [1, 2, 3];\nM_Arr := [4, 5, 6];") });

            Assert.Equal(4, Field(host, "n"));
        }

        [Fact]
        public void StructLiteral_AssignedToReturnVariable_UnnamedFieldsReadAsDefaults()
        {
            var host = Run(
                "VAR\n\tstA : ST_Item;\n\tbResult : BOOL := TRUE;\n\tnResult : INT;\nEND_VAR",
                "stA := M_Make();\nbResult := stA.bFlag;\nnResult := stA.nValue;",
                new[] { new MethodAst("M_Make", "METHOD M_Make : ST_Item", "M_Make := (nValue := 5);") });

            Assert.Equal(false, Field(host, "bResult"));
            Assert.Equal(5, Field(host, "nResult"));
        }

        private static MethodAst OpenInputMethod(string body) => new MethodAst(
            "M_F",
            "METHOD M_F : INT\nVAR_INPUT\n\taIn : ARRAY[*] OF INT;\n\taOther : ARRAY[*] OF INT;\nEND_VAR",
            body);

        // An open-bound parameter has the concrete bounds of the array bound
        // to it, so assigning an array of other bounds is a shape error.
        [Fact]
        public void OpenArrayParameter_AssignedAnArrayOfOtherBounds_ThrowsNamingBothShapes()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Run(
                "VAR\n\taA : ARRAY[1..3] OF INT;\n\taB : ARRAY[0..4] OF INT;\n\tn : INT;\nEND_VAR",
                "n := M_F(aA, aB);",
                new[] { OpenInputMethod("aIn := aOther;\nM_F := 1;") }));

            Assert.Contains("ARRAY[0..4]", ex.Message);
            Assert.Contains("ARRAY[1..3]", ex.Message);
        }

        private static MethodAst OpenInOutMethod(string body) => new MethodAst(
            "M_Io",
            "METHOD M_Io : INT\nVAR_IN_OUT\n\taIo : ARRAY[*] OF INT;\nEND_VAR\nVAR_INPUT\n\taSrc : ARRAY[*] OF INT;\nEND_VAR",
            body);

        // A literal assigned through an open-bound VAR_IN_OUT fills the
        // caller's array in place; the caller's fixed bounds survive.
        [Fact]
        public void OpenInOutParameter_LiteralAssignment_KeepsCallersBounds()
        {
            var host = Run(
                "VAR\n\taA : ARRAY[1..3] OF INT := [7, 7, 7];\n\taS : ARRAY[1..3] OF INT;\n\tn1 : INT;\n\tn3 : INT := 5;\nEND_VAR",
                "M_Io(aA, aS);\nn1 := aA[1];\nn3 := aA[3];",
                new[] { OpenInOutMethod("aIo := [1, 2];\nM_Io := 1;") });

            Assert.Equal(1, Field(host, "n1"));
            Assert.Equal(0, Field(host, "n3"));
        }

        [Fact]
        public void OpenInOutParameter_AssignedArrayOfOtherBounds_Throws()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Run(
                "VAR\n\taA : ARRAY[1..3] OF INT;\n\taS : ARRAY[0..4] OF INT;\nEND_VAR",
                "M_Io(aA, aS);",
                new[] { OpenInOutMethod("aIo := aSrc;\nM_Io := 1;") }));

            Assert.Contains("ARRAY[0..4]", ex.Message);
            Assert.Contains("ARRAY[1..3]", ex.Message);
        }

        [Fact]
        public void OpenArrayParameter_BoundFromLiteral_AcceptsLiteralAssignment()
        {
            var host = Run(
                "VAR\n\tn : INT;\nEND_VAR",
                "n := M_F([1, 2], [3, 4]);",
                new[] { OpenInputMethod("aIn := [4, 5];\nM_F := aIn[1];") });

            Assert.Equal(5, Field(host, "n"));
        }

        [Theory]
        [InlineData("ARRAY[*, *] OF INT")]
        [InlineData("ARRAY[ * ] OF INT")]
        public void OpenArrayParameter_SpellingVariants_AreShapedAsOpen(string parameterType)
        {
            var host = Run(
                "VAR\n\taA : ARRAY[1..3] OF INT;\n\tn : INT;\nEND_VAR",
                "n := M_F(aA);",
                new[]
                {
                    new MethodAst(
                        "M_F",
                        $"METHOD M_F : INT\nVAR_INPUT\n\taIn : {parameterType};\nEND_VAR",
                        "aIn := [4, 5];\nM_F := aIn[2];"),
                });

            Assert.Equal(5, Field(host, "n"));
        }

        [Fact]
        public void ReferenceToArray_LiteralAssignment_FillsTheTargetsDeclaredShape()
        {
            var host = Run(
                "VAR\n\taA : ARRAY[1..5] OF INT := [9, 9, 9, 9, 9];\n\trefA : REFERENCE TO ARRAY[1..5] OF INT;\n\tn2 : INT;\n\tn5 : INT := 5;\nEND_VAR",
                "refA REF= aA;\nrefA := [1, 2];\nn2 := aA[2];\nn5 := aA[5];");

            Assert.Equal(2, Field(host, "n2"));
            Assert.Equal(0, Field(host, "n5"));
        }

        [Fact]
        public void ReferenceToStruct_LiteralAssignment_ResetsUnnamedFieldsOfTheTarget()
        {
            var host = Run(
                "VAR\n\tstA : ST_Item;\n\trefS : REFERENCE TO ST_Item;\n\tbResult : BOOL := TRUE;\n\tnResult : INT;\nEND_VAR",
                "refS REF= stA;\nstA.bFlag := TRUE;\nrefS := (nValue := 5);\nbResult := stA.bFlag;\nnResult := stA.nValue;");

            Assert.Equal(false, Field(host, "bResult"));
            Assert.Equal(5, Field(host, "nResult"));
        }

        [Fact]
        public void ThisQualifiedReferenceToStruct_LiteralAssignment_ResetsUnnamedFields()
        {
            var host = Run(
                "VAR\n\tstA : ST_Item;\n\trefS : REFERENCE TO ST_Item;\n\tbResult : BOOL := TRUE;\n\tnResult : INT;\nEND_VAR",
                "refS REF= stA;\nstA.bFlag := TRUE;\nTHIS^.refS := (nValue := 5);\nbResult := stA.bFlag;\nnResult := stA.nValue;");

            Assert.Equal(false, Field(host, "bResult"));
            Assert.Equal(5, Field(host, "nResult"));
        }

        [Fact]
        public void ArrayLiteral_TooLongInsideStructLiteral_Throws()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Run(
                "VAR\n\tst : ST_Outer;\nEND_VAR",
                "st := (aArr := [1, 2, 3, 4]);"));

            Assert.Contains("st.aArr", ex.Message);
        }

        [Fact]
        public void StructLiteral_ResetsNestedArrayFieldToDeclaredDefaults()
        {
            var host = Run(
                "VAR\n\tst : ST_Outer;\n\tn : INT;\n\tnTop : INT;\nEND_VAR",
                "st.aArr[2] := 1;\nst := (nTop := 4);\nn := st.aArr[2];\nnTop := st.nTop;");

            Assert.Equal(9, Field(host, "n"));
            Assert.Equal(4, Field(host, "nTop"));
        }

        [Fact]
        public void ArrayLiteral_TooLongForThisQualifiedTarget_NamesTheTarget()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Run(
                "VAR\n\taS : ARRAY[1..2] OF INT;\nEND_VAR",
                "THIS^.aS := [1, 2, 3];"));

            Assert.Contains("THIS^.aS", ex.Message);
        }

        // Native array assert parameters bind by value, not by assignment, so
        // arrays of different bounds must reach the assert and report its own
        // outcome instead of a shape error.
        [Fact]
        public void AssertArrayEquals_DifferentBounds_IsNotAnAssignmentShapeError()
        {
            var suite = new PouAst(
                "FB_MySuite",
                "TcUnit.FB_TestSuite",
                "VAR\n\taExp : ARRAY[0..2] OF INT := [1, 2, 3];\n\taAct : ARRAY[1..3] OF INT := [1, 2, 3];\nEND_VAR",
                "TEST('t');\nAssertArrayEquals_INT(Expecteds := aExp, Actuals := aAct, Message := 'x');\nTEST_FINISHED();",
                new List<MethodAst>());

            var result = Assert.Single(new Engine(new TypeRegistry(new[] { suite })).RunSuite("FB_MySuite"));

            Assert.True(result.Passed, string.Join("; ", System.Linq.Enumerable.Select(result.Failures, f => f.Message)));
        }
    }
}
