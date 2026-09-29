using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Parser;

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

        // The wire format of one declared STRING/WSTRING: a fixed buffer of
        // Length characters followed by the terminator that always trails them,
        // CharWidth bytes apiece. Size states that trailing terminator once, so
        // sizing a string and packing one cannot disagree about how many bytes
        // the declaration occupies.
        private readonly struct StringShape
        {
            public StringShape(int length, int charWidth)
            {
                Length = length;
                CharWidth = charWidth;
            }

            // A character count, not a byte count: a WSTRING(80) holds 80
            // characters in 162 bytes.
            public int Length { get; }

            public int CharWidth { get; }

            public int Size => (Length + 1) * CharWidth;

            // A narrow STRING is one Latin-1 byte per character - FromChar
            // refuses anything outside it - and a WSTRING is little-endian
            // UCS-2, matching TwinCAT on x86.
            public void WriteChar(byte[] buffer, int offset, char ch)
            {
                if (CharWidth == 2)
                {
                    buffer[offset] = (byte)(ch & 0xFF);
                    buffer[offset + 1] = (byte)(ch >> 8);
                }
                else
                {
                    buffer[offset] = NarrowStringByte.FromChar(ch);
                }
            }

            public char ReadChar(byte[] buffer, int offset) =>
                CharWidth == 2
                    ? (char)(buffer[offset] | (buffer[offset + 1] << 8))
                    : NarrowStringByte.ToChar(buffer[offset]);
        }

        // The wire format of one declared ARRAY: how many elements its
        // dimensions span and how far apart consecutive elements sit. The
        // stride is the element's whole declared size - an element's own
        // trailing padding lies inside the array, never between its elements.
        private readonly struct ArrayShape
        {
            public ArrayShape(
                IReadOnlyList<(int Lo, int Hi)> dimensions, string elementTypeName, int elementSize, int elementAlign)
            {
                Dimensions = dimensions;
                ElementTypeName = elementTypeName;
                ElementSize = elementSize;
                ElementAlign = elementAlign;
            }

            public IReadOnlyList<(int Lo, int Hi)> Dimensions { get; }

            public string ElementTypeName { get; }

            public int ElementSize { get; }

            public int ElementAlign { get; }

            public int Count => ArrayTypeInfo.ElementCount(Dimensions);

            public int OffsetOf(int index) => index * ElementSize;
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

            return new Dictionary<string, ScalarShape>(IecIdentifier.Comparer)
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
                ["DT"] = duration32,
                ["TIME_OF_DAY"] = duration32,
                ["TOD"] = duration32,
                ["LINT"] = signed64,
                ["ULINT"] = unsigned64,
                ["LWORD"] = unsigned64,
                ["LREAL"] = real64,
                ["LTIME"] = unsigned64,
            };
        }

        private readonly TypeRegistry _registry;
        private readonly TargetPlatform _target;
        private readonly Func<string, int> _resolveBound;
        private readonly HashSet<string> _functionBlocksBeingSized = new HashSet<string>();

        // target settles the one width that is not the same on every machine -
        // an address - and is required rather than defaulted, so no layout is
        // ever computed without a caller having said which machine it is for.
        //
        // resolveBound evaluates an ARRAY bound or STRING size that is not an
        // integer literal - a GVL-qualified constant, say - which only an
        // Engine holding a Frame can do. A caller with no such constants in
        // play may pass null and get literal-only bounds, with a
        // FormatException on anything else.
        public TypeLayout(TypeRegistry registry, TargetPlatform target, Func<string, int> resolveBound = null)
        {
            _registry = registry;
            _target = target;
            _resolveBound = resolveBound;
        }

        public (int Size, int Align) SizeOf(string typeName) =>
            TrySizeOf(typeName)
            ?? throw new NotSupportedException($"SIZEOF() doesn't know the byte size of type '{typeName}'");

        // Null means the type name is one this class has no rule for at all; a
        // type it recognises but cannot size still throws.
        private (int Size, int Align)? TrySizeOf(string typeName)
        {
            var resolved = _registry.ResolveAlias(typeName?.Trim());
            if (resolved == null)
                throw new NotSupportedException("SIZEOF() requires a type name");

            if (AddressTypeInfo.IsAddressType(resolved))
                return (_target.AddressSize, _target.AddressSize);

            if (ArrayTypeInfo.IsArrayType(resolved))
            {
                var shape = ShapeOfArray(resolved);
                return (shape.Count * shape.ElementSize, shape.ElementAlign);
            }

            var structAst = _registry.GetStruct(resolved);
            if (structAst != null)
                return SizeOfStruct(structAst);

            var functionBlockAst = _registry.Get(resolved);
            if (functionBlockAst != null)
                return SizeOfFunctionBlock(functionBlockAst);

            if (StringTypeInfo.IsStringType(resolved))
            {
                // A string's characters are CharWidth-aligned, so a WSTRING
                // member pads itself and everything after it.
                var shape = ShapeOfString(resolved);
                return (shape.Size, shape.CharWidth);
            }

            if (Scalars.TryGetValue(resolved, out var scalar))
                return (scalar.Size, scalar.Size);

            return null;
        }

        // Writes value, already known to be of IEC type typeName, into buffer at
        // offset in the layout SizeOf measures - recursing into a STRUCT's
        // fields and an ARRAY's elements, and reaching the scalar and string
        // codecs at the leaves. A UNION is the one composite with nothing to
        // recurse into: its members are already views onto one buffer, and that
        // buffer is the value.
        //
        // Only the declared fields and elements are written. A struct's padding
        // has no storage behind it, so those bytes keep whatever the buffer
        // already held; callers wanting a whole-type image start from a zeroed
        // buffer.
        //
        // Grow-on-demand: POINTER/REFERENCE byte-packing is not modeled (no
        // fixture needs it), and a shape with no wire format is refused by name
        // rather than skipped, so an unpackable field cannot leave a
        // half-written image behind.
        public void Pack(byte[] buffer, int offset, object value, string typeName)
        {
            var resolved = _registry.ResolveAlias(typeName);

            if (TryPackScalar(buffer, offset, value, resolved))
                return;

            var structAst = _registry.GetStruct(resolved);
            if (structAst != null)
            {
                var instance = (StructInstance)value;
                if (structAst.IsUnion)
                {
                    var overlay = instance.Overlay
                        ?? throw new NotSupportedException(
                            $"UNION '{resolved}' has no overlaid storage behind it, so its bytes are undefined");
                    var overlaid = overlay.SettledBytes();
                    Array.Copy(overlaid, 0, buffer, offset, overlaid.Length);
                    return;
                }

                foreach (var placement in Fields(structAst))
                {
                    var field = placement.Field;
                    Pack(buffer, offset + placement.Offset, instance.Fields[field.Name].Value, field.TypeName);
                }
                return;
            }

            if (ArrayTypeInfo.IsArrayType(resolved))
            {
                var shape = ShapeOfArray(resolved);
                var array = (ArrayValue)value;
                for (var i = 0; i < shape.Count; i++)
                    Pack(buffer, offset + shape.OffsetOf(i), array.Elements[i], shape.ElementTypeName);
                return;
            }

            if (TryPackString(buffer, offset, value, resolved))
                return;

            throw new NotSupportedException(
                $"MEMCPY/MEMSET/MEMMOVE byte-packing doesn't support type '{resolved}' yet");
        }

        // Inverse of Pack: reconstructs a CLR value of the CLR shape
        // IecNumericType/DefaultValue use for typeName from buffer at offset.
        // The Cells and ArrayValue a composite is rebuilt into carry the
        // declared type and its STRING capacity, so a value that survives a byte
        // round trip is still as bounded as its declaration made it.
        public object Unpack(byte[] buffer, int offset, string typeName)
        {
            var resolved = _registry.ResolveAlias(typeName);

            if (TryUnpackScalar(buffer, offset, resolved, out var scalar))
                return scalar;

            var structAst = _registry.GetStruct(resolved);
            if (structAst != null)
            {
                if (structAst.IsUnion)
                {
                    var union = NewUnionInstance(structAst);
                    union.Overlay.Load(buffer, offset);
                    return union;
                }

                var instance = new StructInstance(resolved);
                foreach (var placement in Fields(structAst))
                {
                    var field = placement.Field;
                    instance.Fields[field.Name] = new Cell
                    {
                        Value = Unpack(buffer, offset + placement.Offset, field.TypeName),
                        DeclaredTypeName = field.TypeName,
                        StringCapacity = StringCapacityOf(field.TypeName),
                    };
                }
                return instance;
            }

            if (ArrayTypeInfo.IsArrayType(resolved))
            {
                var shape = ShapeOfArray(resolved);
                var elements = new object[shape.Count];
                for (var i = 0; i < elements.Length; i++)
                    elements[i] = Unpack(buffer, offset + shape.OffsetOf(i), shape.ElementTypeName);

                return new ArrayValue(
                    shape.Dimensions, shape.ElementTypeName, elements, StringCapacityOf(shape.ElementTypeName));
            }

            if (TryUnpackString(buffer, offset, resolved, out var text))
                return text;

            throw new NotSupportedException(
                $"MEMCPY/MEMSET/MEMMOVE byte-unpacking doesn't support type '{resolved}' yet");
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

        // Writes value into the fixed buffer typeName declares, when typeName is
        // a STRING or WSTRING, and reports whether it was: the text truncated to
        // the declared character count, then null-padded to the end of the
        // buffer so no byte of a previous value survives.
        //
        // Throws UnsupportedConstructException for text this encoding cannot
        // hold - above Latin-1 narrow, above the BMP wide - including text that
        // truncation would have dropped, because a value the interpreter cannot
        // represent is an error rather than something to quietly cut away.
        public bool TryPackString(byte[] buffer, int offset, object value, string typeName)
        {
            var resolved = _registry.ResolveAlias(typeName);
            if (!StringTypeInfo.IsStringType(resolved))
                return false;

            var shape = ShapeOfString(resolved);
            var text = (string)value ?? string.Empty;
            if (shape.CharWidth == 2)
                WideStringUnit.RequireRepresentable(text);

            // A CLR char is exactly one character on the wire in both encodings
            // once the surrogate case is excluded, so counting code units counts
            // characters. The narrow half needs no equivalent guard: Latin-1 has
            // no multi-unit character, and WriteChar rejects anything outside it.
            var charCount = Math.Min(text.Length, shape.Length);
            for (var i = 0; i < charCount; i++)
                shape.WriteChar(buffer, offset + i * shape.CharWidth, text[i]);

            for (var i = charCount * shape.CharWidth; i < shape.Size; i++)
                buffer[offset + i] = 0;

            return true;
        }

        // Inverse of TryPackString, with the same false meaning: not a string,
        // so not this class's to reconstruct. Reads up to the declared length,
        // stopping early at the terminator - one byte wide for a narrow STRING,
        // a whole zero WORD for a WSTRING, where a lone zero byte is the high
        // half of a legitimate Latin-1 character.
        public bool TryUnpackString(byte[] buffer, int offset, string typeName, out string text)
        {
            var resolved = _registry.ResolveAlias(typeName);
            if (!StringTypeInfo.IsStringType(resolved))
            {
                text = null;
                return false;
            }

            var shape = ShapeOfString(resolved);
            var count = 0;
            while (count < shape.Length && shape.ReadChar(buffer, offset + count * shape.CharWidth) != 0)
                count++;

            var chars = new char[count];
            for (var i = 0; i < count; i++)
                chars[i] = shape.ReadChar(buffer, offset + i * shape.CharWidth);

            text = new string(chars);
            return true;
        }

        private StringShape ShapeOfString(string resolvedTypeName) =>
            new StringShape(ParseStringLength(resolvedTypeName), StringTypeInfo.CharWidth(resolvedTypeName));

        private ArrayShape ShapeOfArray(string resolvedTypeName)
        {
            var (dimensions, elementTypeName) = ArrayTypeInfo.Parse(resolvedTypeName, _resolveBound);
            var (elementSize, elementAlign) = SizeOf(elementTypeName);
            return new ArrayShape(dimensions, elementTypeName, elementSize, elementAlign);
        }

        private int StringCapacityOf(string typeName) =>
            StringTypeInfo.ResolveCapacity(_registry.ResolveAlias(typeName), _resolveBound);

        // The value a UNION instantiates as: one backing buffer, and one typed
        // view per member onto it. Every construction site goes through here -
        // a declaration's default, a byte-model unpack, a copy - because a
        // union assembled field by field would have independent members, which
        // is precisely what a union is not.
        //
        // The buffer starts zeroed, and every IEC default is all-zero bytes, so
        // an untouched union reads as every member's own default without any
        // member having to be written. That matters for a member type with no
        // wire format: it costs nothing until something reads it.
        internal StructInstance NewUnionInstance(StructAst unionAst)
        {
            var storage = new UnionStorage(this, unionAst);
            var instance = new StructInstance(unionAst.Name) { Overlay = storage };
            foreach (var field in unionAst.Fields)
            {
                instance.Fields[field.Name] =
                    new UnionMemberCell(storage, field.TypeName, StringCapacityOf(field.TypeName));
                instance.FieldTypeNames[field.Name] = field.TypeName;
            }

            return instance;
        }

        // Each field's placement inside structAst, in declaration order.
        public IEnumerable<FieldPlacement> Fields(StructAst structAst) =>
            structAst.IsUnion ? OverlaidFields(structAst) : SequentialFields(structAst);

        private IEnumerable<FieldPlacement> SequentialFields(StructAst structAst)
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

        // A UNION's fields all start at offset 0, so none of them displaces
        // another and the alignment each imposes is still its own.
        private IEnumerable<FieldPlacement> OverlaidFields(StructAst unionAst)
        {
            var packBound = PackBound(unionAst);
            foreach (var field in unionAst.Fields)
            {
                var (size, align) = SizeOfOverlaidField(field.TypeName);
                yield return new FieldPlacement(field, 0, size, Math.Min(align, packBound));
            }
        }

        // BIT is the one member type with no byte width of its own, and what
        // makes it sub-byte is sharing a byte with the members around it. A
        // union field has no neighbours to share with - every field starts at
        // offset 0 - so it gets a whole byte, which is what the compiler
        // declares for the BIT member of TcUnit's U_ExpectedOrActual on both
        // targets. Inside a STRUCT, BIT stays unmodeled and SizeOf refuses it.
        private (int Size, int Align) SizeOfOverlaidField(string typeName) =>
            IecIdentifier.Matches(_registry.ResolveAlias(typeName?.Trim()), "BIT")
                ? (1, 1)
                : SizeOf(typeName);

        // The length may be a non-literal constant expression (e.g. a
        // GVL-qualified constant), so it goes through _resolveBound rather than
        // a bare int.Parse, same as an ARRAY bound. With no resolver, the
        // literal-only overload's own refusal is the honest error.
        private int ParseStringLength(string resolvedTypeName) =>
            _resolveBound == null
                ? StringTypeInfo.ParseLength(resolvedTypeName)
                : StringTypeInfo.ParseLength(resolvedTypeName, _resolveBound);

        internal (int Size, int Align) SizeOfStruct(StructAst structAst)
        {
            var end = 0;
            var maxAlign = 1;
            foreach (var placement in Fields(structAst))
            {
                // The furthest field reached, not the last one placed: a UNION
                // overlays every field at offset 0, so its widest field is what
                // the type has to be able to hold.
                end = Math.Max(end, placement.Offset + placement.Size);
                maxAlign = Math.Max(maxAlign, placement.Align);
            }

            return (RoundUp(end, maxAlign), maxAlign);
        }

        // Instance data only: the hidden vtable pointer, one more hidden pointer
        // when a single interface is implemented, then every inherited and own
        // member in declaration order, base type first. The rules are documented in
        // wiki/10-function-block-layout.md.
        private (int Size, int Align) SizeOfFunctionBlock(PouAst functionBlock)
        {
            if (functionBlock.Kind != PouKind.FunctionBlock)
                throw new NotSupportedException(
                    $"SIZEOF() only sizes a FUNCTION_BLOCK; '{functionBlock.Name}' is a PROGRAM or FUNCTION");

            if (!_functionBlocksBeingSized.Add(functionBlock.Name))
                throw new NotSupportedException(
                    $"SIZEOF() can't size FUNCTION_BLOCK '{functionBlock.Name}': it contains itself");

            try
            {
                var address = _target.AddressSize;
                var end = FunctionBlockHeaderSize(functionBlock);
                var maxAlign = address;
                foreach (var placement in FunctionBlockFields(functionBlock))
                {
                    end = placement.Offset + placement.Size;
                    maxAlign = Math.Max(maxAlign, placement.Align);
                }

                return (RoundUp(end, maxAlign), maxAlign);
            }
            finally
            {
                _functionBlocksBeingSized.Remove(functionBlock.Name);
            }
        }

        // Each declared instance member's placement, past the hidden header.
        // The header's pointers are not members, so they are not yielded.
        internal IEnumerable<FieldPlacement> FunctionBlockFields(PouAst functionBlock)
        {
            var chain = ExtendsChain(functionBlock);
            RejectMethodInstanceVariables(functionBlock, chain);
            var address = _target.AddressSize;
            var offset = FunctionBlockHeaderSize(functionBlock);
            var members = chain
                .SelectMany(pou => _registry.GetDecls(pou.DeclarationText))
                .Where(decl => IsInstanceSection(decl.Section));
            foreach (var member in members)
            {
                var (size, align) = member.Section == VarSection.InOut
                    ? (address, address)
                    : SizeOfMember(functionBlock, member);
                offset = RoundUp(offset, align);
                yield return new FieldPlacement(member, offset, size, align);
                offset += size;
            }
        }

        private void RejectMethodInstanceVariables(PouAst functionBlock, IEnumerable<PouAst> chain)
        {
            foreach (var pou in chain)
            {
                var method = pou.Methods.FirstOrDefault(m =>
                    _registry.GetDecls(m.DeclarationText).Any(d => d.Section == VarSection.MethodInstance));
                if (method != null)
                    throw new NotSupportedException(
                        $"SIZEOF() can't size FUNCTION_BLOCK '{functionBlock.Name}': method '{method.Name}' " +
                        $"of '{pou.Name}' declares VAR_INST, which is stored in the instance and not modelled");
            }
        }

        private int FunctionBlockHeaderSize(PouAst functionBlock)
        {
            var interfaceCount = ExtendsChain(functionBlock).Sum(pou => pou.ImplementedInterfaces.Count);
            if (interfaceCount > 1)
                throw new NotSupportedException(
                    $"SIZEOF() can't size FUNCTION_BLOCK '{functionBlock.Name}': the layout of a block " +
                    $"implementing {interfaceCount} interfaces is not verified");

            return _target.AddressSize * (1 + interfaceCount);
        }

        private List<PouAst> ExtendsChain(PouAst functionBlock)
        {
            var chain = new List<PouAst>();
            var visited = new HashSet<string>();
            var current = functionBlock;
            while (current != null)
            {
                if (!visited.Add(current.Name))
                    throw new NotSupportedException(
                        $"SIZEOF() can't size FUNCTION_BLOCK '{functionBlock.Name}': " +
                        $"its EXTENDS chain loops back to '{current.Name}'");

                chain.Add(current);
                if (current.BaseTypeName == null)
                    break;

                var baseType = _registry.Get(current.BaseTypeName);
                if (baseType == null)
                    throw new NotSupportedException(
                        $"SIZEOF() can't size FUNCTION_BLOCK '{functionBlock.Name}': its base type " +
                        $"'{current.BaseTypeName}' is not a loaded FUNCTION_BLOCK, and native library " +
                        "function blocks are not modelled");

                current = baseType;
            }

            chain.Reverse();
            return chain;
        }

        private (int Size, int Align) SizeOfMember(PouAst functionBlock, VarDecl member)
        {
            var resolved = _registry.ResolveAlias(member.TypeName?.Trim());
            if (resolved != null && _registry.GetInterface(resolved) != null)
                throw new NotSupportedException(
                    $"SIZEOF() can't size FUNCTION_BLOCK '{functionBlock.Name}': member '{member.Name}' " +
                    $"has interface type '{member.TypeName}', which is not modelled");

            (int Size, int Align)? sized;
            try
            {
                sized = TrySizeOf(member.TypeName);
            }
            catch (NotSupportedException ex)
            {
                throw new NotSupportedException(
                    $"SIZEOF() can't size FUNCTION_BLOCK '{functionBlock.Name}': member '{member.Name}' " +
                    $"of type '{member.TypeName}': {ex.Message}", ex);
            }

            return sized ?? throw new NotSupportedException(
                $"SIZEOF() can't size FUNCTION_BLOCK '{functionBlock.Name}': member '{member.Name}' " +
                $"of type '{member.TypeName}' has no modelled layout; native library function " +
                "blocks are not modelled");
        }

        private static bool IsInstanceSection(VarSection section) =>
            section == VarSection.Local ||
            section == VarSection.Input ||
            section == VarSection.Output ||
            section == VarSection.InOut;

        // An absent pack_mode means no cap, i.e. natural alignment. A present
        // one caps every field's alignment at that many bytes; 1 is fully
        // byte-packed, and TwinCAT reads an explicit 0 the same way rather
        // than as "no cap".
        private static int PackBound(StructAst structAst) =>
            structAst.PackMode.HasValue ? Math.Max(structAst.PackMode.Value, 1) : int.MaxValue;

        private static int RoundUp(int value, int align) => (value + align - 1) / align * align;
    }
}
