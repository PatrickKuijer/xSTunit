using xStunit.Runner.TcUnitStub;
using Xunit;

namespace xStunit.Runner.Tests
{
    // Direct coverage for the ScalarAssertType registry (TcXunit-gd2.11):
    // FB_TestSuite/SuiteHost/NativeMethodBridge all just forward into
    // this table now, so its compare/format behavior for each existing
    // entry (INT/BOOL/STRING/REAL) needs its own tests independent of the
    // interpreter/suite-host plumbing.
    public class ScalarAssertTypeTests
    {
        [Fact]
        public void Int_AreEqual_TrueForSameValue()
        {
            var type = ScalarAssertType.Registry["INT"];

            Assert.True(type.AreEqual(5, 5, null));
        }

        [Fact]
        public void Int_AreEqual_FalseForDifferentValue()
        {
            var type = ScalarAssertType.Registry["INT"];

            Assert.False(type.AreEqual(5, 6, null));
        }

        // IEC INT is a signed 16-bit type - out-of-range values that wrap
        // to the same 16-bit representation compare equal (TcXunit-k28.4).
        [Fact]
        public void Int_AreEqual_TrueForWraparoundCollision()
        {
            var type = ScalarAssertType.Registry["INT"];

            Assert.True(type.AreEqual(32768, -32768, null));
        }

        // TcXunit-vh7: unlike every other integer-family entry in this
        // registry (DINT/UDINT/LINT/etc, all built on AsLong64/WrapXxx),
        // INT used to unbox expected/actual straight to C# int, throwing
        // InvalidCastException (Int64->Int32) whenever a long-boxed value
        // (e.g. a UDINT/DWORD value or long-promoted arithmetic result)
        // reached an INT assert.
        [Fact]
        public void Int_AreEqual_AcceptsLongBoxedValue()
        {
            var type = ScalarAssertType.Registry["INT"];

            Assert.True(type.AreEqual(5L, 5, null));
            Assert.True(type.AreEqual(5, 5L, null));
        }

        [Fact]
        public void Int_Format_AcceptsLongBoxedValue()
        {
            var type = ScalarAssertType.Registry["INT"];

            Assert.Equal("5", type.FormatExpected(5L, null));
            Assert.Equal("5", type.FormatActual(5L));
        }

        [Fact]
        public void Int_HasDelta_IsFalse()
        {
            Assert.False(ScalarAssertType.Registry["INT"].HasDelta);
        }

        [Fact]
        public void Int_Format_UsesWrappedShortValue()
        {
            var type = ScalarAssertType.Registry["INT"];

            Assert.Equal("-32768", type.FormatExpected(32768, null));
            Assert.Equal("-32768", type.FormatActual(32768));
        }

        [Fact]
        public void Bool_AreEqual_TrueForSameValue()
        {
            var type = ScalarAssertType.Registry["BOOL"];

            Assert.True(type.AreEqual(true, true, null));
            Assert.True(type.AreEqual(false, false, null));
        }

        [Fact]
        public void Bool_AreEqual_FalseForDifferentValue()
        {
            var type = ScalarAssertType.Registry["BOOL"];

            Assert.False(type.AreEqual(true, false, null));
        }

        [Fact]
        public void Bool_HasDelta_IsFalse()
        {
            Assert.False(ScalarAssertType.Registry["BOOL"].HasDelta);
        }

        [Fact]
        public void Bool_Format_UsesUpstreamTrueFalseLiterals()
        {
            var type = ScalarAssertType.Registry["BOOL"];

            Assert.Equal("TRUE", type.FormatExpected(true, null));
            Assert.Equal("FALSE", type.FormatActual(false));
        }

        [Fact]
        public void String_AreEqual_TrueForSameValue()
        {
            var type = ScalarAssertType.Registry["STRING"];

            Assert.True(type.AreEqual("abc", "abc", null));
        }

        [Fact]
        public void String_AreEqual_FalseForDifferentValue()
        {
            var type = ScalarAssertType.Registry["STRING"];

            Assert.False(type.AreEqual("abc", "xyz", null));
        }

        [Fact]
        public void String_HasDelta_IsFalse()
        {
            Assert.False(ScalarAssertType.Registry["STRING"].HasDelta);
        }

        [Fact]
        public void String_Format_WrapsValueInQuotes()
        {
            var type = ScalarAssertType.Registry["STRING"];

            Assert.Equal("'abc'", type.FormatExpected("abc", null));
            Assert.Equal("'xyz'", type.FormatActual("xyz"));
        }

