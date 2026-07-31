namespace xStunit.Interpreter
{
    public sealed class Pointer
    {
        public Cell Target { get; }

        public Pointer(Cell target)
        {
            Target = target;
        }
    }
}
