using System;
using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-gd2.13: DATE/DATE_AND_TIME/TIME_OF_DAY literal grammar
    // (D#/DATE#, DT#/DATE_AND_TIME#, TOD#/TIME_OF_DAY#) and their Cell
    // representations - all box as uint (DATE/DT as seconds since the
    // 1970-01-01 epoch, TOD as milliseconds since midnight), mirroring how
    // TimeTypeTests.cs covers TIME/LTIME.
    public class DateTimeTypeTests
    {
        private static Engine NewEngine() => new Engine(new TypeRegistry(Array.Empty<PouAst>()));

        private static Frame NewFrame() => new Frame(new FbInstance("Test"), "Test");

        [Fact]
        public void ParseExpression_DateShortPrefixLiteral_ProducesDateLiteralExprInEpochSeconds()
        {
            var expr = Parser.ParseExpression("D#2024-01-01");

            var date = Assert.IsType<DateLiteralExpr>(expr);
            Assert.Equal(1_704_067_200u, date.Value);
        }

        [Fact]
        public void ParseExpression_DateKeywordPrefixLiteral_ProducesDateLiteralExpr()
        {
            var expr = Parser.ParseExpression("DATE#2024-01-01");

            var date = Assert.IsType<DateLiteralExpr>(expr);
            Assert.Equal(1_704_067_200u, date.Value);
        }

        [Fact]
        public void ParseExpression_DateLiteralWithInvalidMonth_Throws()
        {
            Assert.Throws<ParseException>(() => Parser.ParseExpression("D#2024-13-01"));
        }

        [Fact]
        public void ParseExpression_DateLiteralWithInvalidDay_Throws()
        {
            Assert.Throws<ParseException>(() => Parser.ParseExpression("D#2024-02-30"));
        }

        [Fact]
        public void ParseExpression_DateAndTimeShortPrefixLiteral_ProducesDateAndTimeLiteralExprInEpochSeconds()
        {
            var expr = Parser.ParseExpression("DT#2024-01-01-10:00:00");

            var dt = Assert.IsType<DateAndTimeLiteralExpr>(expr);
            Assert.Equal(1_704_103_200u, dt.Value);
        }

        [Fact]
        public void ParseExpression_DateAndTimeKeywordPrefixLiteral_ProducesDateAndTimeLiteralExpr()
        {
            var expr = Parser.ParseExpression("DATE_AND_TIME#2024-01-01-10:00:00");

            var dt = Assert.IsType<DateAndTimeLiteralExpr>(expr);
            Assert.Equal(1_704_103_200u, dt.Value);
        }

        [Fact]
        public void ParseExpression_DateAndTimeLiteralWithFractionalSeconds_Throws()
        {
            Assert.Throws<ParseException>(() => Parser.ParseExpression("DT#2024-01-01-10:00:00.500"));
        }

        [Fact]
        public void ParseExpression_DateAndTimeLiteralWithHourOutOfRange_Throws()
        {
            Assert.Throws<ParseException>(() => Parser.ParseExpression("DT#2024-01-01-24:00:00"));
        }

        [Fact]
        public void ParseExpression_TimeOfDayShortPrefixLiteral_ProducesTimeOfDayLiteralExprInMilliseconds()
        {
            var expr = Parser.ParseExpression("TOD#10:00:00");

            var tod = Assert.IsType<TimeOfDayLiteralExpr>(expr);
            Assert.Equal(36_000_000u, tod.Value);
        }

        [Fact]
        public void ParseExpression_TimeOfDayKeywordPrefixLiteral_ProducesTimeOfDayLiteralExpr()
        {
            var expr = Parser.ParseExpression("TIME_OF_DAY#10:00:00");

            var tod = Assert.IsType<TimeOfDayLiteralExpr>(expr);
            Assert.Equal(36_000_000u, tod.Value);
        }

        [Fact]
        public void ParseExpression_TimeOfDayLiteralWithFractionalSeconds_IncludesMilliseconds()
        {
            var expr = Parser.ParseExpression("TOD#10:00:00.500");

            var tod = Assert.IsType<TimeOfDayLiteralExpr>(expr);
            Assert.Equal(36_000_500u, tod.Value);
        }

        [Fact]
        public void ParseExpression_TimeOfDayLiteralWithHourOutOfRange_Throws()
        {
            Assert.Throws<ParseException>(() => Parser.ParseExpression("TOD#24:00:00"));
        }

        [Fact]
        public void ParseExpression_TimeOfDayLiteralWithMinuteOutOfRange_Throws()
        {
            Assert.Throws<ParseException>(() => Parser.ParseExpression("TOD#10:60:00"));
        }

        [Fact]
        public void Evaluate_DateLiteral_ReturnsUInt()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("D#1970-01-02"), NewFrame());

            Assert.IsType<uint>(result);
            Assert.Equal(86_400u, (uint)result);
        }

        [Fact]
        public void Evaluate_DateAndTimeLiteral_ReturnsUInt()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("DT#1970-01-01-00:00:01"), NewFrame());

            Assert.IsType<uint>(result);
            Assert.Equal(1u, (uint)result);
        }

        [Fact]
        public void Evaluate_TimeOfDayLiteral_ReturnsUInt()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("TOD#00:00:00.250"), NewFrame());

            Assert.IsType<uint>(result);
            Assert.Equal(250u, (uint)result);
        }

        [Fact]
        public void NewInstance_DateDtAndTodFields_DefaultToZeroOfCorrectClrType()
        {
            var pou = new PouAst(
                "FB_Calendar",
                null,
                "VAR\n\tdValue : DATE;\n\tdtValue : DATE_AND_TIME;\n\ttodValue : TIME_OF_DAY;\nEND_VAR",
                "",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_Calendar");

            Assert.IsType<uint>(instance.Fields["dValue"].Value);
            Assert.Equal(0u, instance.Fields["dValue"].Value);
            Assert.IsType<uint>(instance.Fields["dtValue"].Value);
            Assert.Equal(0u, instance.Fields["dtValue"].Value);
            Assert.IsType<uint>(instance.Fields["todValue"].Value);
            Assert.Equal(0u, instance.Fields["todValue"].Value);
        }
    }
}
