using System;
using System.Linq;

namespace xStunit.Interpreter
{
    public sealed partial class Engine
    {
        // Presents whatever a MEMCPY/MEMSET/MEMMOVE pointer targets as an
        // ArrayValue of bytes plus a byte offset into it, so those intrinsics
        // have one indexable shape to copy through.
        //
        // A pointer to a plain scalar or STRUCT/struct-field Cell -
        // ADR(scalarVar), ADR(struct.field) - has no array element to index,
        // so its value is packed into a same-size byte buffer using the SIZEOF
        // byte-layout math. Commit unpacks the buffer's final bytes back into
        // the Cell and is returned only for that case; the caller invokes it
        // for whichever side was mutated (dest only - src is read-only).
        private (ArrayValue Array, int Index, Action Commit) ResolveByteTarget(Pointer ptr, string methodName, string paramName, Frame frame)
        {
            if (ptr.Target is ArrayElementCell aec)
            {
                // A BYTE-element array already IS a byte view - Elements is
                // directly byte-indexable and Index is already a byte offset,
                // so returning it as-is (no repacking) is correct and cheap.
                var elementTypeName = _registry.ResolveAlias(aec.Array.ElementTypeName);
                if (elementTypeName == "BYTE")
                    return (aec.Array, aec.Index, null);

                // For any other element type Elements.Length is an element
                // count, not a byte count, so a caller bounds-checking a
                // SIZEOF()-computed byte size against it (ReadPointerBytes)
                // would see the buffer as elementSize times too small.
                return ResolveArrayByteTarget(aec, frame);
            }

            var cell = ptr.Target;
            if (cell.DeclaredTypeName == null)
            {
                throw new NotSupportedException(
                    $"{methodName} '{paramName}' pointer must target an array element (e.g. ADR(buf) or ADR(buf[i])) " +
                    "or a variable/field with a known declared type - byte-offset into an untyped Cell isn't modeled.");
            }

            var typeName = _registry.ResolveAlias(cell.DeclaredTypeName);
            var (view, size) = PackCellToByteView(cell, typeName, frame);

            void Commit()
            {
                var outBytes = new byte[size];
                for (var i = 0; i < size; i++)
                    outBytes[i] = (byte)(int)view.Elements[i];
                cell.Value = UnpackValue(outBytes, 0, typeName, frame);
            }

            return (view, 0, Commit);
        }

        // Packs aec's whole underlying ArrayValue into a fresh BYTE-array view,
        // elements laid a uniform elementSize apart to match PackValue's own
        // ARRAY branch - no inter-element padding, only intra-element
        // struct-field alignment - and maps aec.Index from an element index to
        // the byte offset of that element's first byte.
        //
        // Commit is needed even though ReadPointerBytes discards it: MEMCPY and
        // MEMSET writing through a dest pointer into an array-of-struct element
        // still have to get the mutated bytes back into the real elements.
        private (ArrayValue Array, int Index, Action Commit) ResolveArrayByteTarget(ArrayElementCell aec, Frame frame)
        {
            var elementTypeName = aec.Array.ElementTypeName;
            var (elementSize, _) = SizeOfType(elementTypeName, frame);
            var elementCount = aec.Array.Elements.Length;
            var totalSize = elementCount * elementSize;

            var bytes = new byte[totalSize];
            for (var i = 0; i < elementCount; i++)
                PackValue(bytes, i * elementSize, aec.Array.Elements[i], elementTypeName, frame);

            var elements = new object[totalSize];
            for (var i = 0; i < totalSize; i++)
                elements[i] = (int)bytes[i];

            var view = new ArrayValue(new[] { (0, totalSize - 1) }, "BYTE", elements, Cell.Unbounded);

            void Commit()
            {
                var outBytes = new byte[totalSize];
                for (var i = 0; i < totalSize; i++)
                    outBytes[i] = (byte)(int)elements[i];
                for (var i = 0; i < elementCount; i++)
                    aec.Array.Elements[i] = UnpackValue(outBytes, i * elementSize, elementTypeName, frame);
            }

            return (view, aec.Index * elementSize, Commit);
        }

        // ResolveByteTarget's packing without the write-back Commit, for pointer
        // arithmetic, which only reads through a struct/scalar byte offset -
        // ptr^ := x is not a supported assignment target (Parser.RequireLValue),
        // so there is nothing to commit back to.
        //
        // Rebuilt on every call rather than cached, so a repeated ADR(x) + i
        // always reflects x's current live value instead of a stale snapshot.
        private (ArrayValue View, int Size) PackCellToByteView(Cell cell, string typeName, Frame frame)
        {
            var (size, _) = SizeOfType(typeName, frame);

            var bytes = new byte[size];
            PackValue(bytes, 0, cell.Value, typeName, frame);

            var elements = new object[size];
            for (var i = 0; i < size; i++)
                elements[i] = (int)bytes[i];

            return (new ArrayValue(new[] { (0, size - 1) }, "BYTE", elements, Cell.Unbounded), size);
        }

