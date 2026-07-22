namespace TcXunit.Interpreter
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
    }
}
