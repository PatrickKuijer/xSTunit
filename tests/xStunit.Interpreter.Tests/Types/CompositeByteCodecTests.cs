using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // ARRAY and STRUCT byte content is a rule about types, not about a running
    // program: a TypeRegistry, and for a non-literal bound a resolver, are all
    // it takes to walk a declaration's elements and fields. Reaching element
    // stride, field offsets and the nesting here rather than through MEMCPY in
    // a .TcPOU fixture is the point - the day these tests need an Engine or a
    // Frame, the layout rules have leaked back into the interpreter they were
    // pulled out of.
    public class CompositeByteCodecTests
    {
        // wide at 0, tail at 4, three bytes of trailing padding: an 8-byte type
        // whose content is five bytes, so a stride bug shows up as a shifted
        // value rather than as a silent overlap.
        private static readonly StructAst PaddedStruct =
            Struct("ST_Padded", ("wide", "DINT"), ("tail", "SINT"));

        private static readonly StructAst NestedStruct =
            Struct("ST_Nested", ("inner", "ST_Padded"), ("items", "ARRAY[0..2] OF INT"), ("flag", "BOOL"));

        private static readonly TypeLayout Layout = NewLayout();

        // The declared element count is the product of every dimension's span,
        // and an element sits one SizeOf apart from its neighbour. Getting
        // either wrong writes an element over the one before it.
        [Fact]
        public void Pack_ArrayOfScalars_LaysElementsOneElementSizeApart()
        {
            var buffer = new byte[Layout.SizeOf("ARRAY[0..3] OF INT").Size];

            Layout.Pack(buffer, 0, ArrayOf("INT", 0x1111, 0x2222, 0x3333, 0x4444), "ARRAY[0..3] OF INT");

            Assert.Equal(new byte[] { 0x11, 0x11, 0x22, 0x22, 0x33, 0x33, 0x44, 0x44 }, buffer);
        }

        // A dimension need not start at zero, and a multi-dimensional array is
        // one flat run of lo..hi spans multiplied together - 2 x 3 is six
        // elements, not five and not two.
        [Theory]
        [InlineData("ARRAY[0..3] OF SINT", 4)]
        [InlineData("ARRAY[1..4] OF SINT", 4)]
        [InlineData("ARRAY[-2..2] OF SINT", 5)]
        [InlineData("ARRAY[0..1,0..2] OF SINT", 6)]
        [InlineData("ARRAY[1..2,1..2,1..2] OF SINT", 8)]
        public void Pack_Array_WritesOneElementPerDeclaredIndex(string typeName, int expectedCount)
        {
            var buffer = new byte[Layout.SizeOf(typeName).Size + 1];
            var values = Enumerable.Range(1, expectedCount).Cast<object>().ToArray();

            Layout.Pack(buffer, 0, new ArrayValue(new[] { (0, expectedCount - 1) }, "SINT", values, Cell.Unbounded), typeName);

            Assert.Equal(
                Enumerable.Range(1, expectedCount).Select(v => (byte)v).Concat(new byte[] { 0 }),
                buffer);
        }

        // The element count folds the same way in both directions: an unpack
        // that read one element too few would leave a default in the last slot
        // and an unpack that read one too many would run past the buffer.
        [Fact]
        public void PackThenUnpack_MultiDimensionalArray_RoundTripsEveryElement()
        {
            const string typeName = "ARRAY[0..1,0..2] OF INT";
            var original = ArrayOf("INT", 10, 20, 30, 40, 50, 60);
            var buffer = new byte[Layout.SizeOf(typeName).Size];

            Layout.Pack(buffer, 0, original, typeName);
            var roundTripped = (ArrayValue)Layout.Unpack(buffer, 0, typeName);

            Assert.Equal(original.Elements, roundTripped.Elements);
            Assert.Equal(new[] { (0, 1), (0, 2) }, roundTripped.Dimensions);
        }

        // An array of a padded struct strides by the struct's declared size,
        // padding included, so the second element starts at 8 and not at 5.
        [Fact]
        public void Pack_ArrayOfPaddedStruct_StridesByTheDeclaredSizeNotTheContentSize()
        {
            const string typeName = "ARRAY[0..1] OF ST_Padded";
            var elements = new object[]
            {
                Padded(0x01020304, 0x11),
                Padded(0x05060708, 0x22),
            };
            var buffer = new byte[Layout.SizeOf(typeName).Size];

            Layout.Pack(buffer, 0, new ArrayValue(new[] { (0, 1) }, "ST_Padded", elements, Cell.Unbounded), typeName);

            Assert.Equal(
                new byte[] { 0x04, 0x03, 0x02, 0x01, 0x11, 0x00, 0x00, 0x00, 0x08, 0x07, 0x06, 0x05, 0x22, 0x00, 0x00, 0x00 },
                buffer);
        }

        // A pack write lands on the declared fields and nothing else: the
        // padding a struct carries has no storage behind it, so the bytes at
        // those offsets keep whatever the destination already held. The zeroes
        // a MEMCPY destination shows there come from its buffer being freshly
        // zeroed, not from the write.
        [Fact]
        public void Pack_PaddedStruct_TouchesTheFieldBytesAndLeavesThePaddingAlone()
        {
            var buffer = Poisoned(Layout.SizeOf("ST_Padded").Size);

            Layout.Pack(buffer, 0, Padded(0, 0), "ST_Padded");

            Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0xAA, 0xAA, 0xAA }, buffer);
        }

        // A composite packed at an offset stays inside its own SizeOf bytes, so
        // a struct field or array element cannot spill into its neighbour.
        [Theory]
        [InlineData("ST_Padded")]
        [InlineData("ST_Nested")]
        [InlineData("ARRAY[0..2] OF INT")]
        public void Pack_AtAnOffset_WritesNothingPastTheDeclaredSize(string typeName)
        {
            const int offset = 4;
            var size = Layout.SizeOf(typeName).Size;
            var buffer = Poisoned(offset + size + 4);

            Layout.Pack(buffer, offset, DefaultOf(typeName), typeName);

            Assert.Equal(Enumerable.Repeat((byte)0xAA, offset), buffer.Take(offset));
            Assert.Equal(Enumerable.Repeat((byte)0xAA, 4), buffer.Skip(offset + size));
        }

        // A struct nesting a struct and an array recurses in both directions:
        // every leaf value comes back, seated at the offset the field placement
        // put it at.
        [Fact]
        public void PackThenUnpack_NestedStruct_RoundTripsEveryLeafValue()
        {
            var original = new StructInstance("ST_Nested");
            original.Fields["inner"] = new Cell { Value = Padded(0x7EADBEE, -3), DeclaredTypeName = "ST_Padded" };
            original.Fields["items"] = new Cell { Value = ArrayOf("INT", -1, 0, 32767), DeclaredTypeName = "ARRAY[0..2] OF INT" };
            original.Fields["flag"] = new Cell { Value = true, DeclaredTypeName = "BOOL" };
            var buffer = new byte[Layout.SizeOf("ST_Nested").Size];

            Layout.Pack(buffer, 0, original, "ST_Nested");
            var roundTripped = (StructInstance)Layout.Unpack(buffer, 0, "ST_Nested");

            var inner = (StructInstance)roundTripped.Fields["inner"].Value;
            Assert.Equal(0x7EADBEE, inner.Fields["wide"].Value);
            Assert.Equal(-3, inner.Fields["tail"].Value);
            Assert.Equal(new object[] { -1, 0, 32767 }, ((ArrayValue)roundTripped.Fields["items"].Value).Elements);
            Assert.Equal(true, roundTripped.Fields["flag"].Value);
        }

        // The bound resolver the layout module was built with settles a
        // non-literal ARRAY bound, so a declaration sized by a GVL constant
        // packs without an Engine evaluating the expression on its behalf.
        [Fact]
        public void Pack_ArrayWithConstantExpressionBound_ResolvesThroughTheDelegate()
        {
            const string typeName = "ARRAY[0..cLimits.SLOTS-1] OF BYTE";
            var asked = new List<string>();
            var layout = NewLayout(boundText =>
            {
                asked.Add(boundText);
                return 3;
            });
            var buffer = new byte[layout.SizeOf(typeName).Size];

            layout.Pack(buffer, 0, ArrayOf("BYTE", 1, 2, 3, 4), typeName);

            Assert.Equal(4, buffer.Length);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, buffer);
            Assert.Contains("cLimits.SLOTS-1", asked);
        }

        // An array of STRING reaches the string codec through the element
        // dispatch, and the unpacked array carries the declared capacity so a
        // later assignment into an element truncates the same way the original
        // declaration did.
        [Fact]
        public void PackThenUnpack_ArrayOfString_KeepsTheDeclaredElementCapacity()
        {
            const string typeName = "ARRAY[0..1] OF STRING(4)";
            var buffer = new byte[Layout.SizeOf(typeName).Size];

            Layout.Pack(buffer, 0, ArrayOf("STRING(4)", "AB", "CDE"), typeName);
            var roundTripped = (ArrayValue)Layout.Unpack(buffer, 0, typeName);

            Assert.Equal(10, buffer.Length);
            Assert.Equal(new object[] { "AB", "CDE" }, roundTripped.Elements);
            Assert.Equal(4, roundTripped.ElementStringCapacity);

            roundTripped.SetElement(0, "TOOLONG");
            Assert.Equal("TOOL", roundTripped.Elements[0]);
        }

        // The same capacity claim one level in: a STRING field unpacked out of
        // a struct is seated in a Cell that still knows its declaration.
        [Fact]
        public void Unpack_StructWithStringField_SeatsTheFieldInACellBoundedByItsDeclaration()
        {
            var registry = NewRegistry(Struct("ST_Labelled", ("label", "STRING(4)")));
            var layout = new TypeLayout(registry);
            var buffer = new byte[layout.SizeOf("ST_Labelled").Size];

            var instance = (StructInstance)layout.Unpack(buffer, 0, "ST_Labelled");
            instance.Fields["label"].Value = "TOOLONG";

            Assert.Equal("STRING(4)", instance.Fields["label"].DeclaredTypeName);
            Assert.Equal("TOOL", instance.Fields["label"].Value);
        }

        // A shape the byte model does not cover is refused rather than
        // half-written, and refused by name - including when it is buried in a
        // field, where naming the outer struct would send the reader looking in
        // the wrong declaration.
        [Theory]
        [InlineData("POINTER TO INT")]
        [InlineData("ST_Unpackable")]
        [InlineData("ARRAY[0..1] OF POINTER TO INT")]
        public void Pack_UnsupportedShape_RefusesNamingTheShapeItCannotWrite(string typeName)
        {
            var registry = NewRegistry(Struct("ST_Unpackable", ("handle", "POINTER TO INT")));
            var layout = new TypeLayout(registry);

            var packing = Assert.Throws<NotSupportedException>(
                () => layout.Pack(new byte[16], 0, DefaultOf(typeName), typeName));
            var unpacking = Assert.Throws<NotSupportedException>(
                () => layout.Unpack(new byte[16], 0, typeName));

            Assert.Contains("POINTER TO INT", packing.Message);
            Assert.Contains("POINTER TO INT", unpacking.Message);
        }

        private static byte[] Poisoned(int length) =>
            Enumerable.Repeat((byte)0xAA, length).ToArray();

        private static object DefaultOf(string typeName)
        {
            if (typeName == "ST_Padded")
                return Padded(0, 0);

            if (typeName == "ST_Nested")
            {
                var nested = new StructInstance("ST_Nested");
                nested.Fields["inner"] = new Cell { Value = Padded(0, 0) };
                nested.Fields["items"] = new Cell { Value = ArrayOf("INT", 0, 0, 0) };
                nested.Fields["flag"] = new Cell { Value = false };
                return nested;
            }

            if (typeName == "ST_Unpackable")
            {
                var unpackable = new StructInstance("ST_Unpackable");
                unpackable.Fields["handle"] = new Cell { Value = null };
                return unpackable;
            }

            return ArrayTypeInfo.IsArrayType(typeName)
                ? (object)ArrayOf("INT", 0, 0, 0)
                : null;
        }

        private static StructInstance Padded(int wide, int tail)
        {
            var instance = new StructInstance("ST_Padded");
            instance.Fields["wide"] = new Cell { Value = wide, DeclaredTypeName = "DINT" };
            instance.Fields["tail"] = new Cell { Value = tail, DeclaredTypeName = "SINT" };
            return instance;
        }

        private static ArrayValue ArrayOf(string elementTypeName, params object[] elements) =>
            new ArrayValue(new[] { (0, elements.Length - 1) }, elementTypeName, elements, Cell.Unbounded);

        private static StructAst Struct(string name, params (string Name, string TypeName)[] fields) =>
            new StructAst(
                name,
                fields.Select(f => new VarDecl(f.Name, f.TypeName, null, VarSection.Local)).ToList());

        private static TypeLayout NewLayout(Func<string, int> resolveBound = null) =>
            new TypeLayout(NewRegistry(PaddedStruct, NestedStruct), resolveBound);

        private static TypeRegistry NewRegistry(params StructAst[] structTypes) =>
            new TypeRegistry(Array.Empty<PouAst>(), structTypes);
    }
}
