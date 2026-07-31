using System;
using xStunit.Interpreter;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // STRING comparison needs a branch of its own; without one the operands
    // reach the numeric path, where the int cast turns an everyday ST
    // comparison into a raw InvalidCastException. BoolLiteralTests pins the
    // same rule for BOOL.
    public class StringComparisonTests
    {
        private static Engine NewEngine() => new Engine(new TypeRegistry(Array.Empty<PouAst>()));

        private static Frame NewFrame() => new Frame(new FbInstance("Test"), "Test");

        [Theory]
        [InlineData("'abc' = 'abc'", true)]
        [InlineData("'abc' = 'abd'", false)]
        [InlineData("'abc' <> 'abd'", true)]
        [InlineData("'abc' <> 'abc'", false)]
        public void Evaluate_StringEqualityBetweenTwoStrings_ReturnsBoxedBool(string source, bool expected)
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Interpreter.Parser.ParseExpression(source), NewFrame());

            Assert.IsType<bool>(result);
            Assert.Equal(expected, (bool)result);
        }

        // IEC 61131-3 defines ordering for ANY_STRING as ordinal comparison;
        // TwinCAT compares string data byte-by-byte.
        [Theory]
        [InlineData("'abc' < 'abd'", true)]
        [InlineData("'abd' < 'abc'", false)]
        [InlineData("'abc' > 'abd'", false)]
        [InlineData("'abd' > 'abc'", true)]
        [InlineData("'abc' <= 'abc'", true)]
        [InlineData("'abc' >= 'abc'", true)]
        public void Evaluate_StringOrderingComparison_UsesOrdinalComparison(string source, bool expected)
        {
            var engine = NewEngine();
            var result = engine.Evaluate(Interpreter.Parser.ParseExpression(source), NewFrame());

            Assert.IsType<bool>(result);
            Assert.Equal(expected, (bool)result);
        }

        // A STRING against a non-STRING has no IEC 61131-3 meaning, so the
        // failure names the operator and both types instead of surfacing as an
        // InvalidCastException from the numeric path.
        [Fact]
        public void Evaluate_StringEqualsInt_ThrowsNotSupportedException()
        {
            var engine = NewEngine();

            var ex = Assert.Throws<NotSupportedException>(
                () => engine.Evaluate(Interpreter.Parser.ParseExpression("'abc' = 1"), NewFrame()));

            Assert.Contains("=", ex.Message);
            Assert.Contains("String", ex.Message);
            Assert.Contains("Int32", ex.Message);
        }
    }
}
