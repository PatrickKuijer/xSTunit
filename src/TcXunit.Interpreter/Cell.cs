namespace TcXunit.Interpreter
{
    // Mutable storage slot. Plain variables own one; POINTER TO holds a Cell
    // reference as its value (see Pointer); REFERENCE TO aliases another
    // variable's Cell directly instead of copying its value (see Frame.Ref).
    public sealed class Cell
    {
        public object Value { get; set; }
    }
}
