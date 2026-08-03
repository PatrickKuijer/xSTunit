using System;
using xStunit.Parser;
using xStunit.Runner;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // The STRING/WSTRING wire format is a rule about types, not about a running
    // program: a TypeRegistry and, for a non-literal declared length, a bound
    // resolver are all it takes. Reaching truncation, null-padding and the
    // surrogate refusal here rather than through MEMCPY in a .TcPOU fixture is
    // the point - the day these tests need an Engine or a Frame, the layout
    // rules have leaked back into the interpreter they were pulled out of.
    public class StringByteCodecTests
    {
        private static readonly TypeLayout Layout = new TypeLayout(new TypeRegistry(Array.Empty<PouAst>()));

        // Text survives a pack/unpack round trip in both encodings, including
        // the non-ASCII characters each one is meant to reach: Latin-1 for a
        // narrow STRING, the whole Basic Multilingual Plane for a WSTRING.
        [Theory]
        [InlineData("STRING(16)", "HELLO")]
        [InlineData("STRING(16)", "")]
        [InlineData("STRING(16)", "Grüße, Ærø")]
        [InlineData("WSTRING(16)", "HELLO")]
        [InlineData("WSTRING(16)", "")]
        [InlineData("WSTRING(16)", "Ω≈日本")]
        public void PackThenUnpack_String_RoundTripsText(string typeName, string text)
        {
            var buffer = new byte[Layout.SizeOf(typeName).Size];

            Assert.True(Layout.TryPackString(buffer, 0, text, typeName));
            Assert.True(Layout.TryUnpackString(buffer, 0, typeName, out var roundTripped));

            Assert.Equal(text, roundTripped);
        }

        // A narrow STRING is Latin-1: one byte per character whose value IS the
        // code point. Anything else on the wire would mismatch TwinCAT, which
        // reads those bytes back as Latin-1 regardless of what wrote them.
        [Fact]
        public void TryPackString_NarrowString_IsOneLatin1BytePerCharacter()
        {
            var buffer = new byte[Layout.SizeOf("STRING(4)").Size];

            Assert.True(Layout.TryPackString(buffer, 0, "Aé", "STRING(4)"));

            Assert.Equal(new byte[] { 0x41, 0xE9, 0x00, 0x00, 0x00 }, buffer);
        }

        // A WSTRING is little-endian UCS-2, matching TwinCAT on x86: low byte
        // first, so a byte-level reader sees U+00E9 as E9 00 and not 00 E9.
        [Fact]
        public void TryPackString_WideString_IsLittleEndianUcs2()
        {
            var buffer = new byte[Layout.SizeOf("WSTRING(3)").Size];

            Assert.True(Layout.TryPackString(buffer, 0, "AéΩ", "WSTRING(3)"));

            Assert.Equal(
                new byte[] { 0x41, 0x00, 0xE9, 0x00, 0xA9, 0x03, 0x00, 0x00 },
                buffer);
        }

        // The declared length is a capacity, not a promise about the value: text
        // longer than it is cut at the declared character count, in characters
        // and not in bytes, so a WSTRING(4) holds four characters and not two.
        [Theory]
        [InlineData("STRING(4)", "ABCDEFG", "ABCD")]
        [InlineData("WSTRING(4)", "ABCDEFG", "ABCD")]
        [InlineData("WSTRING(4)", "ΩΩΩΩΩΩ", "ΩΩΩΩ")]
        public void PackThenUnpack_TextLongerThanDeclaredLength_TruncatesToTheDeclaredCharacterCount(
            string typeName, string text, string expected)
        {
            var buffer = new byte[Layout.SizeOf(typeName).Size];

            Assert.True(Layout.TryPackString(buffer, 0, text, typeName));
            Assert.True(Layout.TryUnpackString(buffer, 0, typeName, out var roundTripped));

            Assert.Equal(expected, roundTripped);
        }

        // Truncation still terminates: the buffer is exactly full of characters,
        // and the terminator the +1 pays for is what stops the read. A reader
        // that trusted the bytes past the last character would run off the end.
        [Theory]
        [InlineData("STRING(4)", 1)]
        [InlineData("WSTRING(4)", 2)]
        public void TryPackString_TruncatedText_StillWritesTheTerminator(string typeName, int charWidth)
        {
            var buffer = Filled(Layout.SizeOf(typeName).Size, 0xAA);

            Assert.True(Layout.TryPackString(buffer, 0, "ABCDEFG", typeName));

            for (var i = 4 * charWidth; i < buffer.Length; i++)
                Assert.Equal(0, buffer[i]);
        }

        // Every byte of the declared buffer past the text is zeroed, not left as
        // whatever the slot held. A STRING assigned a shorter value would
        // otherwise read back as the old, longer one from the stale tail.
        [Theory]
        [InlineData("STRING(8)", "AB", 2)]
        [InlineData("WSTRING(8)", "AB", 4)]
        public void TryPackString_ShorterText_NullPadsTheRestOfTheDeclaredBuffer(
            string typeName, string text, int textBytes)
        {
            var buffer = Filled(Layout.SizeOf(typeName).Size, 0xAA);

            Assert.True(Layout.TryPackString(buffer, 0, text, typeName));

            for (var i = textBytes; i < buffer.Length; i++)
                Assert.Equal(0, buffer[i]);
        }

        // The codec writes exactly the width SizeOf reports and no more, so a
        // STRING packed into a struct or array slot cannot spill into the field
        // that follows it. This is the one assertion that would fail if sizing
        // and packing disagreed about the terminator.
        [Theory]
        [InlineData("STRING(5)")]
        [InlineData("WSTRING(5)")]
        public void TryPackString_AtAnOffset_TouchesOnlyItsOwnBytes(string typeName)
        {
            const int offset = 3;
            var size = Layout.SizeOf(typeName).Size;
            var buffer = Filled(offset + size + 3, 0xAA);

            Assert.True(Layout.TryPackString(buffer, offset, "AB", typeName));

            for (var i = 0; i < offset; i++)
                Assert.Equal(0xAA, buffer[i]);
            for (var i = offset + size; i < buffer.Length; i++)
                Assert.Equal(0xAA, buffer[i]);
        }

        // A WSTRING terminates on a whole zero WORD, never on a lone zero byte:
        // the high half of every Latin-1 character is a zero byte, so byte-wise
        // termination would cut "A" off before its first character.
        [Fact]
        public void TryUnpackString_WideString_TerminatesOnAZeroWordNotAZeroByte()
        {
            var buffer = new byte[] { 0x41, 0x00, 0x42, 0x00, 0x00, 0x00 };

            Assert.True(Layout.TryUnpackString(buffer, 0, "WSTRING(2)", out var text));

            Assert.Equal("AB", text);
        }

        // A character above the Basic Multilingual Plane costs UTF-16 a
        // surrogate pair and UCS-2 has no way to spend it, so the write is
        // refused rather than shifting every later character by one unit.
        [Fact]
        public void TryPackString_WideStringWithASurrogatePair_IsRefused()
        {
            var buffer = new byte[Layout.SizeOf("WSTRING(16)").Size];

            var ex = Assert.Throws<UnsupportedConstructException>(
                () => Layout.TryPackString(buffer, 0, "hi \U0001F600", "WSTRING(16)"));

            Assert.Contains("U+1F600", ex.Message);
        }

        // The refusal happens even when the offending character sits past the
        // declared length, where truncation would have dropped it: a value the
        // interpreter cannot represent is an error, not something to silently
        // cut away.
        [Fact]
        public void TryPackString_SurrogatePairBeyondTheDeclaredLength_IsStillRefused()
        {
            var buffer = new byte[Layout.SizeOf("WSTRING(2)").Size];

            Assert.Throws<UnsupportedConstructException>(
                () => Layout.TryPackString(buffer, 0, "AB\U0001F600", "WSTRING(2)"));
        }

        // The narrow counterpart of the surrogate refusal: Latin-1 has no byte
        // for a character above U+00FF, and losing its high bits would be a
        // wrong answer that looks like a right one.
        [Fact]
        public void TryPackString_NarrowStringAboveLatin1_IsRefused()
        {
            var buffer = new byte[Layout.SizeOf("STRING(16)").Size];

            var ex = Assert.Throws<UnsupportedConstructException>(
                () => Layout.TryPackString(buffer, 0, "10 €", "STRING(16)"));

            Assert.Contains("U+20AC", ex.Message);
        }

        // The declared length may be a constant expression rather than a digit
        // literal, and it resolves through the same bound resolver an ARRAY
        // bound uses - so a GVL-qualified size sizes and truncates identically
        // to the literal it stands for.
        [Fact]
        public void ConstantExpressionLength_ResolvesThroughTheBoundResolver()
        {
            var resolved = new TypeLayout(
                new TypeRegistry(Array.Empty<PouAst>()),
                boundText => boundText == "cLimits.MAX_NAME" ? 4 : throw new InvalidOperationException(boundText));
            const string typeName = "STRING(cLimits.MAX_NAME)";

            var buffer = new byte[resolved.SizeOf(typeName).Size];
            Assert.True(resolved.TryPackString(buffer, 0, "ABCDEFG", typeName));
            Assert.True(resolved.TryUnpackString(buffer, 0, typeName, out var text));

            Assert.Equal(5, buffer.Length);
            Assert.Equal("ABCD", text);
        }

        // Without a resolver the non-literal length is refused outright, which
        // is what proves the length is not being read with a bare int.Parse:
        // a parse would have to invent a number or throw a FormatException
        // naming nothing the caller can act on.
        [Fact]
        public void ConstantExpressionLength_WithNoBoundResolver_IsRefusedNamingTheExpression()
        {
            var ex = Assert.Throws<NotSupportedException>(
                () => Layout.TryPackString(new byte[16], 0, "AB", "STRING(cLimits.MAX_NAME)"));

            Assert.Contains("cLimits.MAX_NAME", ex.Message);
        }

        // A non-string is not this codec's to pack, and saying so with false
        // rather than an exception is what lets the caller fall through to its
        // own STRUCT/ARRAY/scalar handling.
        [Theory]
        [InlineData("DINT")]
        [InlineData("ST_Something")]
        [InlineData("ARRAY [0..3] OF INT")]
        [InlineData("POINTER TO INT")]
        public void TryPackString_NonString_DeclinesInsteadOfThrowing(string typeName)
        {
            Assert.False(Layout.TryPackString(new byte[16], 0, "AB", typeName));
            Assert.False(Layout.TryUnpackString(new byte[16], 0, typeName, out var text));
            Assert.Null(text);
        }

        private static byte[] Filled(int size, byte value)
        {
            var buffer = new byte[size];
            for (var i = 0; i < buffer.Length; i++)
                buffer[i] = value;
            return buffer;
        }
    }
}