        // Writes value (already known to be of IEC type typeName) into
        // buffer at offset, byte-for-byte, using the same natural-alignment
        // struct/array layout SizeOfType computes. Grow-on-demand: only the
        // scalar/STRUCT/ARRAY/STRING shapes SizeOfType itself understands are
        // supported here; POINTER/REFERENCE byte-packing isn't modeled yet
        // (no fixture needs it).
        private void PackValue(byte[] buffer, int offset, object value, string typeName, Frame frame)
        {
            var resolved = _registry.ResolveAlias(typeName);

            if (resolved == "BOOL")
            {
                buffer[offset] = (byte)((bool)value ? 1 : 0);
                return;
            }

            var structAst = _registry.GetStruct(resolved);
            if (structAst != null)
            {
                var instance = (StructInstance)value;
                foreach (var placement in LayoutFor(frame).Fields(structAst))
                {
                    var field = placement.Field;
                    PackValue(buffer, offset + placement.Offset, instance.Fields[field.Name].Value, field.TypeName, frame);
                }
                return;
            }

            if (ArrayTypeInfo.IsArrayType(resolved))
            {
                var (dimensions, elementTypeName) = ArrayTypeInfo.Parse(
                    resolved, boundText => Convert.ToInt32(Evaluate(Parser.ParseExpression(boundText), frame)));
                var count = dimensions.Aggregate(1, (acc, d) => acc * (d.Hi - d.Lo + 1));
                var (elementSize, _) = SizeOfType(elementTypeName, frame);
                var array = (ArrayValue)value;
                for (var i = 0; i < count; i++)
                    PackValue(buffer, offset + i * elementSize, array.Elements[i], elementTypeName, frame);
                return;
            }

            if (StringTypeInfo.IsStringType(resolved))
            {
                // Wire format is the fixed buffer SizeOfType computes for the
                // declared length, null-terminated: truncate to length
                // characters, then null-pad (and terminate) the rest. A narrow
                // STRING is one Latin-1 byte per character; a WSTRING is
                // little-endian UCS-2, matching TwinCAT on x86.
                var length = StringTypeInfo.ParseLength(
                    resolved, boundText => Convert.ToInt32(Evaluate(Parser.ParseExpression(boundText), frame)));
                var text = (string)value ?? string.Empty;
                var charWidth = StringTypeInfo.CharWidth(resolved);
                if (charWidth == 2)
                    WideStringUnit.RequireRepresentable(text);

                // A CLR char is exactly one character on the wire in both
                // encodings once the surrogate case is excluded, so counting
                // code units counts characters. The narrow half needs no
                // equivalent guard: Latin-1 has no multi-unit character, and
                // FromChar below rejects anything outside it.
                var charCount = Math.Min(text.Length, length);
                for (var i = 0; i < charCount; i++)
                {
                    if (charWidth == 2)
                    {
                        buffer[offset + 2 * i] = (byte)(text[i] & 0xFF);
                        buffer[offset + 2 * i + 1] = (byte)(text[i] >> 8);
                    }
                    else
                    {
                        buffer[offset + i] = NarrowStringByte.FromChar(text[i]);
                    }
                }
                for (var i = charCount * charWidth; i < (length + 1) * charWidth; i++)
                    buffer[offset + i] = 0;
                return;
            }

            switch (resolved)
            {
                case "SINT":
                    buffer[offset] = unchecked((byte)(sbyte)(int)value);
                    return;
                case "USINT":
                case "BYTE":
                    buffer[offset] = (byte)(int)value;
                    return;
                case "INT":
                    BitConverter.GetBytes((short)(int)value).CopyTo(buffer, offset);
                    return;
                case "UINT":
                case "WORD":
                    BitConverter.GetBytes((ushort)(int)value).CopyTo(buffer, offset);
                    return;
                case "DINT":
                    BitConverter.GetBytes((int)value).CopyTo(buffer, offset);
                    return;
                case "UDINT":
                case "DWORD":
                    BitConverter.GetBytes((uint)(long)value).CopyTo(buffer, offset);
                    return;
                case "TIME":
                case "DATE":
                case "DATE_AND_TIME":
                case "TIME_OF_DAY":
                    BitConverter.GetBytes((uint)value).CopyTo(buffer, offset);
                    return;
                case "LINT":
                    BitConverter.GetBytes((long)value).CopyTo(buffer, offset);
                    return;
                case "ULINT":
                case "LWORD":
                    BitConverter.GetBytes((ulong)value).CopyTo(buffer, offset);
                    return;
                case "LTIME":
                    BitConverter.GetBytes((ulong)value).CopyTo(buffer, offset);
                    return;
                case "REAL":
                    BitConverter.GetBytes((float)value).CopyTo(buffer, offset);
                    return;
                case "LREAL":
                    BitConverter.GetBytes((double)value).CopyTo(buffer, offset);
                    return;
                default:
                    throw new NotSupportedException(
                        $"MEMCPY/MEMSET/MEMMOVE byte-packing doesn't support type '{resolved}' yet");
            }
        }

