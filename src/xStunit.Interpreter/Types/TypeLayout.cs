using System;
using System.Collections.Generic;
using System.Linq;

namespace xStunit.Interpreter
{
    // Where a field lands inside its struct, as the layout rules place it.
    internal readonly struct FieldPlacement
    {
        public FieldPlacement(VarDecl field, int offset, int size, int align)
        {
            Field = field;
            Offset = offset;
            Size = size;
            Align = align;
        }

        public VarDecl Field { get; }

        public int Offset { get; }

        public int Size { get; }

        // The alignment the field actually imposes on the struct: its natural
        // alignment, capped by the struct's pack_mode.
        public int Align { get; }
    }

    // The byte sizes and field offsets SIZEOF(), the MEMCPY byte packing, and
    // the .tmc layout oracle all read off. One place, so the oracle measures
    // the rules the interpreter really applies rather than a restatement of
    // them.
    //
    // STRUCT and ARRAY sizes assume TwinCAT's default natural-alignment
    // packing: each field aligned to its own size (no alignment above 8 bytes
    // arises among the supported scalars), and the struct's overall size padded
    // up to its largest member's alignment. A struct's {attribute 'pack_mode'
    // := 'N'} pragma caps that per-field alignment at N bytes instead - see
    // PackBound below.
    internal sealed class TypeLayout
    {
        // A scalar's byte width and the codec that reads and writes those
        // bytes, held together so that a type this table sizes is a type it can
        // also pack: splitting them lets a scalar size correctly and then be
        // refused at pack time.
        //
        // Pack and Unpack move the CLR shape IecNumericType and DefaultValue
        // use for the type, not an arbitrary boxed number - Unpack's result
        // goes straight into a Cell.
        private readonly struct ScalarShape
        {
            public ScalarShape(int size, Action<byte[], int, object> pack, Func<byte[], int, object> unpack)
            {
                Size = size;
                Pack = pack;
                Unpack = unpack;
            }

            public int Size { get; }

            public Action<byte[], int, object> Pack { get; }

            public Func<byte[], int, object> Unpack { get; }
        }

        private static readonly IReadOnlyDictionary<string, ScalarShape> Scalars = BuildScalars();

        // Types that differ only in signedness or in the domain they name share
        // one shape instance, so the width is stated once per wire format
        // rather than once per spelling.
        private static IReadOnlyDictionary<string, ScalarShape> BuildScalars()
        {
            var boolean = new ScalarShape(1,
                (buffer, offset, value) => buffer[offset] = (byte)((bool)value ? 1 : 0),
                (buffer, offset) => buffer[offset] != 0);
            var signed8 = new ScalarShape(1,
                (buffer, offset, value) => buffer[offset] = unchecked((byte)(sbyte)(int)value),
                (buffer, offset) => (int)unchecked((sbyte)buffer[offset]));
            var unsigned8 = new ScalarShape(1,
                (buffer, offset, value) => buffer[offset] = (byte)(int)value,
                (buffer, offset) => (int)buffer[offset]);
            var signed16 = new ScalarShape(2,
                (buffer, offset, value) => BitConverter.GetBytes((short)(int)value).CopyTo(buffer, offset),
                (buffer, offset) => (int)BitConverter.ToInt16(buffer, offset));
            var unsigned16 = new ScalarShape(2,
                (buffer, offset, value) => BitConverter.GetBytes((ushort)(int)value).CopyTo(buffer, offset),
                (buffer, offset) => (int)BitConverter.ToUInt16(buffer, offset));
            var signed32 = new ScalarShape(4,
                (buffer, offset, value) => BitConverter.GetBytes((int)value).CopyTo(buffer, offset),
                (buffer, offset) => BitConverter.ToInt32(buffer, offset));
            var unsigned32 = new ScalarShape(4,
                (buffer, offset, value) => BitConverter.GetBytes((uint)(long)value).CopyTo(buffer, offset),
                (buffer, offset) => (long)BitConverter.ToUInt32(buffer, offset));

            // The duration and calendar types are the one unsigned 32-bit
            // family carried as a CLR uint rather than widened to long.
            var duration32 = new ScalarShape(4,
                (buffer, offset, value) => BitConverter.GetBytes((uint)value).CopyTo(buffer, offset),
                (buffer, offset) => BitConverter.ToUInt32(buffer, offset));
            var signed64 = new ScalarShape(8,
                (buffer, offset, value) => BitConverter.GetBytes((long)value).CopyTo(buffer, offset),
                (buffer, offset) => BitConverter.ToInt64(buffer, offset));
            var unsigned64 = new ScalarShape(8,
                (buffer, offset, value) => BitConverter.GetBytes((ulong)value).CopyTo(buffer, offset),
                (buffer, offset) => BitConverter.ToUInt64(buffer, offset));
            var single = new ScalarShape(4,
                (buffer, offset, value) => BitConverter.GetBytes((float)value).CopyTo(buffer, offset),
                (buffer, offset) => BitConverter.ToSingle(buffer, offset));
            var real64 = new ScalarShape(8,
                (buffer, offset, value) => BitConverter.GetBytes((double)value).CopyTo(buffer, offset),
                (buffer, offset) => BitConverter.ToDouble(buffer, offset));

            return new Dictionary<string, ScalarShape>
            {
                ["BOOL"] = boolean,
                ["SINT"] = signed8,
                ["USINT"] = unsigned8,
                ["BYTE"] = unsigned8,
                ["INT"] = signed16,
                ["UINT"] = unsigned16,
                ["WORD"] = unsigned16,
                ["DINT"] = signed32,
                ["UDINT"] = unsigned32,
                ["DWORD"] = unsigned32,
                ["REAL"] = single,
                ["TIME"] = duration32,
                ["DATE"] = duration32,
                ["DATE_AND_TIME"] = duration32,
                ["TIME_OF_DAY"] = duration32,
                ["LINT"] = signed64,
                ["ULINT"] = unsigned64,
                ["LWORD"] = unsigned64,
                ["LREAL"] = real64,
                ["LTIME"] = unsigned64,
            };
        }

