using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
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

        // A bare decimal literal always lexes as REAL (float), so a declared
        // LREAL field must widen its initializer at declaration time. Without
        // that, the field runs at REAL precision for its whole life despite
        // being declared LREAL.
        [Fact]
        public void NewInstance_LrealFieldWithBareDecimalInitializer_WidensToDouble()
        {
            var pou = new PouAst(
                "FB_LrealInit",
                null,
                "VAR\n\tlrGain : LREAL := 2.5;\nEND_VAR",
                "",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_LrealInit");

            Assert.IsType<double>(instance.Fields["lrGain"].Value);
            Assert.Equal(2.5d, instance.Fields["lrGain"].Value);
        }

        // UDINT boxes as long, and the struct-field and FB-field defaulting
        // paths must not each re-derive that answer for themselves.
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

        // ULINT and LWORD box as ulong where LINT/UDINT/DWORD box as long, so
        // arithmetic, bitstring ops, MOD and assignment each need a widening
        // path of their own - the tests below walk them.
        [Fact]
        public void NewInstance_UlintField_DefaultsToZeroUlong()
        {
            var pou = new PouAst(
                "FB_Ulint",
                null,
                "VAR\n\tvalue : ULINT;\nEND_VAR",
                "",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_Ulint");

            Assert.IsType<ulong>(instance.Fields["value"].Value);
            Assert.Equal(0UL, instance.Fields["value"].Value);
        }

        [Fact]
        public void ExecuteStatements_UlintFieldPlusIntLiteral_WidensAndAdds()
        {
            var pou = new PouAst(
                "FB_Ulint",
                null,
                "VAR\n\tvalue : ULINT;\nEND_VAR",
                "",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_Ulint");
            var frame = new Frame(instance, "FB_Ulint");

            engine.ExecuteStatements(Parser.ParseStatements("value := value + 1;"), frame);

            Assert.IsType<ulong>(instance.Fields["value"].Value);
            Assert.Equal(1UL, instance.Fields["value"].Value);
        }

        [Fact]
        public void ExecuteStatements_UlintFieldAndIntLiteral_ComputesBitwiseAnd()
        {
            var pou = new PouAst(
                "FB_Ulint",
                null,
                "VAR\n\tvalue : ULINT;\nEND_VAR",
                "",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_Ulint");
            instance.Fields["value"].Value = 0xFFUL;
            var frame = new Frame(instance, "FB_Ulint");

            engine.ExecuteStatements(Parser.ParseStatements("value := value AND 15;"), frame);

            Assert.IsType<ulong>(instance.Fields["value"].Value);
            Assert.Equal(0x0FUL, instance.Fields["value"].Value);
        }

        [Fact]
        public void ExecuteStatements_UlintFieldModIntLiteral_ComputesRemainder()
        {
            var pou = new PouAst(
                "FB_Ulint",
                null,
                "VAR\n\tvalue : ULINT;\nEND_VAR",
                "",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_Ulint");
            instance.Fields["value"].Value = 10UL;
            var frame = new Frame(instance, "FB_Ulint");

            engine.ExecuteStatements(Parser.ParseStatements("value := value MOD 3;"), frame);

            Assert.IsType<ulong>(instance.Fields["value"].Value);
            Assert.Equal(1UL, instance.Fields["value"].Value);
        }

        // X_TO_STRING formats with InvariantCulture, so a machine running a
        // comma-decimal locale still renders "3.5" and string comparisons in
        // ST test code stay stable.
        [Fact]
        public void Evaluate_RealToStringCast_ProducesInvariantCultureString()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("REAL_TO_STRING(REAL#3.5)"), NewFrame());

            var s = Assert.IsType<string>(result);
            Assert.Equal("3.5", s);
        }

        [Fact]
        public void Evaluate_LrealToStringCast_ProducesInvariantCultureString()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("LREAL_TO_STRING(LREAL#2.25)"), NewFrame());

            var s = Assert.IsType<string>(result);
            Assert.Equal("2.25", s);
        }

        [Fact]
        public void Evaluate_IntToStringCast_ProducesPlainDigits()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("INT_TO_STRING(42)"), NewFrame());

            var s = Assert.IsType<string>(result);
            Assert.Equal("42", s);
        }

        // UDINT boxes as long, a different formatting branch from the
        // int-boxed INT case above. The value has to be built up through a
        // field rather than written as a literal, because the parser's
        // integer literals are Int32-range only.
        [Fact]
        public void Evaluate_UdintToStringCast_ProducesPlainDigits()
        {
            var pou = new PouAst(
                "FB_Udint",
                null,
                "VAR\n\tvalue : UDINT;\nEND_VAR",
                "",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_Udint");
            var frame = new Frame(instance, "FB_Udint");
            engine.ExecuteStatements(Parser.ParseStatements("value := value + 4000000;"), frame);
            Assert.IsType<long>(instance.Fields["value"].Value);

            var result = engine.Evaluate(Parser.ParseExpression("UDINT_TO_STRING(value)"), frame);

            var s = Assert.IsType<string>(result);
            Assert.Equal("4000000", s);
        }

        [Fact]
        public void Evaluate_NegativeLrealToStringCast_IncludesSign()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("LREAL_TO_STRING(-1.5)"), NewFrame());

            var s = Assert.IsType<string>(result);
            Assert.Equal("-1.5", s);
        }

        [Fact]
        public void Evaluate_NegativeIntToStringCast_IncludesSign()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("INT_TO_STRING(-7)"), NewFrame());

            var s = Assert.IsType<string>(result);
            Assert.Equal("-7", s);
        }

        // BOOL is not a numeric cast source, so BOOL_TO_STRING is not a cast
        // at all and must keep falling through to ordinary method dispatch.
        [Fact]
        public void Evaluate_BoolToStringCall_StillThrowsMethodNotFound()
        {
            var engine = NewEngine();

            var ex = Assert.Throws<InvalidOperationException>(
                () => engine.Evaluate(Parser.ParseExpression("BOOL_TO_STRING(TRUE)"), NewFrame()));

            Assert.Equal("Method 'BOOL_TO_STRING' not found starting from type 'Test'", ex.Message);
        }

        [Fact]
        public void Evaluate_RealToStringCastConcatenatedWithLiteral_ProducesCombinedString()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(
                Parser.ParseExpression("CONCAT('Value: ', REAL_TO_STRING(REAL#1.5))"), NewFrame());

            var s = Assert.IsType<string>(result);
            Assert.Equal("Value: 1.5", s);
        }

        [Fact]
        public void Evaluate_IntToStringCastConcatenatedWithLiteral_ProducesCombinedString()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(
                Parser.ParseExpression("CONCAT(INT_TO_STRING(3), ' apples')"), NewFrame());

            var s = Assert.IsType<string>(result);
            Assert.Equal("3 apples", s);
        }

        [Fact]
        public void ExecuteStatements_AssignIntLiteralIntoUlintField_Widens()
        {
            var pou = new PouAst(
                "FB_Ulint",
                null,
                "VAR\n\tvalue : ULINT;\nEND_VAR",
                "",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_Ulint");
            var frame = new Frame(instance, "FB_Ulint");

            engine.ExecuteStatements(Parser.ParseStatements("value := 5;"), frame);

            Assert.IsType<ulong>(instance.Fields["value"].Value);
            Assert.Equal(5UL, instance.Fields["value"].Value);
        }
    }
}
