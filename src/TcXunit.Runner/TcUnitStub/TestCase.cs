using System;

namespace TcXunit.Runner.TcUnitStub
{
    public sealed class TestCase
    {
        public TestCase(string name, Action body)
        {
            Name = name;
            Body = body;
        }

        public string Name { get; }
        public Action Body { get; }
    }
}