        private readonly TypeRegistry _registry;
        private readonly Func<string, int> _resolveBound;

        // resolveBound evaluates an ARRAY bound or STRING size that is not an
        // integer literal - a GVL-qualified constant, say - which only an
        // Engine holding a Frame can do. A caller with no such constants in
        // play may pass null and get literal-only bounds, with a
        // FormatException on anything else.
        public TypeLayout(TypeRegistry registry, Func<string, int> resolveBound = null)
        {
            _registry = registry;
            _resolveBound = resolveBound;
        }

        public (int Size, int Align) SizeOf(string typeName)
        {
            var resolved = _registry.ResolveAlias(typeName?.Trim());
            if (resolved == null)
                throw new NotSupportedException("SIZEOF() requires a type name");

            if (resolved.StartsWith("POINTER TO", StringComparison.Ordinal) ||
                resolved.StartsWith("REFERENCE TO", StringComparison.Ordinal))
                return (4, 4);

            if (ArrayTypeInfo.IsArrayType(resolved))
            {
                var (dimensions, elementTypeName) = ArrayTypeInfo.Parse(resolved, _resolveBound);
                var count = dimensions.Aggregate(1, (acc, d) => acc * (d.Hi - d.Lo + 1));
                var (elementSize, elementAlign) = SizeOf(elementTypeName);
                return (count * elementSize, elementAlign);
            }

            var structAst = _registry.GetStruct(resolved);
            if (structAst != null)
                return SizeOfStruct(structAst);

            if (StringTypeInfo.IsStringType(resolved))
            {
                var length = ParseStringLength(resolved);

                // Latin-1 narrow STRING is one byte per character plus a
                // one-byte terminator; UCS-2 WSTRING is two bytes per character
                // plus a two-byte terminator, and its elements are WORD-aligned,
                // so a WSTRING member pads itself and everything after it.
                var charWidth = StringTypeInfo.CharWidth(resolved);
                return (charWidth * (length + 1), charWidth);
            }

            if (Scalars.TryGetValue(resolved, out var scalar))
                return (scalar.Size, scalar.Size);

            throw new NotSupportedException($"SIZEOF() doesn't know the byte size of type '{typeName}'");
        }

        // Writes value into buffer at offset when typeName is a scalar (BOOL
        // included) and reports whether it was; a false leaves the composite
        // shapes - STRUCT, ARRAY, STRING - to the caller, which owns how their
        // parts are reached.
        public bool TryPackScalar(byte[] buffer, int offset, object value, string typeName)
        {
            if (!Scalars.TryGetValue(_registry.ResolveAlias(typeName), out var scalar))
                return false;

            scalar.Pack(buffer, offset, value);
            return true;
        }

        // Inverse of TryPackScalar, with the same false meaning: not a scalar,
        // so not this class's to reconstruct.
        public bool TryUnpackScalar(byte[] buffer, int offset, string typeName, out object value)
        {
            if (!Scalars.TryGetValue(_registry.ResolveAlias(typeName), out var scalar))
            {
                value = null;
                return false;
            }

            value = scalar.Unpack(buffer, offset);
            return true;
        }

        // Each field's placement inside structAst, in declaration order.
        public IEnumerable<FieldPlacement> Fields(StructAst structAst)
        {
            var packBound = PackBound(structAst);
            var offset = 0;
            foreach (var field in structAst.Fields)
            {
                var (size, align) = SizeOf(field.TypeName);
                var effectiveAlign = Math.Min(align, packBound);
                offset = RoundUp(offset, effectiveAlign);
                yield return new FieldPlacement(field, offset, size, effectiveAlign);
                offset += size;
            }
        }

        // The length may be a non-literal constant expression (e.g. a
        // GVL-qualified constant), so it goes through _resolveBound rather than
        // a bare int.Parse, same as an ARRAY bound. With no resolver, the
        // literal-only overload's own refusal is the honest error.
        private int ParseStringLength(string resolvedTypeName) =>
            _resolveBound == null
                ? StringTypeInfo.ParseLength(resolvedTypeName)
                : StringTypeInfo.ParseLength(resolvedTypeName, _resolveBound);

        private (int Size, int Align) SizeOfStruct(StructAst structAst)
        {
            var end = 0;
            var maxAlign = 1;
            foreach (var placement in Fields(structAst))
            {
                end = placement.Offset + placement.Size;
                maxAlign = Math.Max(maxAlign, placement.Align);
            }

            return (RoundUp(end, maxAlign), maxAlign);
        }

        // pack_mode 0 (absent) means no cap, i.e. natural alignment. A positive
        // pack_mode caps every field's alignment at that many bytes; pack_mode 1
        // is fully byte-packed, with no padding anywhere.
        private static int PackBound(StructAst structAst) =>
            structAst.PackMode > 0 ? structAst.PackMode : int.MaxValue;

        private static int RoundUp(int value, int align) => (value + align - 1) / align * align;
    }
}
