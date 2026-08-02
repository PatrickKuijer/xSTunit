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
        private static readonly IReadOnlyDictionary<string, int> ScalarByteSizes = new Dictionary<string, int>
        {
            ["SINT"] = 1,
            ["USINT"] = 1,
            ["BYTE"] = 1,
            ["INT"] = 2,
            ["UINT"] = 2,
            ["WORD"] = 2,
            ["DINT"] = 4,
            ["UDINT"] = 4,
            ["DWORD"] = 4,
            ["REAL"] = 4,
            ["TIME"] = 4,
            ["DATE"] = 4,
            ["DATE_AND_TIME"] = 4,
            ["TIME_OF_DAY"] = 4,
            ["LINT"] = 8,
            ["ULINT"] = 8,
            ["LWORD"] = 8,
            ["LREAL"] = 8,
            ["LTIME"] = 8,
        };

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
                throw new NotSupportedException("A byte size needs a type name");

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

            if (resolved == "BOOL")
                return (1, 1);

            if (ScalarByteSizes.TryGetValue(resolved, out var scalarSize))
                return (scalarSize, scalarSize);

            throw new NotSupportedException($"No byte size is known for type '{typeName}'");
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
        // a bare int.Parse, same as an ARRAY bound.
        private int ParseStringLength(string resolvedTypeName) =>
            StringTypeInfo.ParseLength(
                resolvedTypeName,
                boundText => _resolveBound != null
                    ? _resolveBound(boundText)
                    : throw new FormatException($"The input string '{boundText}' was not in a correct format"));

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
