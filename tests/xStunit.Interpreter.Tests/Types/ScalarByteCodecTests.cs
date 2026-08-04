using System;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // The byte codec for scalars is a rule about types, not about a running
    // program: it needs a TypeRegistry and nothing else. Building no Engine and
    // no Frame here is the point - the day these tests need one, the layout
    // rules have leaked back into the interpreter they were pulled out of.
    public class ScalarByteCodecTests
    {
        private static readonly TypeLayout Layout = new TypeLayout(new TypeRegistry(Array.Empty<PouAst>()), TargetPlatform.Default);

        // A value pack writes and unpack reads back unchanged, in the CLR shape
        // the rest of the interpreter holds that IEC type in. Widening or
        // narrowing on the way through would corrupt any MEMCPY that moves the
        // type through a byte buffer.
        [Theory]
        [InlineData("BOOL", true)]
        [InlineData("BOOL", false)]
        [InlineData("SINT", -128)]
        [InlineData("USINT", 255)]
        [InlineData("BYTE", 200)]
        [InlineData("INT", -32768)]
        [InlineData("UINT", 65535)]
        [InlineData("WORD", 4660)]
        [InlineData("DINT", -2147483648)]
        [InlineData("UDINT", 4294967295L)]
        [InlineData("DWORD", 3735928559L)]
        [InlineData("REAL", 1.5f)]
        [InlineData("TIME", 86400000u)]
        [InlineData("DATE", 1600000000u)]
        [InlineData("DATE_AND_TIME", 1600000000u)]
        [InlineData("DT", 1600000000u)]
        [InlineData("TIME_OF_DAY", 3600000u)]
        [InlineData("TOD", 3600000u)]
        [InlineData("LINT", -9223372036854775808L)]
        [InlineData("ULINT", 18446744073709551615UL)]
        [InlineData("LWORD", 12297829382473034410UL)]
        [InlineData("LREAL", 1.0e300d)]
        [InlineData("LTIME", 86400000000000UL)]
        public void PackThenUnpack_Scalar_RoundTripsValueAndClrShape(string typeName, object value)
        {
            var buffer = new byte[Layout.SizeOf(typeName).Size];

            Assert.True(Layout.TryPackScalar(buffer, 0, value, typeName));
            Assert.True(Layout.TryUnpackScalar(buffer, 0, typeName, out var roundTripped));

            Assert.Equal(value, roundTripped);
            Assert.Equal(value.GetType(), roundTripped.GetType());
        }

        // Every scalar the table sizes is a scalar the table can also pack.
        // Sizing a type and then refusing to pack it is the split this codec
        // exists to close.
        [Theory]
        [InlineData("BOOL")]
        [InlineData("SINT")]
        [InlineData("USINT")]
        [InlineData("BYTE")]
        [InlineData("INT")]
        [InlineData("UINT")]
        [InlineData("WORD")]
        [InlineData("DINT")]
        [InlineData("UDINT")]
        [InlineData("DWORD")]
        [InlineData("REAL")]
        [InlineData("TIME")]
        [InlineData("DATE")]
        [InlineData("DATE_AND_TIME")]
        [InlineData("DT")]
        [InlineData("TIME_OF_DAY")]
        [InlineData("TOD")]
        [InlineData("LINT")]
        [InlineData("ULINT")]
        [InlineData("LWORD")]
        [InlineData("LREAL")]
        [InlineData("LTIME")]
        public void TryUnpackScalar_EverySizedScalar_IsAlsoADecodableOne(string typeName)
        {
            var buffer = new byte[Layout.SizeOf(typeName).Size];

            Assert.True(Layout.TryUnpackScalar(buffer, 0, typeName, out _));
        }

        // The codec writes exactly the width SizeOf reports, so a scalar packed
        // into a struct or array slot cannot spill into its neighbour.
        [Theory]
        [InlineData("SINT", 1)]
        [InlineData("INT", 2)]
        [InlineData("DINT", 4)]
        [InlineData("LREAL", 8)]
        public void TryPackScalar_AtAnOffset_TouchesOnlyItsOwnBytes(string typeName, int width)
        {
            const int offset = 3;
            var buffer = new byte[offset + width + 3];
            for (var i = 0; i < buffer.Length; i++)
                buffer[i] = 0xAA;

            Assert.True(Layout.TryPackScalar(buffer, offset, Zero(typeName), typeName));

            for (var i = 0; i < buffer.Length; i++)
            {
                var expected = i >= offset && i < offset + width ? 0x00 : 0xAA;
                Assert.Equal(expected, buffer[i]);
            }
        }

        // A composite is not this class's to pack, and saying so with false
        // rather than an exception is what lets the caller fall through to its
        // own STRUCT/ARRAY/STRING handling.
        [Theory]
        [InlineData("ST_Something")]
        [InlineData("STRING(10)")]
        [InlineData("ARRAY [0..3] OF INT")]
        [InlineData("POINTER TO INT")]
        public void TryPackScalar_NonScalar_DeclinesInsteadOfThrowing(string typeName)
        {
            Assert.False(Layout.TryPackScalar(new byte[16], 0, 0, typeName));
            Assert.False(Layout.TryUnpackScalar(new byte[16], 0, typeName, out var value));
            Assert.Null(value);
        }

        private static object Zero(string typeName) =>
            typeName == "LREAL" ? (object)0.0d : 0;
    }
}
