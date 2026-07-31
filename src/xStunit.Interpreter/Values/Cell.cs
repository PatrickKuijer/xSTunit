namespace xStunit.Interpreter
{
    // Mutable storage slot. Plain variables own one; POINTER TO holds a Cell
    // reference as its value (see Pointer); REFERENCE TO aliases another
    // variable's Cell directly instead of copying its value (see Frame.Ref).
    // Value is virtual so ArrayElementCell can proxy a slot inside an
    // ArrayValue.Elements array instead of owning its own storage
    // (TcXunit-sej.2 - pointer arithmetic into array elements).
    public class Cell
    {
        public virtual object Value { get; set; }

        // The IEC type text (e.g. "INT", "REFERENCE TO INT", "POINTER TO
        // BYTE") this Cell was declared with, when known - set alongside
        // Value at every VarDecl-driven Cell construction (instance fields,
        // method locals, struct fields, GVL members). Used by __ISVALIDREF
        // (TcXunit-6lh) to reject misuse on non-reference/pointer variables,
        // since every other type's DefaultValue is non-null and would
        // otherwise make the check meaningless. Null for Cells not backed by
        // a declared variable (e.g. hardcoded native host fields like TON's
        // IN/PT/Q/ET) or for ArrayElementCell, which proxies into an
        // ArrayValue instead of owning declared-type metadata of its own.
        public string DeclaredTypeName { get; set; }
    }
}
