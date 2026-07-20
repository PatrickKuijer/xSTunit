namespace TcXunit.Runner.Tests.Fakes
{
    /// <summary>
    /// Stand-in for a POU under test. A real run would come from the interpreter
    /// executing a parsed .TcPOU AST (see TcXunit-w5x.6) instead of hand-written C#.
    /// </summary>
    internal sealed class Counter
    {
        public int Value { get; private set; }

        public void Increment() => Value++;

        public void Decrement()
        {
            if (Value > 0)
                Value--;
        }
    }
}
