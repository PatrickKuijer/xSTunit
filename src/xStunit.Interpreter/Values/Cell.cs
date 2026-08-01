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
        private const int Unbounded = -1;

        private object _value;
        private string _declaredTypeName;
        private int _stringCapacity = Unbounded;

        public virtual object Value
        {
            get => _value;
            set => _value = ClampToDeclaredCapacity(value);
        }

        // The IEC type text (e.g. "INT", "REFERENCE TO INT", "POINTER TO
        // BYTE") this Cell was declared with, set alongside Value at every
        // VarDecl-driven construction. __ISVALIDREF needs it to reject
        // non-reference/pointer variables, since every other type's default
        // value is non-null and would make the check meaningless. Null for
        // Cells with no declared variable behind them (hardcoded native host
        // fields such as TON's IN/PT/Q/ET) and for ArrayElementCell.
        //
        // Re-clamps the current value because an object initializer assigns
        // Value first: `new Cell { Value = x, DeclaredTypeName = t }` would
        // otherwise seat an over-long initial value under a capacity it
        // violates.
        public string DeclaredTypeName
        {
            get => _declaredTypeName;
            set
            {
                _declaredTypeName = value;
                _stringCapacity = StringTypeInfo.TryParseLength(value, out var length) ? length : Unbounded;
                _value = ClampToDeclaredCapacity(_value);
            }
        }

        // A STRING(n)/WSTRING(n) declaration reserves exactly n characters, and
        // TwinCAT drops what a longer assignment does not fit rather than
        // growing the variable. The byte model has always truncated there
        // (Engine.ByteLayout's PackValue), so without this the same variable
        // reads one length in the interpreter and another after an ADR() round
        // trip.
        //
        // Capacity is read off the declared type text, so a STRING reached
        // through an alias (TYPE MyLabel : STRING(4)) or sized by a constant
        // expression keeps its whole value, as every string did before: the
        // Cell has no type registry and no expression evaluator to resolve
        // either with.
        private object ClampToDeclaredCapacity(object value) =>
            _stringCapacity >= 0 && value is string text && text.Length > _stringCapacity
                ? text.Substring(0, _stringCapacity)
                : value;
    }
}
