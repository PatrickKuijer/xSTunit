using System.Globalization;
using System.Threading;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TwinCAT writes a REAL or LREAL as text that still reads as one: a
    // whole-valued 19.0 comes back '19.0', never '19'. Dropping the point meant
    // every REAL_TO_STRING-shaped assertion in every suite was being checked
    // against a shape the target runtime does not produce - as likely to go red
    // on correct code as green on wrong code.
    //
    // Digit COUNT and exponent parity with TwinCAT are deliberately NOT pinned
    // here: settling those needs a hardware capture this has not had, so the
    // round-trippable shortest form is left as it stands and only the missing
    // decimal point is supplied.
    public class RealToStringFormatTests
    {
        private static Engine NewEngine() => new Engine(new TypeRegistry(System.Array.Empty<PouAst>()));

        private static Frame NewFrame() => new Frame(new FbInstance("Test"), "Test");

        private static string Eval(string expression) =>
            Assert.IsType<string>(NewEngine().Evaluate(Parser.ParseExpression(expression), NewFrame()));

        [Theory]
        [InlineData("REAL_TO_STRING(REAL#19.0)", "19.0")]
        [InlineData("REAL_TO_STRING(-REAL#19.0)", "-19.0")]
        [InlineData("REAL_TO_STRING(REAL#0.0)", "0.0")]
        [InlineData("LREAL_TO_STRING(LREAL#19.0)", "19.0")]
        [InlineData("LREAL_TO_STRING(-LREAL#19.0)", "-19.0")]
        [InlineData("LREAL_TO_STRING(LREAL#0.0)", "0.0")]
        public void Evaluate_WholeValuedReal_KeepsItsFractionalPart(string expression, string expected)
        {
            Assert.Equal(expected, Eval(expression));
        }

        [Theory]
        [InlineData("REAL_TO_STRING(REAL#3.5)", "3.5")]
        [InlineData("REAL_TO_STRING(-REAL#0.25)", "-0.25")]
        [InlineData("LREAL_TO_STRING(LREAL#2.25)", "2.25")]
        [InlineData("LREAL_TO_STRING(-1.5)", "-1.5")]
        public void Evaluate_FractionalReal_IsUnchanged(string expression, string expected)
        {
            Assert.Equal(expected, Eval(expression));
        }

        // An integer conversion is not a real one and must not grow a point:
        // INT_TO_STRING(19) is '19' on any runtime.
        [Theory]
        [InlineData("INT_TO_STRING(19)", "19")]
        [InlineData("DINT_TO_STRING(-7)", "-7")]
        public void Evaluate_IntegerToString_StaysWhole(string expression, string expected)
        {
            Assert.Equal(expected, Eval(expression));
        }

        // A de-DE CurrentCulture would otherwise render the separator as ',',
        // which no PLC ever writes and no STRING_TO_REAL here would read back.
        [Fact]
        public void Evaluate_RealToString_UnderACommaDecimalCulture_StillWritesAPoint()
        {
            var original = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                Assert.Equal("19.0", Eval("REAL_TO_STRING(REAL#19.0)"));
                Assert.Equal("3.5", Eval("REAL_TO_STRING(REAL#3.5)"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        // NaN and the infinities carry no decimal point and must not be handed
        // one: 'NaN.0' is not a number in any notation.
        [Theory]
        [InlineData("REAL_TO_STRING(REAL#0.0 / REAL#0.0)")]
        [InlineData("REAL_TO_STRING(REAL#1.0 / REAL#0.0)")]
        public void Evaluate_NonFiniteReal_IsNotGivenAFractionalPart(string expression)
        {
            Assert.DoesNotContain(".0", Eval(expression));
        }
    }
}
