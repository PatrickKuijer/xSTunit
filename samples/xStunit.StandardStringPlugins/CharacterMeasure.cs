using System;

namespace xStunit.StandardStringPlugins
{
    // How a string function counts and slices "characters".
    //
    // The narrow (STRING) and wide (WSTRING) halves of this plugin share one
    // body per function - see StringOperations - and the measure is the only
    // axis on which the two halves are allowed to differ. Every index in an
    // operation body is a character offset in the sense defined here, never a
    // raw .NET char index.
    public abstract class CharacterMeasure
    {
        // Both measures count UTF-16 code units today, so a non-BMP character
        // (a surrogate pair) counts as 2 under either, and the two fields are a
        // seam rather than a present difference.
        public static readonly CharacterMeasure Wide = new CodeUnitMeasure();

        // Kept distinct from Wide despite the identical implementation: moving
        // the narrow half to another unit of measure is then a change to this
        // one field, with no operation body and no registration touched.
        public static readonly CharacterMeasure Narrow = new CodeUnitMeasure();

        public abstract int Length(string value);

        // start is a 0-based character offset. Callers clamp first: start and
        // count are assumed to be in range.
        public abstract string Substring(string value, int start, int count);

        // 0-based character offset of the first occurrence, or -1 when sought
        // does not occur. sought is assumed non-empty.
        public abstract int IndexOf(string value, string sought);

        private sealed class CodeUnitMeasure : CharacterMeasure
        {
            public override int Length(string value) => value.Length;

            public override string Substring(string value, int start, int count) =>
                value.Substring(start, count);

            public override int IndexOf(string value, string sought) =>
                value.IndexOf(sought, StringComparison.Ordinal);
        }
    }
}