        // WSTRING (TcXunit-gd2.4): identical behavior to STRING, since the
        // interpreter has no narrower wide-char representation than C#
        // string - only the registry key differs.
        [Fact]
        public void WString_AreEqual_TrueForSameValue()
        {
            var type = ScalarAssertType.Registry["WSTRING"];

            Assert.True(type.AreEqual("abc", "abc", null));
        }

        [Fact]
        public void WString_AreEqual_FalseForDifferentValue()
        {
            var type = ScalarAssertType.Registry["WSTRING"];

            Assert.False(type.AreEqual("abc", "xyz", null));
        }

        [Fact]
        public void WString_HasDelta_IsFalse()
        {
            Assert.False(ScalarAssertType.Registry["WSTRING"].HasDelta);
        }

        [Fact]
        public void WString_Format_WrapsValueInQuotes()
        {
            var type = ScalarAssertType.Registry["WSTRING"];

            Assert.Equal("'abc'", type.FormatExpected("abc", null));
            Assert.Equal("'xyz'", type.FormatActual("xyz"));
        }

        [Fact]
        public void Real_AreEqual_TrueWithinDelta()
        {
            var type = ScalarAssertType.Registry["REAL"];

            Assert.True(type.AreEqual(1.0, 1.05, 0.1));
        }

        [Fact]
        public void Real_AreEqual_FalseOutsideDelta()
        {
            var type = ScalarAssertType.Registry["REAL"];

            Assert.False(type.AreEqual(1.0, 1.2, 0.1));
        }

        [Fact]
        public void Real_HasDelta_IsTrue()
        {
            Assert.True(ScalarAssertType.Registry["REAL"].HasDelta);
        }

        [Fact]
        public void Real_Format_IncludesDeltaOnExpectedOnly()
        {
            var type = ScalarAssertType.Registry["REAL"];

            // Production formats with InvariantCulture (TcXunit-gd2.7) so
            // the failure message doesn't vary with the running culture's
            // decimal separator - assert against a fixed literal rather
            // than a culture-sensitive interpolation.
            Assert.Equal("1 +/- 0.1", type.FormatExpected(1.0, 0.1));
            Assert.Equal("1.2", type.FormatActual(1.2));
        }

        // Integer-family types (TcXunit-gd2.1). Boundary values exercise
        // each type's own min/max width - deliberately no wraparound cases
        // like INT's (ticket calls for exact equality only, not collision
        // semantics) - plus one mismatch case per type to prove Format*
        // produces the value used in the Fail() message.

        [Theory]
        [InlineData((sbyte)0, (sbyte)0, true)]
        [InlineData(sbyte.MinValue, sbyte.MinValue, true)]
        [InlineData(sbyte.MaxValue, sbyte.MaxValue, true)]
        [InlineData(sbyte.MinValue, sbyte.MaxValue, false)]
        public void Sint_AreEqual(sbyte expected, sbyte actual, bool expectedResult)
        {
            var type = ScalarAssertType.Registry["SINT"];

            Assert.Equal(expectedResult, type.AreEqual(expected, actual, null));
        }

        [Fact]
        public void Sint_HasDelta_IsFalse()
        {
            Assert.False(ScalarAssertType.Registry["SINT"].HasDelta);
        }

        [Fact]
        public void Sint_Format_UsesSignedByteValue()
        {
            var type = ScalarAssertType.Registry["SINT"];

            Assert.Equal("-128", type.FormatExpected(sbyte.MinValue, null));
            Assert.Equal("127", type.FormatActual(sbyte.MaxValue));
        }

        [Theory]
        [InlineData(byte.MinValue, byte.MinValue, true)]
        [InlineData(byte.MaxValue, byte.MaxValue, true)]
        [InlineData(byte.MinValue, byte.MaxValue, false)]
        public void Usint_AreEqual(byte expected, byte actual, bool expectedResult)
        {
            var type = ScalarAssertType.Registry["USINT"];

            Assert.Equal(expectedResult, type.AreEqual(expected, actual, null));
        }

        [Fact]
        public void Usint_HasDelta_IsFalse()
        {
            Assert.False(ScalarAssertType.Registry["USINT"].HasDelta);
        }

        [Fact]
        public void Usint_Format_UsesUnsignedByteValue()
        {
            var type = ScalarAssertType.Registry["USINT"];

            Assert.Equal("0", type.FormatExpected(byte.MinValue, null));
            Assert.Equal("255", type.FormatActual(byte.MaxValue));
        }

