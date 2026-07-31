namespace xStunit.Interpreter
{
    // A Cell view onto a single byte of a STRING variable, so s[n] can be a
    // REF=/ADR()/Transmit() target - the STRING counterpart to
    // ArrayElementCell. Unlike that one, which mutates an Elements slot in
    // place, System.String is immutable: a write here rebuilds the string and
    // replaces the parent Cell's Value wholesale.
    public sealed class StringByteCell : Cell
    {
        public Cell Parent { get; }
        public int Index { get; }

        public StringByteCell(Cell parent, int index)
        {
            Parent = parent;
            Index = index;
        }

        public override object Value
        {
            get => Engine.GetStringByte((string)Parent.Value, Index);
            set => Parent.Value = Engine.SetStringByte((string)Parent.Value, Index, System.Convert.ToInt32(value));
        }
    }
}
