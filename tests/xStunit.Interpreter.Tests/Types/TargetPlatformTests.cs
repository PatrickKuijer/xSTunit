using Xunit;

namespace xStunit.Interpreter.Tests
{
    // The one place the target platform's answers are stated outright: which
    // machine a run assumes when nobody said, how wide an address is on each,
    // and what happens to a name that is neither.
    public class TargetPlatformTests
    {
        // The default decides the answers of every caller who never names a
        // target, so it is a product promise rather than an implementation
        // detail: real TwinCAT 3 machines are x64, and a run that says nothing
        // must match one. Pinned as a number as well as an instance so that
        // reverting it - however indirectly - goes red here first.
        [Fact]
        public void Default_IsTheSixtyFourBitTarget()
        {
            Assert.Same(TargetPlatform.X64, TargetPlatform.Default);
            Assert.Equal(8, TargetPlatform.Default.AddressSize);
        }

        [Fact]
        public void X86_SizesAnAddressAtFourBytes()
        {
            Assert.Equal(4, TargetPlatform.X86.AddressSize);
        }

        [Fact]
        public void X64_SizesAnAddressAtEightBytes()
        {
            Assert.Equal(8, TargetPlatform.X64.AddressSize);
        }

        // IEC and TwinCAT alike are case-insensitive about names, and a user
        // typing --target X64 means the same thing as --target x64.
        [Theory]
        [InlineData("x86", 4)]
        [InlineData("X86", 4)]
        [InlineData("x64", 8)]
        [InlineData("X64", 8)]
        public void TryParse_EitherSpelling_ResolvesToThatTargetsAddressWidth(string text, int expectedAddressSize)
        {
            Assert.True(TargetPlatform.TryParse(text, out var target));
            Assert.Equal(expectedAddressSize, target.AddressSize);
        }

        // A name nobody recognises must not fall back to the default: a run
        // computing at a width the caller did not ask for is the failure this
        // whole type exists to prevent, and it would be silent.
        [Theory]
        [InlineData("arm64")]
        [InlineData("x84")]
        [InlineData("")]
        [InlineData(null)]
        public void TryParse_AnythingElse_IsRefusedRatherThanDefaulted(string text)
        {
            Assert.False(TargetPlatform.TryParse(text, out var target));
            Assert.Null(target);
        }

        // What a compiler-emitted module calls its own target, which is how a
        // conformance run picks a width without asking anyone. Anything that
        // does not name x64 is a 32-bit build: the file was written by the
        // compiler that produced the layout, so there is no third answer to
        // report.
        [Theory]
        [InlineData("TwinCAT RT (x64)", 8)]
        [InlineData("TwinCAT RT (x86)", 4)]
        [InlineData("something else entirely", 4)]
        [InlineData(null, 4)]
        public void FromModuleTarget_ReadsTheWidthTheModuleWasBuiltFor(string moduleTarget, int expectedAddressSize)
        {
            Assert.Equal(expectedAddressSize, TargetPlatform.FromModuleTarget(moduleTarget).AddressSize);
        }
    }
}
