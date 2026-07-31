using xStunit.Interpreter;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // TcXunit-mvbk: direct unit tests for the elementary non-numeric
    // type-name -> default value provider (one per branch, plus
    // case-insensitivity and the unknown-type miss).
    public class ElementaryDefaultTests
    {
        [Fact]
        public void TryGetDefault_Bool_ReturnsFalse()
        {
            Assert.True(IecElementaryDefault.TryGetDefault("BOOL", out var value));
            Assert.Equal(false, value);
        }

        [Fact]
        public void TryGetDefault_Time_ReturnsUintZero()
        {
            Assert.True(IecElementaryDefault.TryGetDefault("TIME", out var value));
            Assert.Equal(0u, value);
        }

        [Fact]
        public void TryGetDefault_Ltime_ReturnsUlongZero()
        {
            Assert.True(IecElementaryDefault.TryGetDefault("LTIME", out var value));
            Assert.Equal(0ul, value);
        }

        [Theory]
        [InlineData("DATE")]
        [InlineData("DATE_AND_TIME")]
        [InlineData("TIME_OF_DAY")]
        public void TryGetDefault_DateFamily_ReturnsUintZero(string typeName)
        {
            Assert.True(IecElementaryDefault.TryGetDefault(typeName, out var value));
            Assert.Equal(0u, value);
        }

        [Theory]
        [InlineData("STRING")]
        [InlineData("WSTRING")]
        [InlineData("STRING(80)")]
        [InlineData("WSTRING(80)")]
        [InlineData("STRING(cScratchConstants.MAX_STRING_SIZE)")]
        public void TryGetDefault_StringTypes_ReturnsEmptyString(string typeName)
        {
            Assert.True(IecElementaryDefault.TryGetDefault(typeName, out var value));
            Assert.Equal("", value);
        }

        [Theory]
        [InlineData("bool", false)]
        [InlineData("time", 0u)]
        [InlineData("lTime", 0ul)]
        [InlineData("Date", 0u)]
        [InlineData("date_and_time", 0u)]
        [InlineData("Time_Of_Day", 0u)]
        [InlineData("string", "")]
        [InlineData("wString(80)", "")]
        public void TryGetDefault_LowerOrMixedCaseTypeName_MatchesCaseInsensitively(string typeName, object expected)
        {
            Assert.True(IecElementaryDefault.TryGetDefault(typeName, out var value));
            Assert.Equal(expected, value);
        }

        [Theory]
        [InlineData("INT")]
        [InlineData("LREAL")]
        [InlineData("FB_MySuite")]
        [InlineData("POINTER TO BOOL")]
        [InlineData("ARRAY[0..3] OF BOOL")]
        [InlineData("")]
        [InlineData(null)]
        public void TryGetDefault_UnknownTypeName_ReturnsFalseAndNull(string typeName)
        {
            Assert.False(IecElementaryDefault.TryGetDefault(typeName, out var value));
            Assert.Null(value);
        }
    }
}
