namespace xStunit.Runner.Tests.Fakes
{
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