        private static char ReadStringChar(byte[] buffer, int offset, int charWidth) =>
            charWidth == 2
                ? (char)(buffer[offset] | (buffer[offset + 1] << 8))
                : NarrowStringByte.ToChar(buffer[offset]);

        // Inverse of PackValue: reconstructs a CLR value of the CLR shape
        // IecNumericType/DefaultValue use for typeName from buffer at
        // offset.
        private object UnpackValue(byte[] buffer, int offset, string typeName, Frame frame)
        {
            var resolved = _registry.ResolveAlias(typeName);

            if (resolved == "BOOL")
                return buffer[offset] != 0;

            var structAst = _registry.GetStruct(resolved);
            if (structAst != null)
            {
                var instance = new StructInstance(resolved);
                foreach (var placement in LayoutFor(frame).Fields(structAst))
                {
                    var field = placement.Field;
                    instance.Fields[field.Name] = NewDeclaredCell(
                        UnpackValue(buffer, offset + placement.Offset, field.TypeName, frame),
                        field.TypeName,
                        frame.Instance);
                }
                return instance;
            }

            if (ArrayTypeInfo.IsArrayType(resolved))
            {
                var (dimensions, elementTypeName) = ArrayTypeInfo.Parse(
                    resolved, boundText => Convert.ToInt32(Evaluate(Parser.ParseExpression(boundText), frame)));
                var count = dimensions.Aggregate(1, (acc, d) => acc * (d.Hi - d.Lo + 1));
                var (elementSize, _) = SizeOfType(elementTypeName, frame);
                var elements = new object[count];
                for (var i = 0; i < count; i++)
                    elements[i] = UnpackValue(buffer, offset + i * elementSize, elementTypeName, frame);
                return new ArrayValue(
                    dimensions, elementTypeName, elements, ResolveStringCapacity(elementTypeName, frame.Instance));
            }

            if (StringTypeInfo.IsStringType(resolved))
            {
                // Inverse of the PackValue case above: read up to the
                // declared length, stopping early at the null terminator - one
                // byte wide for a narrow STRING, a whole zero WORD for a
                // WSTRING, where a lone zero byte is the high half of a
                // legitimate Latin-1 character.
                var length = StringTypeInfo.ParseLength(
                    resolved, boundText => Convert.ToInt32(Evaluate(Parser.ParseExpression(boundText), frame)));
                var charWidth = StringTypeInfo.CharWidth(resolved);
                var count = 0;
                while (count < length && ReadStringChar(buffer, offset + count * charWidth, charWidth) != 0)
                    count++;
                var chars = new char[count];
                for (var i = 0; i < count; i++)
                    chars[i] = ReadStringChar(buffer, offset + i * charWidth, charWidth);
                return new string(chars);
            }

            switch (resolved)
            {
                case "SINT":
                    return (int)unchecked((sbyte)buffer[offset]);
                case "USINT":
                case "BYTE":
                    return (int)buffer[offset];
                case "INT":
                    return (int)BitConverter.ToInt16(buffer, offset);
                case "UINT":
                case "WORD":
                    return (int)BitConverter.ToUInt16(buffer, offset);
                case "DINT":
                    return BitConverter.ToInt32(buffer, offset);
                case "UDINT":
                case "DWORD":
                    return (long)BitConverter.ToUInt32(buffer, offset);
                case "TIME":
                case "DATE":
                case "DATE_AND_TIME":
                case "TIME_OF_DAY":
                    return BitConverter.ToUInt32(buffer, offset);
                case "LINT":
                    return BitConverter.ToInt64(buffer, offset);
                case "ULINT":
                case "LWORD":
                    return BitConverter.ToUInt64(buffer, offset);
                case "LTIME":
                    return BitConverter.ToUInt64(buffer, offset);
                case "REAL":
                    return BitConverter.ToSingle(buffer, offset);
                case "LREAL":
                    return BitConverter.ToDouble(buffer, offset);
                default:
                    throw new NotSupportedException(
                        $"MEMCPY/MEMSET/MEMMOVE byte-unpacking doesn't support type '{resolved}' yet");
            }
        }
    }
}
