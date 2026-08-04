using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Interpreter.Extensibility;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests.Conformance
{
    // The half of the trailing-padding checklist rule a .tmc cannot settle: a
    // module states how wide a type is, never which bytes a MEMCPY out of that
    // type lands on. These tests state that second half as an internal
    // consistency claim - the byte view a pack write fills is exactly SIZEOF
    // wide, and inside it the field bytes carry the field values while every
    // padding byte reads as zero.
    //
    // The rule the padding bytes follow: a pack write touches the declared
    // fields only, into a freshly zeroed buffer sized by SIZEOF. Padding is
    // therefore never carried over from the source variable - the interpreter
    // has no storage for it - and reads as zero at the destination, clobbering
    // whatever the destination held at those offsets. A copy of SIZEOF bytes
    // is a whole-type copy, padding included, not a copy of the field bytes
    // alone.
    //
    // What this cannot catch is the pack write and SIZEOF being wrong the same
    // way; that is what the .tmc oracle is for. What it does catch is the two
    // of them drifting apart, which would be a buffer overrun rather than a
    // wrong value.
    public class LayoutChecklistPackWriteTests
    {
        private static readonly string[] FixtureDirectories = { TestFixtures.LayoutChecklistFixtureDir() };

        // Wider than the widest fixture type, so the bytes past SIZEOF are
        // real buffer rather than the end of the array: a copy that ran on
        // past the declared size lands in them instead of throwing.
        private const int DestinationBytes = 64;

        private const int Poison = 0xFF;

        // Five bytes of content in a type SIZEOF calls 8: the three bytes
        // behind the trailer are the trailing padding the checklist asks
        // about, and a pack write leaves them zero.
        [Theory]
        [InlineData("x86")]
        [InlineData("x64")]
        public void TrailingPadding_FillsFiveFieldBytesAndZeroesTheThreeBehindThem(string targetName)
        {
            AssertPackedImage(
                targetName,
                "ST_TrailingPadding",
                new[] { "src.wide := 16#01020304;", "src.tail := 16#7F;" },
                declaredSize: 8,
                expected: new byte[] { 0x04, 0x03, 0x02, 0x01, 0x7F, 0x00, 0x00, 0x00 });
        }

        // Trailing padding at the one alignment no other fixture reaches: an
        // 8-byte alignment pads a type ending in a single byte at offset 24
        // out to 32, and a pack write zeroes all seven of those bytes as well
        // as the four between leadIn and wideFloat.
        [Theory]
        [InlineData("x86")]
        [InlineData("x64")]
        public void WideAlignment_ZeroesTheInteriorGapAndAllSevenTrailingBytes(string targetName)
        {
            var expected = new byte[32];
            BitConverter.GetBytes(0x11223344).CopyTo(expected, 0);
            BitConverter.GetBytes(-1234.5678).CopyTo(expected, 8);
            BitConverter.GetBytes(-1234L).CopyTo(expected, 16);
            expected[24] = 0x5A;

            // The LREAL is written as a typed literal because an untyped
            // 1234.5678 lexes as a REAL, and the single-precision rounding
            // that follows would make this a test about float conversion
            // rather than about which bytes the write landed on. The LINT is
            // negative so that its high four bytes are non-zero: sized as a
            // DINT it would leave them zero and read as padding.
            AssertPackedImage(
                targetName,
                "ST_WideAlignment",
                new[]
                {
                    "src.leadIn := 16#11223344;",
                    "src.wideFloat := -LREAL#1234.5678;",
                    "src.wideInt := -1234;",
                    "src.trailer := 16#5A;",
                },
                declaredSize: 32,
                expected: expected);
        }

        // Per-element round-up seen as bytes rather than as an offset: each
        // element's own three padding bytes are inside the copy, so the second
        // element's first byte lands at 8 and not at 5, and the outer type's
        // three trailing bytes behind the sentinel are zeroed too.
        [Theory]
        [InlineData("x86")]
        [InlineData("x64")]
        public void ArrayStride_ZeroesEachElementsPaddingAndTheOuterTrailer(string targetName)
        {
            var expected = new byte[28];
            for (var i = 0; i < 3; i++)
            {
                BitConverter.GetBytes(0x11 * (i + 1)).CopyTo(expected, 8 * i);
                expected[8 * i + 4] = (byte)(0xA0 + i);
            }
            expected[24] = 0x5A;

            AssertPackedImage(
                targetName,
                "ST_ArrayStride",
                new[]
                {
                    "src.items[0].wide := 16#11; src.items[0].tail := 16#A0;",
                    "src.items[1].wide := 16#22; src.items[1].tail := 16#A1;",
                    "src.items[2].wide := 16#33; src.items[2].tail := 16#A2;",
                    "src.sentinel := 16#5A;",
                },
                declaredSize: 28,
                expected: expected);
        }

        // The width of the packed view itself, read through the one caller
        // that bounds-checks it and says how many bytes were there. SIZEOF
        // bytes are readable and SIZEOF + 1 is not, which is the pin that
        // breaks the moment the pack write and SIZEOF disagree about a type -
        // in either direction, and without needing an overrun to crash first.
        [Theory]
        [InlineData("x86", "ST_TrailingPadding", 8)]
        [InlineData("x86", "ST_WideAlignment", 32)]
        [InlineData("x86", "ST_ArrayStride", 28)]
        [InlineData("x64", "ST_TrailingPadding", 8)]
        [InlineData("x64", "ST_WideAlignment", 32)]
        [InlineData("x64", "ST_ArrayStride", 28)]
        public void PackedView_IsExactlySizeOfBytesWide(string targetName, string typeName, int declaredSize)
        {
            var (engine, _, frame) = NewHolder(targetName, typeName);

            Assert.Equal(declaredSize, Convert.ToInt32(Evaluate(engine, frame, "SIZEOF(src)")));
            Assert.Equal(
                declaredSize,
                Convert.ToInt32(Evaluate(engine, frame, $"F_CountReadableBytes(ADR(src), {declaredSize})")));

            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => Evaluate(engine, frame, $"F_CountReadableBytes(ADR(src), {declaredSize + 1})"));

            Assert.Contains($"only {declaredSize} byte(s) available", ex.Message);
        }

        // The expected image is one table for both targets, not one per
        // target: none of these types holds an address, so a byte that moved
        // with the target would mean the pack write had taken the pointer
        // width into account somewhere it has no business doing so.
        private static void AssertPackedImage(
            string targetName, string typeName, string[] setup, int declaredSize, byte[] expected)
        {
            Assert.Equal(declaredSize, expected.Length);

            var (engine, instance, frame) = NewHolder(targetName, typeName);
            engine.ExecuteStatements(Parser.ParseStatements(string.Join("\n", setup)), frame);

            Assert.Equal(declaredSize, Convert.ToInt32(Evaluate(engine, frame, "SIZEOF(src)")));

            // Poisoning first is what makes a zero in the destination mean the
            // copy wrote it, rather than meaning nothing was ever there.
            Evaluate(engine, frame, $"MEMSET(ADR(dest), 16#{Poison:X2}, {DestinationBytes})");
            Evaluate(engine, frame, "MEMCPY(ADR(dest), ADR(src), SIZEOF(src))");

            Assert.Equal(Hex(expected.Select(b => (int)b)), Hex(DestinationImage(instance).Take(declaredSize)));

            var untouched = DestinationImage(instance).Skip(declaredSize).ToList();
            Assert.Equal(Hex(untouched.Select(_ => Poison)), Hex(untouched));
        }

        private static string Hex(IEnumerable<int> bytes) =>
            string.Join(" ", bytes.Select(b => b.ToString("X2")));

        private static IEnumerable<int> DestinationImage(FbInstance instance) =>
            ((ArrayValue)instance.Fields["dest"].Value).Elements.Select(Convert.ToInt32);

        private static object Evaluate(Engine engine, Frame frame, string expression) =>
            engine.Evaluate(Parser.ParseExpression(expression), frame);

        private static (Engine Engine, FbInstance Instance, Frame Frame) NewHolder(
            string targetName, string typeName)
        {
            var holder = new PouAst(
                "FB_PackHolder",
                null,
                $"VAR\n\tsrc : {typeName};\n\tdest : ARRAY[0..{DestinationBytes - 1}] OF BYTE;\nEND_VAR",
                "",
                new List<MethodAst>());

            var nativeFunctions = new NativeFunctionRegistry();
            nativeFunctions.RegisterAll(new IXstunitNativeFunction[] { new ReadableByteCounter() });

            // The three-argument overload on purpose: the two-argument one
            // hard-defaults to x86, which would leave every claim below an
            // x86-only claim no matter what the theory row asked for.
            var engine = new Engine(BuildRegistry(holder), nativeFunctions, Target(targetName));
            var instance = engine.NewInstance("FB_PackHolder");
            return (engine, instance, new Frame(instance, "FB_PackHolder"));
        }

        private static TargetPlatform Target(string name)
        {
            Assert.True(TargetPlatform.TryParse(name, out var target), $"'{name}' is not a target platform");
            return target;
        }

        // Reading through the plugin boundary rather than with MEMCPY: it goes
        // through the same packed view MEMCPY would, but bounds-checks the
        // count and reports how many bytes the view actually held.
        private sealed class ReadableByteCounter : IXstunitNativeFunction
        {
            public string Name => "F_CountReadableBytes";

            public object Invoke(NativeCallContext context) =>
                context.RequireBytes("pointer", 0, context.RequireInt32("count", 1)).Length;
        }

        private static TypeRegistry BuildRegistry(PouAst holder)
        {
            var structs = DutStructLoader.Load(FixtureDirectories, out _);
            var enums = DutEnumLoader.Load(FixtureDirectories, out _, out _);
            var aliases = DutAliasLoader.Load(FixtureDirectories, out _);

            var resolvable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var alias in aliases.Concat(enums))
                resolvable[alias.Key] = alias.Value;

            return new TypeRegistry(new[] { holder }, structs, null, resolvable);
        }
    }
}
