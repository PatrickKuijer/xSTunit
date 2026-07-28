using System;
using System.Linq;

namespace TcXunit.Interpreter
{
    public sealed partial class Engine
    {
        // MEMCPY/MEMSET/MEMMOVE (TcXunit-4vn): a dest/src pointer whose
        // target is a plain scalar or STRUCT/struct-field Cell - not an
        // ArrayElementCell - has no array element to index into (the
        // ADR(struct.field)/ADR(scalarVar) case, as opposed to
        // ADR(byteBuf)/ADR(byteBuf[i]), TcXunit-sej.3). Reusing the SIZEOF
        // byte-layout math (TcXunit-l1x), pack the Cell's current value into
        // a same-size byte buffer so the existing ArrayValue-indexed copy
        // loop in MemCopy/MemSet can address it exactly like a real BYTE
        // array, then hand back a Commit callback that unpacks the buffer's
        // final bytes back into the Cell - the caller invokes it once for
        // whichever side was actually mutated (dest only; src is read-only).
        private (ArrayValue Array, int Index, Action Commit) ResolveByteTarget(Pointer ptr, string methodName, string paramName, Frame frame)
        {
            if (ptr.Target is ArrayElementCell aec)
                return (aec.Array, aec.Index, null);

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

        // Packs cell's current value into a fresh BYTE-array view the same
        // way ResolveByteTarget does, but without the write-back Commit -
        // used by pointer arithmetic (EvaluatePointerArithmetic,
        // TcXunit-sej.2), which only needs to read through a struct/scalar
        // byte offset (ptr^ := x isn't a supported assignment target yet -
        // Parser.RequireLValue - so there is nothing to commit back to).
        // Rebuilt fresh on every call rather than cached: cheap enough for
        // the sizes these fixtures use, and it means a repeated ADR(x) + i
        // always reflects x's current live value instead of a stale snapshot.
        private (ArrayValue View, int Size) PackCellToByteView(Cell cell, string typeName, Frame frame)
        {
            var (size, _) = SizeOfType(typeName, frame);

            var bytes = new byte[size];
            PackValue(bytes, 0, cell.Value, typeName, frame);

            var elements = new object[size];
            for (var i = 0; i < size; i++)
                elements[i] = (int)bytes[i];

            return (new ArrayValue(new[] { (0, size - 1) }, "BYTE", elements), size);
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
                var fieldOffset = 0;
                foreach (var field in structAst.Fields)
                {
                    var (fieldSize, fieldAlign) = SizeOfType(field.TypeName, frame);
                    fieldOffset = RoundUp(fieldOffset, fieldAlign);
                    PackValue(buffer, offset + fieldOffset, instance.Fields[field.Name].Value, field.TypeName, frame);
                    fieldOffset += fieldSize;
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
                // Wire format is a fixed length+1 byte buffer (matching
                // SizeOfType's STRING/WSTRING size), ASCII, null-terminated:
                // truncate to length chars, then null-pad (and terminate)
                // the rest. WSTRING mirrors STRING here (StringTypeInfo's
                // character width isn't enforced elsewhere either).
                var length = StringTypeInfo.ParseLength(
                    resolved, boundText => Convert.ToInt32(Evaluate(Parser.ParseExpression(boundText), frame)));
                var text = (string)value ?? string.Empty;
                var charCount = Math.Min(text.Length, length);
                for (var i = 0; i < charCount; i++)
                    buffer[offset + i] = unchecked((byte)text[i]);
                for (var i = charCount; i <= length; i++)
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
                var fieldOffset = 0;
                foreach (var field in structAst.Fields)
                {
                    var (fieldSize, fieldAlign) = SizeOfType(field.TypeName, frame);
                    fieldOffset = RoundUp(fieldOffset, fieldAlign);
                    instance.Fields[field.Name] = new Cell
                    {
                        Value = UnpackValue(buffer, offset + fieldOffset, field.TypeName, frame),
                        DeclaredTypeName = field.TypeName,
                    };
                    fieldOffset += fieldSize;
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
                return new ArrayValue(dimensions, elementTypeName, elements);
            }

            if (StringTypeInfo.IsStringType(resolved))
            {
                // Inverse of the PackValue case above: read up to the
                // declared length, stopping early at the null terminator.
                var length = StringTypeInfo.ParseLength(
                    resolved, boundText => Convert.ToInt32(Evaluate(Parser.ParseExpression(boundText), frame)));
                var end = offset;
                var max = offset + length;
                while (end < max && buffer[end] != 0)
                    end++;
                var chars = new char[end - offset];
                for (var i = 0; i < chars.Length; i++)
                    chars[i] = (char)buffer[offset + i];
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
