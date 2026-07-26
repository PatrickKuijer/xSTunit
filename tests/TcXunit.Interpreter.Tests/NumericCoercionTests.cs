using System;
using TcXunit.Interpreter;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    // TcXunit-6af.2: direct unit tests for the shared promotion/narrowing
    // rule, exercised as plain CLR values in/out - no Engine/Frame/FbInstance
    // setup needed, unlike EvaluateBinary/CoerceForAssignment's own tests
    // (NumericTypeTests) which drive it through the interpreter end-to-end.
    public class NumericCoercionTests
    {
        [Theory]
        [InlineData(1, 2)]
        public void Promote_BothInt_StaysInt(int a, int b)
        {
            var (left, right) = NumericCoercion.Promote(a, b);

            Assert.IsType<int>(left);
            Assert.IsType<int>(right);
        }

        [Fact]
        public void Promote_IntAndLong_WidensBothToLong()
        {
            var (left, right) = NumericCoercion.Promote(1, 2L);

            Assert.IsType<long>(left);
            Assert.IsType<long>(right);
            Assert.Equal(1L, left);
            Assert.Equal(2L, right);
        }

        [Fact]
        public void Promote_IntAndFloat_WidensBothToFloat()
        {
            var (left, right) = NumericCoercion.Promote(1, 2f);

            Assert.IsType<float>(left);
            Assert.IsType<float>(right);
        }

        [Fact]
        public void Promote_AnyOperandDouble_WidensBothToDouble()
        {
            var (left, right) = NumericCoercion.Promote(1, 2d);

            Assert.IsType<double>(left);
            Assert.IsType<double>(right);
        }

        // long mixed with float/double has no IEC widening rule defined yet
        // (out of scope for this extraction - ToDouble/ToFloat only accept
        // the same inputs the pre-extraction private helpers did) and throws
        // rather than silently narrowing a 64-bit LINT/UDINT/DWORD value into
        // a float/double mantissa.
        [Fact]
        public void Promote_LongAndDouble_Throws()
        {
            Assert.Throws<NotSupportedException>(() => NumericCoercion.Promote(1L, 2d));
        }

        [Fact]
        public void Promote_LongAndFloat_Throws()
        {
            Assert.Throws<NotSupportedException>(() => NumericCoercion.Promote(1L, 2f));
        }

        [Fact]
        public void ToDouble_Long_Throws()
        {
            Assert.Throws<NotSupportedException>(() => NumericCoercion.ToDouble(5L));
        }

        [Fact]
        public void ToFloat_Long_Throws()
        {
            Assert.Throws<NotSupportedException>(() => NumericCoercion.ToFloat(5L));
        }

        [Fact]
        public void ToLong_Int_Widens()
        {
            Assert.Equal(5L, NumericCoercion.ToLong(5));
        }

        [Fact]
        public void ToLong_UnsupportedType_Throws()
        {
            Assert.Throws<NotSupportedException>(() => NumericCoercion.ToLong(5f));
        }

        [Fact]
        public void Promote_IntAndULong_WidensBothToULong()
        {
            var (left, right) = NumericCoercion.Promote(1, 2UL);

            Assert.IsType<ulong>(left);
            Assert.IsType<ulong>(right);
            Assert.Equal(1UL, left);
            Assert.Equal(2UL, right);
        }

        // long and ulong have no IEC widening rule defined between them - a
        // ULINT/LWORD (ulong) can't mix with a LINT/UDINT/DWORD (long)
        // without an explicit cast, mirroring long+float/double above.
        [Fact]
        public void Promote_LongAndULong_Throws()
        {
            Assert.Throws<NotSupportedException>(() => NumericCoercion.Promote(1L, 2UL));
        }

        [Fact]
        public void ToULong_Int_Widens()
        {
            Assert.Equal(5UL, NumericCoercion.ToULong(5));
        }

        [Fact]
        public void ToULong_NegativeInt_Throws()
        {
            Assert.Throws<NotSupportedException>(() => NumericCoercion.ToULong(-1));
        }

        [Fact]
        public void ToULong_UnsupportedType_Throws()
        {
            Assert.Throws<NotSupportedException>(() => NumericCoercion.ToULong(5f));
        }

        [Fact]
        public void CoerceForAssignment_ULongExistingIntIncoming_WidensToULong()
        {
            var result = NumericCoercion.CoerceForAssignment(0UL, 5);

            Assert.IsType<ulong>(result);
            Assert.Equal(5UL, result);
        }

        [Fact]
        public void CoerceForAssignment_ULongExistingNegativeIntIncoming_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => NumericCoercion.CoerceForAssignment(0UL, -1));
        }

        [Fact]
        public void CoerceForAssignment_IntExistingFloatIncoming_ThrowsNarrowingError()
        {
            Assert.Throws<InvalidOperationException>(() => NumericCoercion.CoerceForAssignment(0, 1.5f));
        }

        [Fact]
        public void CoerceForAssignment_IntExistingDoubleIncoming_ThrowsNarrowingError()
        {
            Assert.Throws<InvalidOperationException>(() => NumericCoercion.CoerceForAssignment(0, 1.5d));
        }

        [Fact]
        public void CoerceForAssignment_FloatExistingDoubleIncoming_ThrowsNarrowingError()
        {
            Assert.Throws<InvalidOperationException>(() => NumericCoercion.CoerceForAssignment(0f, 1.5d));
        }

        [Fact]
        public void CoerceForAssignment_FloatExistingIntIncoming_WidensToFloat()
        {
            var result = NumericCoercion.CoerceForAssignment(0f, 5);

            Assert.IsType<float>(result);
            Assert.Equal(5f, result);
        }

        [Fact]
        public void CoerceForAssignment_DoubleExistingIntIncoming_WidensToDouble()
        {
            var result = NumericCoercion.CoerceForAssignment(0d, 5);

            Assert.IsType<double>(result);
            Assert.Equal(5d, result);
        }

        [Fact]
        public void CoerceForAssignment_DoubleExistingFloatIncoming_WidensToDouble()
        {
            var result = NumericCoercion.CoerceForAssignment(0d, 5f);

            Assert.IsType<double>(result);
            Assert.Equal(5d, result);
        }

        [Fact]
        public void CoerceForAssignment_LongExistingIntIncoming_WidensToLong()
        {
            var result = NumericCoercion.CoerceForAssignment(0L, 5);

            Assert.IsType<long>(result);
            Assert.Equal(5L, result);
        }

        [Fact]
        public void CoerceForAssignment_IntExistingIntIncoming_PassesThrough()
        {
            var result = NumericCoercion.CoerceForAssignment(0, 5);

            Assert.IsType<int>(result);
            Assert.Equal(5, result);
        }
    }
}
