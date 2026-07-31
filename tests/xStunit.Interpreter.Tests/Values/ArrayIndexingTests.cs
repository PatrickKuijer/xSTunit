using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcXunit-sej.1: arr[i] / arr[i,j] read and write, prerequisite for
    // MEMCPY/MEMSET/MEMMOVE pointer-offset support (TcXunit-sej.3).
    public class ArrayIndexingTests
    {
        private static Engine NewEngine(IEnumerable<StructAst> structTypes = null) =>
            new Engine(new TypeRegistry(Array.Empty<PouAst>(), structTypes));

        [Fact]
        public void Evaluate_IndexExpr_ReadsElementAtIndex()
        {
            var engine = NewEngine();
            var frame = new Frame(new FbInstance("Test"), "Test");

            var result = engine.Evaluate(Parser.ParseExpression("[10, 20, 30][1]"), frame);

            Assert.Equal(20, result);
        }

        [Fact]
        public void ExecuteStatements_IndexAssignment_WritesElementInPlace()
        {
            var fb = new PouAst("FB_Holder", null, "VAR\n\tbuf : ARRAY[1..3] OF INT;\nEND_VAR", "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            engine.ExecuteStatements(Parser.ParseStatements("buf[2] := 99;"), frame);

            var buf = (ArrayValue)instance.Fields["buf"].Value;
            Assert.Equal(new object[] { 0, 99, 0 }, buf.Elements);
        }

        [Fact]
        public void ExecuteStatements_IndexAssignment_HonorsDeclaredLowerBound()
        {
            var fb = new PouAst("FB_Holder", null, "VAR\n\tbuf : ARRAY[5..7] OF INT;\nEND_VAR", "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            engine.ExecuteStatements(Parser.ParseStatements("buf[5] := 1;"), frame);

            var buf = (ArrayValue)instance.Fields["buf"].Value;
            Assert.Equal(new object[] { 1, 0, 0 }, buf.Elements);
        }

        [Fact]
        public void Evaluate_MultiDimIndexExpr_FlattensRowMajor()
        {
            var fb = new PouAst("FB_Holder", null, "VAR\n\tgrid : ARRAY[1..2,1..3] OF INT;\nEND_VAR", "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            engine.ExecuteStatements(Parser.ParseStatements("grid[2,1] := 42;"), frame);

            var grid = (ArrayValue)instance.Fields["grid"].Value;
            Assert.Equal(42, grid.Elements[3]);
            var result = engine.Evaluate(Parser.ParseExpression("grid[2,1]"), frame);
            Assert.Equal(42, result);
        }

        [Fact]
        public void ExecuteStatements_IndexAssignment_OutOfBoundsThrows()
        {
            var fb = new PouAst("FB_Holder", null, "VAR\n\tbuf : ARRAY[1..3] OF INT;\nEND_VAR", "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            Assert.Throws<IndexOutOfRangeException>(() =>
                engine.ExecuteStatements(Parser.ParseStatements("buf[4] := 1;"), frame));
        }

        // DINT/UDINT/LINT-typed index variables box as long (NumericCoercion),
        // unlike INT which boxes as int - FlattenIndex used to (int)-cast the
        // evaluated index directly and threw InvalidCastException whenever it
        // was handed a boxed long, a real-usage find (TcXunit-iyd.5) against
        // FB_WidgetWireRecordsTests.
        [Fact]
        public void ExecuteStatements_IndexAssignment_AcceptsUdintIndexVariable()
        {
            var fb = new PouAst(
                "FB_Holder",
                null,
                "VAR\n\tbuf : ARRAY[1..3] OF INT;\n\ti : UDINT;\nEND_VAR",
                "",
                new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            engine.ExecuteStatements(Parser.ParseStatements("i := 2;\nbuf[i] := 99;"), frame);

            var buf = (ArrayValue)instance.Fields["buf"].Value;
            Assert.Equal(new object[] { 0, 99, 0 }, buf.Elements);

            var result = engine.Evaluate(Parser.ParseExpression("buf[i]"), frame);
            Assert.Equal(99, result);
        }

        [Fact]
        public void ExecuteStatements_FieldAssignment_WritesStructField()
        {
            var stPoint = StructDeclParser.Parse(@"TYPE ST_Point :
STRUCT
	x : INT;
END_STRUCT
END_TYPE");
            var fb = new PouAst("FB_Holder", null, "VAR\n\tpos : ST_Point;\nEND_VAR", "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }, new[] { stPoint }));
            var instance = engine.NewInstance("FB_Holder");
            var frame = new Frame(instance, "FB_Holder");

            engine.ExecuteStatements(Parser.ParseStatements("pos.x := 7;"), frame);

            var pos = (StructInstance)instance.Fields["pos"].Value;
            Assert.Equal(7, pos.Fields["x"].Value);
        }
    }
}
