using TcXunit.Runner.TcUnitStub;
using Xunit;

namespace TcXunit.Runner.Tests
{
    // Direct coverage for the ScalarAssertType registry (TcXunit-gd2.11):
    // FB_TestSuite/TcUnitSuiteHost/NativeMethodBridge all just forward into
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

            // Matches production's own $"{value}" interpolation rather than
            // a hardcoded literal, so this doesn't depend on the running
            // culture's decimal separator (same culture-sensitivity the
            // pre-existing AssertEquals_REAL formatting already had).
            Assert.Equal($"{1.0} +/- {0.1}", type.FormatExpected(1.0, 0.1));
            Assert.Equal($"{1.2}", type.FormatActual(1.2));
        }
    }
}
