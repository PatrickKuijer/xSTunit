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
        public virtual object Value { get; set; }

        // The IEC type text (e.g. "INT", "REFERENCE TO INT", "POINTER TO
        // BYTE") this Cell was declared with, set alongside Value at every
        // VarDecl-driven construction. __ISVALIDREF needs it to reject
        // non-reference/pointer variables, since every other type's default
        // value is non-null and would make the check meaningless. Null for
        // Cells with no declared variable behind them (hardcoded native host
        // fields such as TON's IN/PT/Q/ET) and for ArrayElementCell.
        public string DeclaredTypeName { get; set; }
    }
}
