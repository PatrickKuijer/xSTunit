using System;
using xStunit.Interpreter;

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
        // A WSTRING is UCS-2, so one UTF-16 code unit is one character exactly
        // as TwinCAT counts it, and a non-BMP character (a surrogate pair)
        // counts as 2 in both.
        public static readonly CharacterMeasure Wide = new CodeUnitMeasure();

        // A narrow STRING is Latin-1, where one byte is one code unit for
        // every character it can hold, so the arithmetic is the wide half's.
        // What differs is the range: text beyond Latin-1 is refused rather
        // than counted as if TwinCAT could store it.
        public static readonly CharacterMeasure Narrow = new Latin1Measure();

        public abstract int Length(string value);

        // start is a 0-based character offset. Callers clamp first: start and
        // count are assumed to be in range.
        public abstract string Substring(string value, int start, int count);

        // 0-based character offset of the first occurrence, or -1 when sought
        // does not occur. sought is assumed non-empty.
        public abstract int IndexOf(string value, string sought);

        // For a string operand an operation splices in whole rather than
        // slicing: the three methods above never see it, so nothing would
        // otherwise check it is representable in this measure's encoding.
        public abstract void RequireRepresentable(string value);

        private class CodeUnitMeasure : CharacterMeasure
        {
            public override int Length(string value) => value.Length;

            public override string Substring(string value, int start, int count) =>
                value.Substring(start, count);

            public override int IndexOf(string value, string sought) =>
                value.IndexOf(sought, StringComparison.Ordinal);

            public override void RequireRepresentable(string value)
            {
            }
        }

        // Every operand goes through NarrowStringByte, the interpreter's one
        // definition of a narrow byte, so a plugin function and the engine's
        // own s[n]/ADR() paths can never disagree about which characters a
        // narrow STRING holds.
        private sealed class Latin1Measure : CodeUnitMeasure
        {
            public override int Length(string value)
            {
                RequireRepresentable(value);
                return base.Length(value);
            }

            public override string Substring(string value, int start, int count)
            {
                RequireRepresentable(value);
                return base.Substring(value, start, count);
            }

            public override int IndexOf(string value, string sought)
            {
                RequireRepresentable(value);
                RequireRepresentable(sought);
                return base.IndexOf(value, sought);
            }

            public override void RequireRepresentable(string value) =>
                NarrowStringByte.RequireRepresentable(value);
        }
    }
}
