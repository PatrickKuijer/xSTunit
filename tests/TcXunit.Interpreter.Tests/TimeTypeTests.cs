using System;
using System.Collections.Generic;
using TcXunit.Interpreter;
using TcXunit.Parser;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-w5x.15.3: TIME/LTIME literal grammar (T#/TIME#/LTIME#) and their
    // Cell representations (TIME as uint ms, LTIME as ulong ns).
    public class TimeTypeTests
    {
        private static Engine NewEngine() => new Engine(new TypeRegistry(Array.Empty<PouAst>()));

        private static Frame NewFrame() => new Frame(new FbInstance("Test"), "Test");

        [Fact]
        public void ParseExpression_TimePrefixLiteral_ProducesTimeLiteralExprInMilliseconds()
        {
            var expr = Parser.ParseExpression("T#1s500ms");

            var time = Assert.IsType<TimeLiteralExpr>(expr);
            Assert.Equal(1500u, time.Value);
        }

        [Fact]
        public void ParseExpression_TimeKeywordPrefixLiteral_ProducesTimeLiteralExpr()
        {
            var expr = Parser.ParseExpression("TIME#2m3s");

            var time = Assert.IsType<TimeLiteralExpr>(expr);
            Assert.Equal(123_000u, time.Value);
        }

        [Fact]
        public void ParseExpression_TimeLiteralWithOverflowInHighestUnit_IsLegal()
        {
            var expr = Parser.ParseExpression("T#100s12ms");

            var time = Assert.IsType<TimeLiteralExpr>(expr);
            Assert.Equal(100_012u, time.Value);
        }

        [Fact]
        public void ParseExpression_TimeLiteralWithOverflowInLowerUnit_Throws()
        {
            Assert.Throws<FormatException>(() => Parser.ParseExpression("T#5m68s"));
        }

        [Fact]
        public void ParseExpression_TimeLiteralWithReorderedUnits_Throws()
        {
            Assert.Throws<FormatException>(() => Parser.ParseExpression("T#500ms1s"));
        }

        [Fact]
        public void ParseExpression_TimeLiteralWithUppercaseUnits_MatchesLowercaseEquivalent()
        {
            var expr = Parser.ParseExpression("T#25MS");

            var time = Assert.IsType<TimeLiteralExpr>(expr);
            Assert.Equal(25u, time.Value);
        }

        [Fact]
        public void ParseExpression_TimeLiteralWithMixedCaseUnits_MatchesLowercaseEquivalent()
        {
            var expr = Parser.ParseExpression("T#1S500Ms");

            var time = Assert.IsType<TimeLiteralExpr>(expr);
            Assert.Equal(1500u, time.Value);
        }

        [Fact]
        public void ParseExpression_LtimePrefixLiteral_ProducesLtimeLiteralExprInNanoseconds()
        {
            var expr = Parser.ParseExpression("LTIME#1s2us44ns");

            var ltime = Assert.IsType<LtimeLiteralExpr>(expr);
            Assert.Equal(1_000_002_044ul, ltime.Value);
        }

        [Fact]
        public void ParseExpression_LtimeAllowsMicrosecondsAndNanoseconds()
        {
            var expr = Parser.ParseExpression("LT#15us3ns");

            var ltime = Assert.IsType<LtimeLiteralExpr>(expr);
            Assert.Equal(15_003ul, ltime.Value);
        }

        [Fact]
        public void ParseExpression_LtimeLiteralWithUppercaseUnits_MatchesLowercaseEquivalent()
        {
            var expr = Parser.ParseExpression("LTIME#1S2US44NS");

            var ltime = Assert.IsType<LtimeLiteralExpr>(expr);
            Assert.Equal(1_000_002_044ul, ltime.Value);
        }

        [Fact]
        public void Evaluate_TimeLiteral_ReturnsUInt()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("T#250ms"), NewFrame());

            Assert.IsType<uint>(result);
            Assert.Equal(250u, (uint)result);
        }

        [Fact]
        public void Evaluate_LtimeLiteral_ReturnsULong()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("LTIME#1ms"), NewFrame());

            Assert.IsType<ulong>(result);
            Assert.Equal(1_000_000ul, (ulong)result);
        }

        [Fact]
        public void NewInstance_TimeAndLtimeFields_DefaultToZeroOfCorrectClrType()
        {
            var pou = new PouAst(
                "FB_Timing",
                null,
                "VAR\n\ttValue : TIME;\n\tltValue : LTIME;\nEND_VAR",
                "",
                new List<MethodAst>());

            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_Timing");

            Assert.IsType<uint>(instance.Fields["tValue"].Value);
            Assert.Equal(0u, instance.Fields["tValue"].Value);
            Assert.IsType<ulong>(instance.Fields["ltValue"].Value);
            Assert.Equal(0ul, instance.Fields["ltValue"].Value);
        }
    }
}
