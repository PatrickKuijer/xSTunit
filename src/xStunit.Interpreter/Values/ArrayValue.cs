using System.Collections.Generic;

namespace xStunit.Interpreter
{
    // ARRAY value. Elements is flat row-major storage for every dimension in
    // Dimensions, which carries the declared lo..hi bounds - IEC array
    // indices need not start at 0, so an index only becomes an Elements
    // offset after subtracting Lo.
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
