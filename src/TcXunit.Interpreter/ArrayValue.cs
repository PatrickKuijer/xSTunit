using System.Collections.Generic;

namespace TcXunit.Interpreter
{
    // ARRAY value: flattened row-major element storage plus the declared
    // lo..hi bounds per dimension (multi-dim support - TcXunit-w5x.15.6).
    public sealed class ArrayValue
    {
        public IReadOnlyList<(int Lo, int Hi)> Dimensions { get; }
        public string ElementTypeName { get; }
        public object[] Elements { get; }

        public ArrayValue(IReadOnlyList<(int Lo, int Hi)> dimensions, string elementTypeName, object[] elements)
        {
            Dimensions = dimensions;
            ElementTypeName = elementTypeName;
            Elements = elements;
        }
    }
}
