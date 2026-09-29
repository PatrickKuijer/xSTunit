using System.Collections.Generic;
using xStunit.Interpreter;
using Xunit;

namespace xStunit.Interpreter.Tests.Types
{
    public class IecIdentifierTests
    {
        // Dispatch switches name each case once, in its declared spelling; if
        // Canonical echoed the call-site spelling instead, 'adr(x)' would miss
        // the "ADR" case label and fall through to an unknown-method fault.
        [Theory]
        [InlineData("adr", "ADR")]
        [InlineData("AdvanceClock", "AdvanceClock")]
        [InlineData("ADVANCECLOCK", "AdvanceClock")]
        public void Canonical_NameInAnyCase_ReturnsTheDeclaredSpelling(string written, string expected)
        {
            var declared = new[] { "ADR", "AdvanceClock" };

            Assert.Equal(expected, IecIdentifier.Canonical(declared, written));
        }

        // Null is how a caller learns the name is none of the declared ones
        // and falls through to the next dispatch stage.
        [Theory]
        [InlineData("ADRX")]
        [InlineData(null)]
        public void Canonical_NameNotDeclared_ReturnsNull(string written)
        {
            Assert.Null(IecIdentifier.Canonical(new[] { "ADR" }, written));
        }

        // A table handed in by a caller may have been built with any comparer
        // (even ordinal, holding keys that differ only in case); the copy has
        // to look up case-insensitively regardless, with the later key winning
        // the collision rather than throwing.
        [Fact]
        public void CopyOf_OrdinalSourceWithCaseCollidingKeys_IsCaseInsensitiveAndLaterWins()
        {
            var source = new List<KeyValuePair<string, int>>
            {
                new KeyValuePair<string, int>("eRed", 1),
                new KeyValuePair<string, int>("ERED", 2),
            };

            var copy = IecIdentifier.CopyOf(source);

            Assert.Single(copy);
            Assert.Equal(2, copy["ered"]);
        }

        [Fact]
        public void CopyOf_Null_IsEmpty()
        {
            Assert.Empty(IecIdentifier.CopyOf<int>(null));
        }

        [Fact]
        public void Matches_DiffersOnlyInCase_IsTrue()
        {
            Assert.True(IecIdentifier.Matches("fbMotor", "FBMOTOR"));
        }

        [Fact]
        public void Matches_DifferentName_IsFalse()
        {
            Assert.False(IecIdentifier.Matches("fbMotor", "fbMotor2"));
        }
    }
}