        [Theory]
        [InlineData(byte.MinValue, byte.MinValue, true)]
        [InlineData(byte.MaxValue, byte.MaxValue, true)]
        [InlineData(byte.MinValue, byte.MaxValue, false)]
        public void Byte_AreEqual(byte expected, byte actual, bool expectedResult)
        {
            var type = ScalarAssertType.Registry["BYTE"];

            Assert.Equal(expectedResult, type.AreEqual(expected, actual, null));
        }

        [Fact]
        public void Byte_HasDelta_IsFalse()
        {
            Assert.False(ScalarAssertType.Registry["BYTE"].HasDelta);
        }

        [Fact]
        public void Byte_Format_UsesUnsignedByteValue()
        {
            var type = ScalarAssertType.Registry["BYTE"];

            Assert.Equal("0", type.FormatExpected(byte.MinValue, null));
            Assert.Equal("255", type.FormatActual(byte.MaxValue));
        }

        [Theory]
        [InlineData(ushort.MinValue, ushort.MinValue, true)]
        [InlineData(ushort.MaxValue, ushort.MaxValue, true)]
        [InlineData(ushort.MinValue, ushort.MaxValue, false)]
        public void Word_AreEqual(ushort expected, ushort actual, bool expectedResult)
        {
            var type = ScalarAssertType.Registry["WORD"];

            Assert.Equal(expectedResult, type.AreEqual(expected, actual, null));
        }

        [Fact]
        public void Word_HasDelta_IsFalse()
        {
            Assert.False(ScalarAssertType.Registry["WORD"].HasDelta);
        }

        [Fact]
        public void Word_Format_UsesUnsignedShortValue()
        {
            var type = ScalarAssertType.Registry["WORD"];

            Assert.Equal("0", type.FormatExpected(ushort.MinValue, null));
            Assert.Equal("65535", type.FormatActual(ushort.MaxValue));
        }

        [Theory]
        [InlineData(ushort.MinValue, ushort.MinValue, true)]
        [InlineData(ushort.MaxValue, ushort.MaxValue, true)]
        [InlineData(ushort.MinValue, ushort.MaxValue, false)]
        public void Uint_AreEqual(ushort expected, ushort actual, bool expectedResult)
        {
            var type = ScalarAssertType.Registry["UINT"];

            Assert.Equal(expectedResult, type.AreEqual(expected, actual, null));
        }

        [Fact]
        public void Uint_HasDelta_IsFalse()
        {
            Assert.False(ScalarAssertType.Registry["UINT"].HasDelta);
        }

        [Fact]
        public void Uint_Format_UsesUnsignedShortValue()
        {
            var type = ScalarAssertType.Registry["UINT"];

            Assert.Equal("0", type.FormatExpected(ushort.MinValue, null));
            Assert.Equal("65535", type.FormatActual(ushort.MaxValue));
        }

        // AssertEquals_DINT is highest priority (failing in a real
        // production suite today per TcXunit-gd2.1) - covered here plus an
        // E2E interpreter test in NativeMethodBridgeAssertTests.
        [Theory]
        [InlineData(0, 0, true)]
        [InlineData(int.MinValue, int.MinValue, true)]
        [InlineData(int.MaxValue, int.MaxValue, true)]
        [InlineData(int.MinValue, int.MaxValue, false)]
        public void Dint_AreEqual(int expected, int actual, bool expectedResult)
        {
            var type = ScalarAssertType.Registry["DINT"];

            Assert.Equal(expectedResult, type.AreEqual(expected, actual, null));
        }

        [Fact]
        public void Dint_HasDelta_IsFalse()
        {
            Assert.False(ScalarAssertType.Registry["DINT"].HasDelta);
        }

        [Fact]
        public void Dint_Format_UsesIntValue()
        {
            var type = ScalarAssertType.Registry["DINT"];

            Assert.Equal(int.MinValue.ToString(), type.FormatExpected(int.MinValue, null));
            Assert.Equal(int.MaxValue.ToString(), type.FormatActual(int.MaxValue));
        }

        [Theory]
        [InlineData(uint.MinValue, uint.MinValue, true)]
        [InlineData(uint.MaxValue, uint.MaxValue, true)]
        [InlineData(uint.MinValue, uint.MaxValue, false)]
        public void Dword_AreEqual(uint expected, uint actual, bool expectedResult)
        {
            var type = ScalarAssertType.Registry["DWORD"];

            Assert.Equal(expectedResult, type.AreEqual(expected, actual, null));
        }

