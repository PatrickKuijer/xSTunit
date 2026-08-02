namespace xStunit.Interpreter
{
    // A Cell view onto one flattened slot of an ArrayValue.Elements array
    // rather than storage of its own, so array elements go through the same
    // lvalue/ADR/REF= machinery as plain variables. Also where Pointer
    // arithmetic lands: advancing ADR(x) by an offset means a new
    // ArrayElementCell at Index + delta on the same Array.
    public sealed class ArrayElementCell : Cell
    {
        public ArrayValue Array { get; }
        public int Index { get; }

        public ArrayElementCell(ArrayValue array, int index)
        {
            Array = array;
            Index = index;
            StringCapacity = array.ElementStringCapacity;
        }

        public override object Value
        {
            get => Array.Elements[Index];
            set => Array.SetElement(Index, value);
        }
    }
}
