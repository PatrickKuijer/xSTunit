using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // Operands sharing a CLR box keep it in the result; operands in different
    // boxes are promoted as NumericCoercion.Promote does for the binary
    // operators, so the result type is never wider than arithmetic would give.
    public class SelectionFunctionTests
    {
        private static object Eval(string expression, string varBlock = "VAR\nEND_VAR")
        {
            var fb = new PouAst("FB_Holder", null, varBlock, "", new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { fb }));
            var instance = engine.NewInstance("FB_Holder");
            return engine.Evaluate(Parser.ParseExpression(expression), new Frame(instance, "FB_Holder"));
        }

        [Fact]
        public void Min_OfTwoIntegers_ReturnsSmaller()
        {
            Assert.Equal(3, Assert.IsType<int>(Eval("MIN(3, 7)")));
        }

        [Fact]
        public void Max_OfTwoIntegers_ReturnsLarger()
        {
            Assert.Equal(7, Assert.IsType<int>(Eval("MAX(3, 7)")));
        }

        [Fact]
        public void Limit_ValueAboveMaximum_ReturnsMaximum()
        {
            Assert.Equal(5, Assert.IsType<int>(Eval("LIMIT(0, 9, 5)")));
        }

        [Fact]
        public void Limit_ValueInsideRange_ReturnsValue()
        {
            Assert.Equal(4, Assert.IsType<int>(Eval("LIMIT(0, 4, 5)")));
        }

        [Fact]
        public void Limit_ValueBelowMinimum_ReturnsMinimum()
        {
            Assert.Equal(2, Assert.IsType<int>(Eval("LIMIT(2, -9, 5)")));
        }

        [Fact]
        public void Limit_MinimumAboveMaximum_ReturnsMaximum()
        {
            // IEC defines LIMIT as MIN(MAX(IN, MN), MX), so an inverted range
            // resolves to MX rather than being rejected or swapped.
            Assert.Equal(3, Assert.IsType<int>(Eval("LIMIT(8, 5, 3)")));
        }

        [Fact]
        public void Limit_MinimumEqualsMaximum_ReturnsThatBound()
        {
            Assert.Equal(4, Assert.IsType<int>(Eval("LIMIT(4, 100, 4)")));
            Assert.Equal(4, Assert.IsType<int>(Eval("LIMIT(4, -100, 4)")));
        }

        [Fact]
        public void Limit_NamedArguments_Bind()
        {
            Assert.Equal(5, Assert.IsType<int>(Eval("LIMIT(MX := 5, IN := 9, MN := 0)")));
        }

        [Fact]
        public void Limit_MixedNamedAndPositionalArguments_Bind()
        {
            Assert.Equal(5, Assert.IsType<int>(Eval("LIMIT(0, MX := 5, IN := 9)")));
        }

        [Fact]
        public void Min_ThreeOrMoreInputs_ReturnsSmallest()
        {
            Assert.Equal(-2, Assert.IsType<int>(Eval("MIN(4, 9, -2, 6)")));
        }

        [Fact]
        public void Max_ThreeOrMoreInputs_ReturnsLargest()
        {
            Assert.Equal(9, Assert.IsType<int>(Eval("MAX(4, 9, -2, 6)")));
        }

        [Fact]
        public void Min_SmallestInputInLastPosition_IsFound()
        {
            Assert.Equal(1, Assert.IsType<int>(Eval("MIN(5, 3, 1)")));
        }

        [Fact]
        public void Min_IsCaseInsensitive()
        {
            Assert.Equal(3, Assert.IsType<int>(Eval("min(3, 7)")));
        }

        [Fact]
        public void Min_OfIntAndLint_PromotesToLong()
        {
            var result = Eval("MIN(nA, nB)", "VAR\n\tnA : INT := 3;\n\tnB : LINT := 7;\nEND_VAR");

            Assert.Equal(3L, Assert.IsType<long>(result));
        }

        [Fact]
        public void Max_OfIntAndReal_PromotesToReal()
        {
            var result = Eval("MAX(nA, fB)", "VAR\n\tnA : INT := 3;\n\tfB : REAL := 2.5;\nEND_VAR");

            Assert.Equal(3f, Assert.IsType<float>(result));
        }

        [Fact]
        public void Min_OfRealAndLreal_PromotesToLreal()
        {
            var result = Eval("MIN(fA, fB)", "VAR\n\tfA : REAL := 1.5;\n\tfB : LREAL := 2.5;\nEND_VAR");

            Assert.Equal(1.5, Assert.IsType<double>(result));
        }

        [Fact]
        public void Min_OfLreals_ReturnsLreal()
        {
            var result = Eval("MIN(fA, fB)", "VAR\n\tfA : LREAL := 1.0;\n\tfB : LREAL := -1.5;\nEND_VAR");

            Assert.Equal(-1.5, Assert.IsType<double>(result));
        }

        [Fact]
        public void Min_OfUlints_ReturnsUlong()
        {
            var result = Eval("MIN(nA, nB)", "VAR\n\tnA : ULINT := 18000000000000000000;\n\tnB : ULINT := 5;\nEND_VAR");

            Assert.Equal(5UL, Assert.IsType<ulong>(result));
        }

        [Fact]
        public void Max_OfUdints_ReturnsLargerAboveInt32Range()
        {
            var result = Eval("MAX(nA, nB)", "VAR\n\tnA : UDINT := 4000000000;\n\tnB : UDINT := 5;\nEND_VAR");

            Assert.Equal(4000000000L, Assert.IsType<long>(result));
        }

        [Fact]
        public void Min_OfTimes_KeepsTimeRepresentation()
        {
            var result = Eval("MIN(tA, tB)", "VAR\n\ttA : TIME := T#3s;\n\ttB : TIME := T#1s;\nEND_VAR");

            Assert.Equal(1000u, Assert.IsType<uint>(result));
        }

        [Fact]
        public void Max_OfTimeLiterals_KeepsTimeRepresentation()
        {
            Assert.Equal(3000u, Assert.IsType<uint>(Eval("MAX(T#1s, T#3s)")));
        }

        [Fact]
        public void Limit_OfTimes_ClampsToMaximum()
        {
            var result = Eval("LIMIT(T#1s, T#9s, T#5s)");

            Assert.Equal(5000u, Assert.IsType<uint>(result));
        }

        [Fact]
        public void Max_OfLtimes_ReturnsLtime()
        {
            var result = Eval("MAX(LTIME#1s, LTIME#3s)");

            Assert.Equal(3000000000UL, Assert.IsType<ulong>(result));
        }

        [Fact]
        public void Min_OfDates_ReturnsEarlierDate()
        {
            var early = Assert.IsType<uint>(Eval("D#2024-01-01"));
            var late = Assert.IsType<uint>(Eval("D#2025-01-01"));

            Assert.Equal(early, Assert.IsType<uint>(Eval("MIN(D#2025-01-01, D#2024-01-01)")));
            Assert.Equal(late, Assert.IsType<uint>(Eval("MAX(D#2025-01-01, D#2024-01-01)")));
        }

        [Fact]
        public void Max_OfDateTimes_ReturnsLaterDateTime()
        {
            var late = Assert.IsType<uint>(Eval("DT#2024-01-02-10:00:00"));

            Assert.Equal(late, Assert.IsType<uint>(Eval("MAX(DT#2024-01-01-10:00:00, DT#2024-01-02-10:00:00)")));
        }

        [Fact]
        public void Min_OfTimesOfDay_ReturnsEarlier()
        {
            var early = Assert.IsType<uint>(Eval("TOD#08:00:00"));

            Assert.Equal(early, Assert.IsType<uint>(Eval("MIN(TOD#20:00:00, TOD#08:00:00)")));
        }

        [Fact]
        public void Min_OfStrings_ComparesOrdinally()
        {
            Assert.Equal("Zebra", Assert.IsType<string>(Eval("MIN('apple', 'Zebra')")));
            Assert.Equal("apple", Assert.IsType<string>(Eval("MAX('apple', 'Zebra')")));
        }

        [Fact]
        public void Max_OfEnumMembers_ReturnsMemberValue()
        {
            var result = Eval("MAX(TcEventSeverity.Info, TcEventSeverity.Error)");

            Assert.Equal(4, Assert.IsType<int>(result));
        }

        [Fact]
        public void Min_OfMixedTimeAndLtime_KeepsThrowing()
        {
            Assert.Throws<NotSupportedException>(() => Eval("MIN(T#1s, LTIME#1s)"));
        }

        [Fact]
        public void Min_WithOneArgument_NamesFunctionInError()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Eval("MIN(3)"));

            Assert.Contains("MIN requires at least 2 arguments", ex.Message);
        }

        [Fact]
        public void Max_WithNoArguments_NamesFunctionInError()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Eval("MAX()"));

            Assert.Contains("MAX requires at least 2 arguments", ex.Message);
        }

        [Fact]
        public void Limit_WithTwoArguments_NamesFunctionInError()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Eval("LIMIT(0, 9)"));

            Assert.Contains("LIMIT missing argument 'MX'", ex.Message);
        }

        [Fact]
        public void Limit_WithFourArguments_NamesFunctionInError()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Eval("LIMIT(0, 9, 5, 1)"));

            Assert.Contains("LIMIT takes exactly 3 arguments (MN, IN, MX)", ex.Message);
        }

        [Fact]
        public void Min_WithBoolOperands_NamesFunctionInError()
        {
            var ex = Assert.Throws<NotSupportedException>(() => Eval("MIN(TRUE, FALSE)"));

            Assert.Contains("MIN requires ordered arguments", ex.Message);
        }

        [Fact]
        public void Max_OfStringAndNumber_NamesFunctionInError()
        {
            var ex = Assert.Throws<NotSupportedException>(() => Eval("MAX('a', 1)"));

            Assert.Contains("MAX cannot order", ex.Message);
        }

        [Fact]
        public void Min_WithNamedArguments_NamesFunctionInError()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Eval("MIN(IN1 := 1, IN2 := 2)"));

            Assert.Contains("MIN takes positional arguments only", ex.Message);
        }

        [Fact]
        public void Limit_DuplicateNamedArgument_NamesThatParameter()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Eval("LIMIT(MN := 0, IN := 9, IN := 4)"));

            Assert.Contains("LIMIT parameter 'IN' given more than once", ex.Message);
        }

        [Fact]
        public void Limit_UnknownNamedArgument_NamesThatParameter()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Eval("LIMIT(MN := 0, IN := 9, XX := 4)"));

            Assert.Contains("LIMIT has no parameter 'XX'", ex.Message);
        }

        [Fact]
        public void Limit_MissingNamedParameter_NamesThatParameter()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Eval("LIMIT(MN := 0, MX := 4)"));

            Assert.Contains("LIMIT missing argument 'IN'", ex.Message);
        }

        [Fact]
        public void Min_OfMixedTimeAndLtime_ErrorNamesFunction()
        {
            var ex = Assert.Throws<NotSupportedException>(() => Eval("MIN(T#1s, LTIME#1s)"));

            Assert.Contains("MIN cannot order mixed operand types", ex.Message);
        }

        private static FbInstance Run(string body, params PouAst[] extraPous)
        {
            var next = new MethodAst("Next", "METHOD Next : INT", "nCount := nCount + 1;\nNext := nCount;");
            var run = new MethodAst("Run", "METHOD Run : BOOL", body);
            var fb = new PouAst(
                "FB_Holder", null,
                "VAR\n\tnCount : INT;\n\tnResult : INT;\nEND_VAR",
                "", new List<MethodAst> { next, run });
            var engine = new Engine(new TypeRegistry(new[] { fb }.Concat(extraPous)));
            var instance = engine.NewInstance("FB_Holder");
            engine.CallMethod(instance, "Run", new Expr[0], new NamedArg[0], null, null);
            return instance;
        }

        [Fact]
        public void Min_UserGlobalFunctionOfSameName_TakesPrecedence()
        {
            var userMin = new PouAst(
                "MIN", null,
                "FUNCTION MIN : INT\nVAR_INPUT\n\tnA : INT;\n\tnB : INT;\nEND_VAR",
                "MIN := 1000;", new List<MethodAst>());

            var instance = Run("nResult := MIN(3, 7);", userMin);

            Assert.Equal(1000, instance.Fields["nResult"].Value);
        }

        [Fact]
        public void Limit_CalledFromGlobalFunctionBody_Resolves()
        {
            var wrapper = new PouAst(
                "F_Clamp", null,
                "FUNCTION F_Clamp : INT\nVAR_INPUT\n\tnIn : INT;\nEND_VAR",
                "F_Clamp := LIMIT(0, MAX(nIn, -4), 10);", new List<MethodAst>());

            var instance = Run("nResult := F_Clamp(-9) + F_Clamp(99);", wrapper);

            Assert.Equal(10, instance.Fields["nResult"].Value);
        }

        [Fact]
        public void Min_QualifiedCallOnThis_IsNotIntercepted()
        {
            // The built-ins are unqualified operators; a qualified call names a
            // method and must keep its method-not-found error.
            var ex = Assert.Throws<InvalidOperationException>(() => Run("nResult := THIS^.MIN(3, 7);"));

            Assert.Contains("Method 'MIN' not found", ex.Message);
        }

        [Fact]
        public void Min_EachArgumentIsEvaluatedOnce()
        {
            var instance = Run("nResult := MIN(Next(), Next(), Next());");

            Assert.Equal(1, instance.Fields["nResult"].Value);
            Assert.Equal(3, instance.Fields["nCount"].Value);
        }

        [Fact]
        public void Limit_EachArgumentIsEvaluatedOnce()
        {
            var instance = Run("nResult := LIMIT(Next(), Next(), Next());");

            Assert.Equal(2, instance.Fields["nResult"].Value);
            Assert.Equal(3, instance.Fields["nCount"].Value);
        }
    }
}