        [Fact]
        public void Dword_HasDelta_IsFalse()
        {
            Assert.False(ScalarAssertType.Registry["DWORD"].HasDelta);
        }

        [Fact]
        public void Dword_Format_UsesUnsignedIntValue()
        {
            var type = ScalarAssertType.Registry["DWORD"];

            Assert.Equal("0", type.FormatExpected(uint.MinValue, null));
            Assert.Equal(uint.MaxValue.ToString(), type.FormatActual(uint.MaxValue));
        }

        [Theory]
        [InlineData(uint.MinValue, uint.MinValue, true)]
        [InlineData(uint.MaxValue, uint.MaxValue, true)]
        [InlineData(uint.MinValue, uint.MaxValue, false)]
        public void Udint_AreEqual(uint expected, uint actual, bool expectedResult)
        {
            var type = ScalarAssertType.Registry["UDINT"];

            Assert.Equal(expectedResult, type.AreEqual(expected, actual, null));
        }

        [Fact]
        public void Udint_HasDelta_IsFalse()
        {
            Assert.False(ScalarAssertType.Registry["UDINT"].HasDelta);
        }

        [Fact]
        public void Udint_Format_UsesUnsignedIntValue()
        {
            var type = ScalarAssertType.Registry["UDINT"];

            Assert.Equal("0", type.FormatExpected(uint.MinValue, null));
            Assert.Equal(uint.MaxValue.ToString(), type.FormatActual(uint.MaxValue));
        }

        [Theory]
        [InlineData(0L, 0L, true)]
        [InlineData(long.MinValue, long.MinValue, true)]
        [InlineData(long.MaxValue, long.MaxValue, true)]
        [InlineData(long.MinValue, long.MaxValue, false)]
        public void Lint_AreEqual(long expected, long actual, bool expectedResult)
        {
            var type = ScalarAssertType.Registry["LINT"];

            Assert.Equal(expectedResult, type.AreEqual(expected, actual, null));
        }

        [Fact]
        public void Lint_HasDelta_IsFalse()
        {
            Assert.False(ScalarAssertType.Registry["LINT"].HasDelta);
        }

        [Fact]
        public void Lint_Format_UsesLongValue()
        {
            var type = ScalarAssertType.Registry["LINT"];

            Assert.Equal(long.MinValue.ToString(), type.FormatExpected(long.MinValue, null));
            Assert.Equal(long.MaxValue.ToString(), type.FormatActual(long.MaxValue));
        }

        [Theory]
        [InlineData(ulong.MinValue, ulong.MinValue, true)]
        [InlineData(ulong.MaxValue, ulong.MaxValue, true)]
        [InlineData(ulong.MinValue, ulong.MaxValue, false)]
        public void Lword_AreEqual(ulong expected, ulong actual, bool expectedResult)
        {
            var type = ScalarAssertType.Registry["LWORD"];

            Assert.Equal(expectedResult, type.AreEqual(expected, actual, null));
        }

        [Fact]
        public void Lword_HasDelta_IsFalse()
        {
            Assert.False(ScalarAssertType.Registry["LWORD"].HasDelta);
        }

        [Fact]
        public void Lword_Format_UsesUnsignedLongValue()
        {
            var type = ScalarAssertType.Registry["LWORD"];

            Assert.Equal("0", type.FormatExpected(ulong.MinValue, null));
            Assert.Equal(ulong.MaxValue.ToString(), type.FormatActual(ulong.MaxValue));
        }

        [Theory]
        [InlineData(ulong.MinValue, ulong.MinValue, true)]
        [InlineData(ulong.MaxValue, ulong.MaxValue, true)]
        [InlineData(ulong.MinValue, ulong.MaxValue, false)]
        public void Ulint_AreEqual(ulong expected, ulong actual, bool expectedResult)
        {
            var type = ScalarAssertType.Registry["ULINT"];

            Assert.Equal(expectedResult, type.AreEqual(expected, actual, null));
        }

        [Fact]
        public void Ulint_HasDelta_IsFalse()
        {
            Assert.False(ScalarAssertType.Registry["ULINT"].HasDelta);
        }

        [Fact]
        public void Ulint_Format_UsesUnsignedLongValue()
        {
            var type = ScalarAssertType.Registry["ULINT"];

            Assert.Equal("0", type.FormatExpected(ulong.MinValue, null));
            Assert.Equal(ulong.MaxValue.ToString(), type.FormatActual(ulong.MaxValue));
        }

