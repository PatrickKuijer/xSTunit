using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    public class StructArrayTypeTests
    {
        private static Engine NewEngine(IEnumerable<StructAst> structTypes = null) =>
            new Engine(new TypeRegistry(Array.Empty<PouAst>(), structTypes));

        [Fact]
        public void StructDeclParser_Parse_ReadsNameAndFieldsIncludingRealAndTime()
        {
            const string declaration = @"TYPE ST_Point :
STRUCT
	x : REAL;
	y : REAL;
	stamp : TIME;
END_STRUCT
END_TYPE";

            var structAst = StructDeclParser.Parse(declaration);

            Assert.Equal("ST_Point", structAst.Name);
            Assert.Equal(new[] { "x", "y", "stamp" }, structAst.Fields.Select(f => f.Name));
            Assert.Equal(new[] { "REAL", "REAL", "TIME" }, structAst.Fields.Select(f => f.TypeName));
        }

        [Fact]
        public void NewInstance_StructField_DefaultsAllFieldsToTypeDefault()
        {
            var stPoint = StructDeclParser.Parse(@"TYPE ST_Point :
STRUCT
	x : REAL;
	y : INT;
END_STRUCT
END_TYPE");

            var fb = new PouAst("FB_Holder", null, "VAR\n\tpos : ST_Point;\nEND_VAR", "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }, new[] { stPoint }));

            var instance = engine.NewInstance("FB_Holder");
            var pos = Assert.IsType<StructInstance>(instance.Fields["pos"].Value);

            Assert.Equal(0f, pos.Fields["x"].Value);
            Assert.Equal(0, pos.Fields["y"].Value);
        }

        [Fact]
        public void NewInstance_StructFieldWithLiteralDefault_OverlaysGivenFieldsKeepsRestDefault()
        {
            var stPoint = StructDeclParser.Parse(@"TYPE ST_Point :
STRUCT
	x : REAL;
	y : REAL;
END_STRUCT
END_TYPE");

            var fb = new PouAst(
                "FB_Holder",
                null,
                "VAR\n\tpos : ST_Point := (x := 1.5);\nEND_VAR",
                "",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }, new[] { stPoint }));

            var instance = engine.NewInstance("FB_Holder");
            var pos = (StructInstance)instance.Fields["pos"].Value;

            Assert.Equal(1.5f, pos.Fields["x"].Value);
            Assert.Equal(0f, pos.Fields["y"].Value);
        }

        [Fact]
        public void Evaluate_StructLiteral_BuildsStructInstanceFromGivenFields()
        {
            var engine = NewEngine();
            var frame = new Frame(new FbInstance("Test"), "Test");

            var result = engine.Evaluate(Parser.ParseExpression("(a := 1, b := 2)"), frame);

            var instance = Assert.IsType<StructInstance>(result);
            Assert.Equal(1, instance.Fields["a"].Value);
            Assert.Equal(2, instance.Fields["b"].Value);
        }

        [Fact]
        public void VarBlockParser_ArrayDeclaration_ReadsArrayTypeText()
        {
            const string declaration = @"VAR
	buf : ARRAY[1..10] OF INT;
END_VAR";

            var vars = VarBlockParser.Parse(declaration);

            var buf = Assert.Single(vars);
            Assert.Equal("buf", buf.Name);
            Assert.Equal("ARRAY[1..10] OF INT", buf.TypeName);
        }

        [Fact]
        public void NewInstance_ArrayField_DefaultsAllElementsAndPreservesBounds()
        {
            var fb = new PouAst("FB_Holder", null, "VAR\n\tbuf : ARRAY[1..3] OF INT;\nEND_VAR", "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));

            var instance = engine.NewInstance("FB_Holder");
            var buf = Assert.IsType<ArrayValue>(instance.Fields["buf"].Value);

            Assert.Equal(new[] { (1, 3) }, buf.Dimensions);
            Assert.Equal(3, buf.Elements.Length);
            Assert.All(buf.Elements, e => Assert.Equal(0, e));
        }

        [Fact]
        public void NewInstance_MultiDimArrayField_ComputesElementCountFromAllDimensions()
        {
            var fb = new PouAst(
                "FB_Holder",
                null,
                "VAR\n\tgrid : ARRAY[1..2,1..3] OF INT;\nEND_VAR",
                "",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));

            var instance = engine.NewInstance("FB_Holder");
            var grid = (ArrayValue)instance.Fields["grid"].Value;

            Assert.Equal(new[] { (1, 2), (1, 3) }, grid.Dimensions);
            Assert.Equal(6, grid.Elements.Length);
        }

        [Fact]
        public void NewInstance_ArrayOfStructField_DefaultsEachElementToStructDefault()
        {
            var stPoint = StructDeclParser.Parse(@"TYPE ST_Point :
STRUCT
	x : INT;
END_STRUCT
END_TYPE");
            var fb = new PouAst(
                "FB_Holder",
                null,
                "VAR\n\tpts : ARRAY[1..2] OF ST_Point;\nEND_VAR",
                "",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }, new[] { stPoint }));

            var instance = engine.NewInstance("FB_Holder");
            var pts = (ArrayValue)instance.Fields["pts"].Value;

            Assert.Equal(2, pts.Elements.Length);
            Assert.All(pts.Elements, e => Assert.Equal(0, Assert.IsType<StructInstance>(e).Fields["x"].Value));
        }

        [Fact]
        public void NewInstance_ArrayFieldWithLiteralDefault_OverlaysGivenValuesKeepsRestDefault()
        {
            var fb = new PouAst(
                "FB_Holder",
                null,
                "VAR\n\tbuf : ARRAY[1..4] OF INT := [10, 20];\nEND_VAR",
                "",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));

            var instance = engine.NewInstance("FB_Holder");
            var buf = (ArrayValue)instance.Fields["buf"].Value;

            Assert.Equal(new object[] { 10, 20, 0, 0 }, buf.Elements);
        }

        [Fact]
        public void NewInstance_ArrayFieldWithRepeatShorthandDefault_ExpandsRepeatedValues()
        {
            var fb = new PouAst(
                "FB_Holder",
                null,
                "VAR\n\tbuf : ARRAY[1..4] OF INT := [2(10), 2(20)];\nEND_VAR",
                "",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));

            var instance = engine.NewInstance("FB_Holder");
            var buf = (ArrayValue)instance.Fields["buf"].Value;

            Assert.Equal(new object[] { 10, 10, 20, 20 }, buf.Elements);
        }

        [Fact]
        public void Evaluate_ArrayLiteral_BuildsArrayValueFromGivenElements()
        {
            var engine = NewEngine();
            var frame = new Frame(new FbInstance("Test"), "Test");

            var result = engine.Evaluate(Parser.ParseExpression("[1, 2, 3]"), frame);

            var array = Assert.IsType<ArrayValue>(result);
            Assert.Equal(new object[] { 1, 2, 3 }, array.Elements);
        }

        [Fact]
        public void FieldAccess_OnStructInstance_ReadsFieldValue()
        {
            var stPoint = StructDeclParser.Parse(@"TYPE ST_Point :
STRUCT
	x : INT;
END_STRUCT
END_TYPE");
            var fb = new PouAst("FB_Holder", null, "VAR\n\tpos : ST_Point := (x := 7);\nEND_VAR", "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }, new[] { stPoint }));

            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            var result = engine.Evaluate(Parser.ParseExpression("pos.x"), frame);

            Assert.Equal(7, result);
        }
    }
}
