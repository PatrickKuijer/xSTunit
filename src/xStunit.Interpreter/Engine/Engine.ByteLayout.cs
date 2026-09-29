using System;

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
                if (IsNamed(elementTypeName, "BYTE"))
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
                cell.Value = LayoutFor(frame).Unpack(outBytes, 0, typeName);
            }

            return (view, 0, Commit);
        }

        // Packs aec's whole underlying ArrayValue into a fresh BYTE-array view,
        // elements laid a uniform elementSize apart to match the layout module's
        // own ARRAY stride - no inter-element padding, only intra-element
        // struct-field alignment - and maps aec.Index from an element index to
        // the byte offset of that element's first byte.
        //
        // Commit is needed even though ReadPointerBytes discards it: MEMCPY and
        // MEMSET writing through a dest pointer into an array-of-struct element
        // still have to get the mutated bytes back into the real elements.
        private (ArrayValue Array, int Index, Action Commit) ResolveArrayByteTarget(ArrayElementCell aec, Frame frame)
        {
            var layout = LayoutFor(frame);
            var elementTypeName = aec.Array.ElementTypeName;
            var (elementSize, _) = layout.SizeOf(elementTypeName);
            var elementCount = aec.Array.Elements.Length;
            var totalSize = elementCount * elementSize;

            var bytes = new byte[totalSize];
            for (var i = 0; i < elementCount; i++)
                layout.Pack(bytes, i * elementSize, aec.Array.Elements[i], elementTypeName);

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
                    aec.Array.Elements[i] = layout.Unpack(outBytes, i * elementSize, elementTypeName);
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
            var layout = LayoutFor(frame);
            var (size, _) = layout.SizeOf(typeName);

            var bytes = new byte[size];
            layout.Pack(bytes, 0, cell.Value, typeName);

            var elements = new object[size];
            for (var i = 0; i < size; i++)
                elements[i] = (int)bytes[i];

            return (new ArrayValue(new[] { (0, size - 1) }, "BYTE", elements, Cell.Unbounded), size);
        }
    }
}
