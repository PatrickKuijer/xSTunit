using System;

namespace xStunit.Interpreter
{
    // The one storage a UNION instance has, and the coherence rule its member
    // views share. A union is overlaid memory by definition, so the bytes are
    // what the value IS; every member is a reinterpretation of them through the
    // codec its own declared type names.
    //
    // Members are not read straight out of the buffer on every touch, because a
    // composite member hands out a mutable object - an ArrayValue, a nested
    // StructInstance - that the interpreter then writes into in place
    // (u.asBytes[1] := x never goes through the member's own setter). So one
    // member at a time is the AUTHORITY: the one whose materialised value may
    // be newer than the buffer. Reaching any other member folds the authority's
    // value back into the buffer first. Every ST statement re-resolves its
    // union member from the variable, so a mutation is always folded in before
    // anything else can observe the storage.
    //
    // The buffer is allocated on first use rather than at construction, so a
    // union carrying a member whose type has no byte width - and which
    // therefore cannot be sized - still instantiates, and only escalates if
    // something actually reads across the overlay.
    internal sealed class UnionStorage
    {
        private readonly TypeLayout _layout;
        private readonly StructAst _unionAst;
        private byte[] _bytes;
        private UnionMemberCell _authority;

        public UnionStorage(TypeLayout layout, StructAst unionAst)
        {
            _layout = layout;
            _unionAst = unionAst;
        }

        // Materialises member from the overlaid bytes and makes it the
        // authority, for a read of its value.
        public void Materialize(UnionMemberCell member)
        {
            if (ReferenceEquals(_authority, member))
                return;

            Settle();
            member.UnpackFrom(_layout, Bytes);
            _authority = member;
        }

        // Hands the storage to member for a write of its whole value. Unlike
        // Materialize there is nothing to read back first: the incoming value
        // replaces whatever this member overlapped.
        public void Claim(UnionMemberCell member)
        {
            if (ReferenceEquals(_authority, member))
                return;

            Settle();
            _authority = member;
        }

        // The overlaid bytes, with any pending member write folded in - what a
        // MEMCPY out of the union copies.
        public byte[] SettledBytes()
        {
            Settle();
            return Bytes;
        }

        // Replaces the whole overlay with bytes copied in from outside, which
        // supersedes every materialised member value.
        public void Load(byte[] source, int offset)
        {
            _authority = null;
            Array.Copy(source, offset, Bytes, 0, Bytes.Length);
        }

        // A copy that shares nothing with this one: same bytes, fresh views.
        // CellCloner cannot rebuild a union field by field, because independent
        // Cells are exactly what a union does not have.
        public StructInstance CloneInstance()
        {
            var clone = _layout.NewUnionInstance(_unionAst);
            clone.Overlay.Load(SettledBytes(), 0);
            return clone;
        }

        private byte[] Bytes => _bytes ?? (_bytes = new byte[_layout.SizeOfStruct(_unionAst).Size]);

        private void Settle()
        {
            _authority?.PackInto(_layout, Bytes);
            _authority = null;
        }
    }
}
