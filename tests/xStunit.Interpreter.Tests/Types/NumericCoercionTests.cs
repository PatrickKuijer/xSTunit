using System;
using xStunit.Interpreter;
using Xunit;

namespace xStunit.Interpreter.Tests
{
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

        // No IEC widening rule mixes a 64-bit integer with float/double, so
        // promotion throws rather than silently dropping bits of a
        // LINT/UDINT/DWORD value into a narrower mantissa.
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

        // ULINT/LWORD box as ulong and LINT/UDINT/DWORD as long; no widening
        // rule spans the two, so they need an explicit cast to mix.
        [Fact]
        public void Promote_LongAndULong_Throws()
        {
            Assert.Throws<NotSupportedException>(() => NumericCoercion.Promote(1L, 2UL));
        }

        // TIME/DATE/DATE_AND_TIME/TIME_OF_DAY all box as uint, so comparing
        // or adding two of them - or one against a plain INT literal - lands
        // on these promotions rather than the int/long ones.
        [Fact]
        public void Promote_UIntAndUInt_WidensBothToLong()
        {
            var (left, right) = NumericCoercion.Promote(1u, 2u);

            Assert.IsType<long>(left);
            Assert.IsType<long>(right);
            Assert.Equal(1L, left);
            Assert.Equal(2L, right);
        }

        [Fact]
        public void Promote_IntAndUInt_WidensBothToLong()
        {
            var (left, right) = NumericCoercion.Promote(1, 2u);

            Assert.IsType<long>(left);
            Assert.IsType<long>(right);
            Assert.Equal(1L, left);
            Assert.Equal(2L, right);
        }

        [Fact]
        public void ToLong_UInt_Widens()
        {
            Assert.Equal(5L, NumericCoercion.ToLong(5u));
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
