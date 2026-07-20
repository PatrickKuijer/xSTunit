namespace TcXunit.Runner.TcUnitStub
{
    public sealed class AssertionFailure
    {
        public AssertionFailure(string message)
        {
            Message = message;
        }

        public string Message { get; }
    }
}
