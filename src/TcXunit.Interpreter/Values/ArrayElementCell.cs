namespace TcXunit.Interpreter
{
    // A Cell view onto one flattened slot of an ArrayValue.Elements array,
    // rather than owning its own storage. Lets array-element access go
    // through the same Cell-based lvalue/ADR/REF= machinery as plain
    // variables and struct fields, and gives Pointer arithmetic
    // (ADR(x) + offset) a place to land: advancing a pointer means building
    // a new ArrayElementCell at Index + delta on the same Array
    // (TcXunit-sej.2).
    public sealed class ArrayElementCell : Cell
    {
        public ArrayValue Array { get; }
        public int Index { get; }

        public ArrayElementCell(ArrayValue array, int index)
        {
            Array = array;
            Index = index;
        }

        public override object Value
        {
            get => Array.Elements[Index];
            set => Array.Elements[Index] = value;
        }
    }
}
