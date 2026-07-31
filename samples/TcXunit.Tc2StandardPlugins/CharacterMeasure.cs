using System;

namespace TcXunit.Tc2StandardPlugins
{
    // How a string function counts and slices "characters" (TcXunit-p4qb).
    //
    // The narrow (STRING) and wide (WSTRING) halves of this plugin share one
    // body per function - see StringOperations - and this is the only axis on
    // which the two halves are allowed to differ. Every index in an operation
    // body is a character offset in the sense defined here, never a raw .NET
    // char index.
    //
    // Both measures resolve to the same UTF-16 code-unit implementation today,
    // so this is a seam rather than a difference. It is a seam that has to
    // exist: see Narrow below.
    public abstract class CharacterMeasure
    {
        // WSTRING holds UTF-16 code units in TwinCAT, and so does a .NET
        // string, so this measure is exact for the W-prefixed functions. A
        // non-BMP character (a surrogate pair) counts as 2 in both, which is
        // what the PLC does; counting Unicode scalar values instead would make
        // the wide functions disagree with the runtime they stand in for.
        public static readonly CharacterMeasure Wide = new CodeUnitMeasure();

        // TwinCAT's narrow STRING is byte-counted, so a code-unit measure is
        // only an approximation here: correct for ASCII, wrong for anything
        // above U+007F. Fixing that (TcXunit-ielv) means pointing this one
        // field at a byte-oriented measure; no operation body and no function
        // registration should have to change.
        public static readonly CharacterMeasure Narrow = new CodeUnitMeasure();

        // Number of characters in value.
        public abstract int Length(string value);

        // The count characters of value starting at the 0-based character
        // offset start. Callers clamp first: start and count are assumed to
        // be in range.
        public abstract string Substring(string value, int start, int count);

        // 0-based character offset of the first occurrence of sought within
        // value, or -1 when it does not occur. sought is assumed non-empty.
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
