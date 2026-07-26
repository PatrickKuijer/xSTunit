using System;
using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-w5x.15.1: REAL/LREAL literals, INT->REAL->LREAL implicit widening,
    // explicit X_TO_Y narrowing casts, and rejection of narrowing without a cast.
    public class NumericTypeTests
    {
        private static Engine NewEngine() => new Engine(new TypeRegistry(Array.Empty<PouAst>()));

        private static Frame NewFrame() => new Frame(new FbInstance("Test"), "Test");

        [Fact]
        public void ParseExpression_BareDecimalLiteral_ProducesRealLiteralExpr()
        {
            var expr = Parser.ParseExpression("1.5");

            var real = Assert.IsType<RealLiteralExpr>(expr);
            Assert.Equal(1.5f, real.Value);
        }

        [Fact]
        public void ParseExpression_DecimalWithExponent_ProducesRealLiteralExpr()
        {
            var expr = Parser.ParseExpression("1.5e3");

            var real = Assert.IsType<RealLiteralExpr>(expr);
            Assert.Equal(1500f, real.Value);
        }

        [Fact]
        public void ParseExpression_LrealPrefixLiteral_ProducesLrealLiteralExpr()
        {
            var expr = Parser.ParseExpression("LREAL#1.5");

            var lreal = Assert.IsType<LrealLiteralExpr>(expr);
            Assert.Equal(1.5d, lreal.Value);
        }

        [Fact]
        public void ParseExpression_RealPrefixLiteral_ProducesRealLiteralExpr()
        {
            var expr = Parser.ParseExpression("REAL#2.0");

            var real = Assert.IsType<RealLiteralExpr>(expr);
            Assert.Equal(2.0f, real.Value);
        }

        [Fact]
        public void ParseExpression_PlainDigits_StillProducesIntLiteralExpr()
        {
            var expr = Parser.ParseExpression("42");

            var i = Assert.IsType<IntLiteralExpr>(expr);
            Assert.Equal(42, i.Value);
        }

        [Fact]
        public void Evaluate_IntPlusReal_WidensToReal()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("1 + 1.5"), NewFrame());

            Assert.IsType<float>(result);
            Assert.Equal(2.5f, (float)result);
        }

        [Fact]
        public void Evaluate_RealPlusLreal_WidensToLreal()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("REAL#1.5 + LREAL#1.5"), NewFrame());

            Assert.IsType<double>(result);
            Assert.Equal(3.0d, (double)result);
        }

        [Fact]
        public void Evaluate_IntTimesLreal_WidensToLreal()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("2 - LREAL#0.5"), NewFrame());

            Assert.IsType<double>(result);
            Assert.Equal(1.5d, (double)result);
        }

        [Fact]
        public void Evaluate_IntTimesInt_Multiplies()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("3 * 4"), NewFrame());

            Assert.IsType<int>(result);
            Assert.Equal(12, (int)result);
        }

        [Fact]
        public void Evaluate_IntDividedByInt_TruncatesTowardZero()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("7 / 2"), NewFrame());

            Assert.IsType<int>(result);
            Assert.Equal(3, (int)result);
        }

        [Fact]
        public void Evaluate_LrealTimesLreal_Multiplies()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("LREAL#2.5 * LREAL#2.0"), NewFrame());

            Assert.IsType<double>(result);
            Assert.Equal(5.0d, (double)result);
        }

        [Fact]
        public void Evaluate_RealDividedByReal_Divides()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("REAL#5.0 / REAL#2.0"), NewFrame());

            Assert.IsType<float>(result);
            Assert.Equal(2.5f, (float)result);
        }

        [Fact]
        public void Evaluate_IntToRealCast_ProducesFloat()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("INT_TO_REAL(3)"), NewFrame());

            Assert.IsType<float>(result);
            Assert.Equal(3f, (float)result);
        }

        [Fact]
        public void Evaluate_RealToIntCast_RoundsToNearestInt()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("REAL_TO_INT(REAL#3.2)"), NewFrame());

            Assert.IsType<int>(result);
            Assert.Equal(3, (int)result);
        }

        [Fact]
        public void Evaluate_LrealToRealCast_ProducesFloat()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("LREAL_TO_REAL(LREAL#2.5)"), NewFrame());

            Assert.IsType<float>(result);
            Assert.Equal(2.5f, (float)result);
        }

        [Fact]
        public void ExecuteStatements_AssignRealIntoIntWithoutCast_Throws()
        {
            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["x"] = new Cell { Value = 0 };

            var stmts = Parser.ParseStatements("x := REAL#1.5;");

            Assert.Throws<InvalidOperationException>(() => engine.ExecuteStatements(stmts, frame));
        }

        [Fact]
        public void ExecuteStatements_AssignLrealIntoRealWithoutCast_Throws()
        {
            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["x"] = new Cell { Value = 0f };

            var stmts = Parser.ParseStatements("x := LREAL#1.5;");

            Assert.Throws<InvalidOperationException>(() => engine.ExecuteStatements(stmts, frame));
        }

        [Fact]
        public void ExecuteStatements_AssignIntIntoRealCell_WidensImplicitly()
        {
            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["x"] = new Cell { Value = 0f };

            engine.ExecuteStatements(Parser.ParseStatements("x := 5;"), frame);

            Assert.Equal(5f, frame.Locals["x"].Value);
        }

        [Fact]
        public void ExecuteStatements_AssignCastResultIntoIntCell_Succeeds()
        {
            var engine = NewEngine();
            var frame = NewFrame();
            frame.Locals["x"] = new Cell { Value = 0 };

            engine.ExecuteStatements(Parser.ParseStatements("x := REAL_TO_INT(REAL#1.2);"), frame);

            Assert.Equal(1, frame.Locals["x"].Value);
        }

        [Fact]
        public void NewInstance_RealAndLrealFields_DefaultToZeroOfCorrectClrType()
        {
            var pou = new PouAst(
                "FB_Numeric",
                null,
                "VAR\n\trValue : REAL;\n\tlrValue : LREAL;\nEND_VAR",
                "",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_Numeric");

            Assert.IsType<float>(instance.Fields["rValue"].Value);
            Assert.Equal(0f, instance.Fields["rValue"].Value);
            Assert.IsType<double>(instance.Fields["lrValue"].Value);
            Assert.Equal(0d, instance.Fields["lrValue"].Value);
        }

        // TcXunit-6af.1: struct-field UDINT and FB-field UDINT must agree on
        // CLR representation - both go through IecNumericType now instead of
        // each defaulting path (Engine.BuildStructDefault vs. Engine.NewInstance)
        // re-deriving its own answer.
        [Fact]
        public void NewInstance_UdintField_AgreesWithStructFieldOnClrRepresentation()
        {
            var structAst = StructDeclParser.Parse(
                "TYPE ST_Udint :\nSTRUCT\n\tvalue : UDINT;\nEND_STRUCT\nEND_TYPE");
            var pou = new PouAst(
                "FB_Udint",
                null,
                "VAR\n\tvalue : UDINT;\n\ts : ST_Udint;\nEND_VAR",
                "",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { pou }, new[] { structAst }));
            var fbInstance = engine.NewInstance("FB_Udint");
            var structInstance = (StructInstance)fbInstance.Fields["s"].Value;

            Assert.IsType<long>(fbInstance.Fields["value"].Value);
            Assert.Equal(fbInstance.Fields["value"].Value.GetType(), structInstance.Fields["value"].Value.GetType());
            Assert.Equal(0L, fbInstance.Fields["value"].Value);
            Assert.Equal(0L, structInstance.Fields["value"].Value);
        }
    }
}