        // Interpreted ST integer literals are always boxed C# int
        // (Parser.Expressions.cs IntLiteralExpr), regardless of which
        // integer-family type the call targets - the registry entries must
        // accept boxed int for the wider (long/ulong-backed) types too, not
        // just their own natural CLR box (TcXunit-gd2.1).
        [Fact]
        public void Lint_AreEqual_AcceptsBoxedIntFromInterpretedLiteral()
        {
            var type = ScalarAssertType.Registry["LINT"];

            Assert.True(type.AreEqual(100, 100, null));
        }

        [Fact]
        public void Ulint_AreEqual_AcceptsBoxedIntFromInterpretedLiteral()
        {
            var type = ScalarAssertType.Registry["ULINT"];

            Assert.True(type.AreEqual(100, 100, null));
        }

        [Fact]
        public void Dword_AreEqual_AcceptsBoxedLongFromCell()
        {
            var type = ScalarAssertType.Registry["DWORD"];

            Assert.True(type.AreEqual(100L, 100L, null));
        }

        // LREAL (TcXunit-gd2.2): the 64-bit delta-based twin of REAL - same
        // compare/format logic, but at double precision throughout.
        [Fact]
        public void Lreal_AreEqual_TrueWithinDelta()
        {
            var type = ScalarAssertType.Registry["LREAL"];

            Assert.True(type.AreEqual(1.0, 1.05, 0.1));
        }

        [Fact]
        public void Lreal_AreEqual_FalseOutsideDelta()
        {
            var type = ScalarAssertType.Registry["LREAL"];

            Assert.False(type.AreEqual(1.0, 1.2, 0.1));
        }

        [Fact]
        public void Lreal_HasDelta_IsTrue()
        {
            Assert.True(ScalarAssertType.Registry["LREAL"].HasDelta);
        }

        [Fact]
        public void Lreal_Format_IncludesDeltaOnExpectedOnly()
        {
            var type = ScalarAssertType.Registry["LREAL"];

            // Production formats with InvariantCulture (TcXunit-gd2.7); see
            // the matching REAL test above.
            Assert.Equal("1 +/- 0.1", type.FormatExpected(1.0, 0.1));
            Assert.Equal("1.2", type.FormatActual(1.2));
        }

        // TIME (TcXunit-gd2.3): boxed C# uint milliseconds per
        // TimeLiteral.ParseTimeMs, compared exactly (no Delta).
        [Theory]
        [InlineData(0u, 0u, true)]
        [InlineData(uint.MaxValue, uint.MaxValue, true)]
        [InlineData(0u, uint.MaxValue, false)]
        [InlineData(1_500u, 1_500u, true)]
        [InlineData(1_500u, 1_501u, false)]
        public void Time_AreEqual(uint expected, uint actual, bool expectedResult)
        {
            var type = ScalarAssertType.Registry["TIME"];

            Assert.Equal(expectedResult, type.AreEqual(expected, actual, null));
        }

        [Fact]
        public void Time_HasDelta_IsFalse()
        {
            Assert.False(ScalarAssertType.Registry["TIME"].HasDelta);
        }

        [Fact]
        public void Time_Format_UsesUnsignedIntValue()
        {
            var type = ScalarAssertType.Registry["TIME"];

            Assert.Equal("0", type.FormatExpected(uint.MinValue, null));
            Assert.Equal(uint.MaxValue.ToString(), type.FormatActual(uint.MaxValue));
        }

        [Fact]
        public void Time_AreEqual_AcceptsBoxedIntFromInterpretedLiteral()
        {
            var type = ScalarAssertType.Registry["TIME"];

            Assert.True(type.AreEqual(100, 100, null));
        }

        // LTIME (TcXunit-gd2.3): boxed C# ulong nanoseconds per
        // TimeLiteral.ParseLTimeNs, compared exactly (no Delta).
        [Theory]
        [InlineData(0ul, 0ul, true)]
        [InlineData(ulong.MaxValue, ulong.MaxValue, true)]
        [InlineData(0ul, ulong.MaxValue, false)]
        [InlineData(1_000_002_044ul, 1_000_002_044ul, true)]
        [InlineData(1_000_002_044ul, 1_000_002_045ul, false)]
        public void Ltime_AreEqual(ulong expected, ulong actual, bool expectedResult)
        {
            var type = ScalarAssertType.Registry["LTIME"];

            Assert.Equal(expectedResult, type.AreEqual(expected, actual, null));
        }

