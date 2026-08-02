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

        // Carried on the array rather than resolved per element, because an
        // ArrayElementCell is minted fresh on every index and every pointer
        // step - by callers with no type registry to resolve
        // ARRAY[..] OF T_Label with. Unbounded for a non-STRING element type.
        public int ElementStringCapacity { get; }

        // elementStringCapacity has no default on purpose: an ARRAY built
        // without one silently stops truncating its elements, and every
        // construction site should have to say which it is.
        public ArrayValue(
            IReadOnlyList<(int Lo, int Hi)> dimensions,
            string elementTypeName,
            object[] elements,
            int elementStringCapacity)
        {
            Dimensions = dimensions;
            ElementTypeName = elementTypeName;
            Elements = elements;
            ElementStringCapacity = elementStringCapacity;
        }

        // The write half of Elements, for every assignment that means "this
        // element now holds that value" - element assignment, REF= into an
        // element, an array literal overlaying its defaults. Writes straight
        // into Elements stay legitimate where the value is already known to fit
        // (the byte model unpacking a buffer it just packed).
        public void SetElement(int index, object value) =>
            Elements[index] = Cell.ClampToCapacity(value, ElementStringCapacity);
    }
}
