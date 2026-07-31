namespace xStunit.Interpreter
{
    // A Cell view onto a single byte within a STRING variable's own Cell -
    // the STRING-typed counterpart to ArrayElementCell, for REF=/ADR()/
    // Transmit() targeting a string index (s[n], TcXunit-3jr). Unlike
    // ArrayElementCell (which mutates an ArrayValue.Elements slot in place),
    // System.String is immutable, so a write here replaces the parent
    // Cell's Value wholesale via Engine's SetStringByte.
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
