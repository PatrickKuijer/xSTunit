namespace xStunit.Interpreter
{
    // Mutable storage slot, and the unit of aliasing: POINTER TO holds a Cell
    // reference as its value (see Pointer), REFERENCE TO aliases another
    // variable's Cell directly rather than copying its value, so an
    // assignment through either propagates. Value is virtual so
    // ArrayElementCell and StringByteCell can proxy a slot inside a larger
    // value instead of owning storage.
    public class Cell
    {
        public const int Unbounded = -1;

        private object _value;
        private int _stringCapacity = Unbounded;

        public virtual object Value
        {
            get => _value;
            set => _value = ClampToCapacity(value, _stringCapacity);
        }

        // The IEC type text (e.g. "INT", "REFERENCE TO INT", "POINTER TO
        // BYTE") this Cell was declared with, set alongside Value at every
        // VarDecl-driven construction. __ISVALIDREF needs it to reject
        // non-reference/pointer variables, since every other type's default
        // value is non-null and would make the check meaningless. Null for
        // Cells with no declared variable behind them (hardcoded native host
        // fields such as TON's IN/PT/Q/ET) and for ArrayElementCell.
        public string DeclaredTypeName { get; set; }

        // Character capacity of a STRING(n)/WSTRING(n) declaration, Unbounded
        // for every other type. A STRING declaration reserves exactly n
        // characters and TwinCAT drops what a longer assignment does not fit
        // rather than growing the variable; the byte model has always truncated
        // there (Engine.ByteLayout's PackValue), so without this the same
        // variable reads one length in the interpreter and another after an
        // ADR() round trip.
        //
        // Set by whoever declared the Cell (Engine.ResolveStringCapacity) and
        // never derived from DeclaredTypeName here: an alias type or a
        // constant-expression size needs the type registry and an expression
        // evaluator to settle, and a Cell has neither.
        //
        // Re-clamps the current value because an object initializer assigns
        // Value first: `new Cell { Value = x, StringCapacity = n }` would
        // otherwise seat an over-long initial value under a capacity it
        // violates.
        public int StringCapacity
        {
            get => _stringCapacity;
            set
            {
                _stringCapacity = value;
                _value = ClampToCapacity(_value, _stringCapacity);
            }
        }

        // Shared with ArrayValue, which enforces the same capacity on writes
        // that reach an element without going through an ArrayElementCell.
        public static object ClampToCapacity(object value, int capacity) =>
            capacity >= 0 && value is string text && text.Length > capacity
                ? text.Substring(0, capacity)
                : value;
    }
}