        [Fact]
        public void Ltime_HasDelta_IsFalse()
        {
            Assert.False(ScalarAssertType.Registry["LTIME"].HasDelta);
        }

        [Fact]
        public void Ltime_Format_UsesUnsignedLongValue()
        {
            var type = ScalarAssertType.Registry["LTIME"];

            Assert.Equal("0", type.FormatExpected(ulong.MinValue, null));
            Assert.Equal(ulong.MaxValue.ToString(), type.FormatActual(ulong.MaxValue));
        }

        [Fact]
        public void Ltime_AreEqual_AcceptsBoxedIntFromInterpretedLiteral()
        {
            var type = ScalarAssertType.Registry["LTIME"];

            Assert.True(type.AreEqual(100, 100, null));
        }

        // DATE/DATE_AND_TIME/TIME_OF_DAY (TcXunit-gd2.13): all boxed C#
        // uint per DateTimeLiteral.cs, compared exactly (no Delta), same
        // shape as TIME above.
        [Theory]
        [InlineData(0u, 0u, true)]
        [InlineData(uint.MaxValue, uint.MaxValue, true)]
        [InlineData(0u, uint.MaxValue, false)]
        [InlineData(1_704_067_200u, 1_704_067_200u, true)]
        [InlineData(1_704_067_200u, 1_704_067_201u, false)]
        public void Date_AreEqual(uint expected, uint actual, bool expectedResult)
        {
            var type = ScalarAssertType.Registry["DATE"];

            Assert.Equal(expectedResult, type.AreEqual(expected, actual, null));
        }

        [Fact]
        public void Date_HasDelta_IsFalse()
        {
            Assert.False(ScalarAssertType.Registry["DATE"].HasDelta);
        }

        [Fact]
        public void Date_Format_UsesUnsignedIntValue()
        {
            var type = ScalarAssertType.Registry["DATE"];

            Assert.Equal("0", type.FormatExpected(uint.MinValue, null));
            Assert.Equal(uint.MaxValue.ToString(), type.FormatActual(uint.MaxValue));
        }

        [Theory]
        [InlineData(0u, 0u, true)]
        [InlineData(uint.MaxValue, uint.MaxValue, true)]
        [InlineData(0u, uint.MaxValue, false)]
        [InlineData(1_704_103_200u, 1_704_103_200u, true)]
        [InlineData(1_704_103_200u, 1_704_103_201u, false)]
        public void DateAndTime_AreEqual(uint expected, uint actual, bool expectedResult)
        {
            var type = ScalarAssertType.Registry["DATE_AND_TIME"];

            Assert.Equal(expectedResult, type.AreEqual(expected, actual, null));
        }

        [Fact]
        public void DateAndTime_HasDelta_IsFalse()
        {
            Assert.False(ScalarAssertType.Registry["DATE_AND_TIME"].HasDelta);
        }

        [Fact]
        public void DateAndTime_Format_UsesUnsignedIntValue()
        {
            var type = ScalarAssertType.Registry["DATE_AND_TIME"];

            Assert.Equal("0", type.FormatExpected(uint.MinValue, null));
            Assert.Equal(uint.MaxValue.ToString(), type.FormatActual(uint.MaxValue));
        }

        [Theory]
        [InlineData(0u, 0u, true)]
        [InlineData(uint.MaxValue, uint.MaxValue, true)]
        [InlineData(0u, uint.MaxValue, false)]
        [InlineData(36_000_000u, 36_000_000u, true)]
        [InlineData(36_000_000u, 36_000_500u, false)]
        public void TimeOfDay_AreEqual(uint expected, uint actual, bool expectedResult)
        {
            var type = ScalarAssertType.Registry["TIME_OF_DAY"];

            Assert.Equal(expectedResult, type.AreEqual(expected, actual, null));
        }

        [Fact]
        public void TimeOfDay_HasDelta_IsFalse()
        {
            Assert.False(ScalarAssertType.Registry["TIME_OF_DAY"].HasDelta);
        }

        [Fact]
        public void TimeOfDay_Format_UsesUnsignedIntValue()
        {
            var type = ScalarAssertType.Registry["TIME_OF_DAY"];

            Assert.Equal("0", type.FormatExpected(uint.MinValue, null));
            Assert.Equal(uint.MaxValue.ToString(), type.FormatActual(uint.MaxValue));
        }
    }
}
