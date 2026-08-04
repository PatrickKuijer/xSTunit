using System;
using System.Collections.Generic;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TIME boxes as uint milliseconds; LTIME as ulong nanoseconds.
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
            Assert.Throws<ParseException>(() => Parser.ParseExpression("T#5m68s"));
        }

        [Fact]
        public void ParseExpression_TimeLiteralWithReorderedUnits_Throws()
        {
            Assert.Throws<ParseException>(() => Parser.ParseExpression("T#500ms1s"));
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

        // Comparison operands go through numeric promotion, which has to
        // handle the boxed uint a TIME value arrives as.
        [Fact]
        public void Evaluate_TimeLiteralEqualsTimeLiteral_ReturnsTrueWithoutThrowing()
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Parser.ParseExpression("T#1s = T#1s"), NewFrame());

            Assert.IsType<bool>(result);
            Assert.True((bool)result);
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

        // Arithmetic widens a TIME operand to long to do the sum, but the
        // declared type is what the variable holds afterwards: a TIME that has
        // been added to must be indistinguishable from one that has not.
        [Fact]
        public void Assign_SumOfTwoTimeValuesToTimeVariable_NarrowsBackToUInt()
        {
            var instance = RunBody(
                "VAR\n\tlhs : TIME := T#1s;\n\trhs : TIME := T#500ms;\n\tsum : TIME;\nEND_VAR",
                "sum := lhs + rhs;");

            Assert.IsType<uint>(instance.Fields["sum"].Value);
            Assert.Equal(1500u, instance.Fields["sum"].Value);
        }

        // TIME is a 32-bit millisecond counter on the target, so a sum past
        // T#49d17h2m47s295ms rolls over rather than growing into a 64-bit
        // total. A fixture that drives a meter over the boundary has to see the
        // same rollover here that it would see on the PLC.
        [Fact]
        public void Assign_TimeSumPastThe32BitBoundary_WrapsRatherThanWidening()
        {
            var instance = RunBody(
                "VAR\n\tnearMax : TIME := T#49d17h2m47s295ms;\n\tstep : TIME := T#5ms;\n\tsum : TIME;\nEND_VAR",
                "sum := nearMax + step;");

            Assert.IsType<uint>(instance.Fields["sum"].Value);
            Assert.Equal(4u, instance.Fields["sum"].Value);
        }

        // The other direction of the same 32-bit rule: subtracting a longer
        // TIME from a shorter one rolls under to the top of the range, which is
        // how an unguarded "now - then" reads on the target.
        [Fact]
        public void Assign_TimeDifferenceBelowZero_WrapsToTheTopOfTheRange()
        {
            var instance = RunBody(
                "VAR\n\tsmall : TIME := T#1ms;\n\tlarge : TIME := T#3ms;\n\tdiff : TIME;\nEND_VAR",
                "diff := small - large;");

            Assert.IsType<uint>(instance.Fields["diff"].Value);
            Assert.Equal(uint.MaxValue - 1u, instance.Fields["diff"].Value);
        }

        // LTIME is 64 bits wide and already boxes as ulong, so its arithmetic
        // has no narrowing step to get wrong - pinned so the TIME rule above is
        // never generalized onto it.
        [Fact]
        public void Assign_SumOfTwoLtimeValuesToLtimeVariable_StaysULong()
        {
            var instance = RunBody(
                "VAR\n\tlhs : LTIME := LTIME#1s;\n\trhs : LTIME := LTIME#1s;\n\tsum : LTIME;\nEND_VAR",
                "sum := lhs + rhs;");

            Assert.IsType<ulong>(instance.Fields["sum"].Value);
            Assert.Equal(2_000_000_000ul, instance.Fields["sum"].Value);
        }

        private static FbInstance RunBody(string declarations, string body)
        {
            var pou = new PouAst("FB_Timing", null, declarations, body, new List<MethodAst>());
            var engine = new Engine(new TypeRegistry(new[] { pou }));
            var instance = engine.NewInstance("FB_Timing");
            engine.CallMethod(
                instance, "StepCycles", new Expr[] { new IntLiteralExpr(1) }, new List<NamedArg>(), null, null);
            return instance;
        }
    }
}
