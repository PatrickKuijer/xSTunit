using System;

namespace xStunit.Interpreter
{
    // Parses the raw duration text captured after T#/TIME#/LTIME#. Units come
    // in fixed descending order d,h,m,s,ms (LTIME adds us,ns) and reordering
    // is illegal; a unit may exceed its own range only when it is the first
    // one present, so T#100s12ms is legal and T#1m68s is not. TIME resolves to
    // milliseconds, LTIME to nanoseconds - the same literal text means
    // different numbers under the two.
    internal static class TimeLiteral
    {
        private static readonly (string Unit, long Multiplier, int Max)[] TimeSegments =
        {
            ("d", 86_400_000L, int.MaxValue),
            ("h", 3_600_000L, 23),
            ("m", 60_000L, 59),
            ("s", 1_000L, 59),
            ("ms", 1L, 999),
        };

        private static readonly (string Unit, long Multiplier, int Max)[] LTimeSegments =
        {
            ("d", 86_400_000_000_000L, int.MaxValue),
            ("h", 3_600_000_000_000L, 23),
            ("m", 60_000_000_000L, 59),
            ("s", 1_000_000_000L, 59),
            ("ms", 1_000_000L, 999),
            ("us", 1_000L, 999),
            ("ns", 1L, 999),
        };

        public static uint ParseTimeMs(string text) => (uint)ParseSegments(text, TimeSegments);

        public static ulong ParseLTimeNs(string text) => (ulong)ParseSegments(text, LTimeSegments);

        private static long ParseSegments(string text, (string Unit, long Multiplier, int Max)[] segments)
        {
            var pos = 0;
            var total = 0L;
            var lastSegmentIndex = -1;

            while (pos < text.Length)
            {
                var numStart = pos;
                while (pos < text.Length && char.IsDigit(text[pos]))
                    pos++;
                if (pos == numStart)
                    throw new ParseException($"Expected a number at position {pos} in TIME literal '{text}'", text);
                var number = long.Parse(text.Substring(numStart, pos - numStart));

                var unitStart = pos;
                while (pos < text.Length && char.IsLetter(text[pos]))
                    pos++;
                var unit = text.Substring(unitStart, pos - unitStart);

                var segmentIndex = Array.FindIndex(
                    segments,
                    s => string.Equals(s.Unit, unit, StringComparison.OrdinalIgnoreCase));
                if (segmentIndex < 0 || segmentIndex <= lastSegmentIndex)
                    throw new ParseException($"Unexpected or out-of-order unit '{unit}' in TIME literal '{text}'", unit);

                // lastSegmentIndex < 0 means this is the first unit present,
                // which alone may carry the whole duration and so is not range
                // checked; every later unit must stay inside its own range.
                if (lastSegmentIndex >= 0 && number > segments[segmentIndex].Max)
                    throw new ParseException($"Value {number} overflows unit '{unit}' in TIME literal '{text}'", unit);

                total += number * segments[segmentIndex].Multiplier;
                lastSegmentIndex = segmentIndex;
            }

            if (lastSegmentIndex < 0)
                throw new ParseException($"TIME literal '{text}' has no duration segments", text);

            return total;
        }
    }
}
