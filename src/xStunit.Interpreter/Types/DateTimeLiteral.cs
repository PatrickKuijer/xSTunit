using System;

namespace xStunit.Interpreter
{
    // Parses the raw text captured after D#/DATE#, DT#/DATE_AND_TIME#, and
    // TOD#/TIME_OF_DAY# literals (TcXunit-gd2.13). Unlike TIME/LTIME
    // (TimeLiteral.cs), these are calendar/clock literals with a fixed
    // YYYY-MM-DD / HH:MM:SS[.mmm] shape rather than a reorderable duration-
    // segment grammar, so Lexer.cs scans the literal body as a run of
    // digits/'-'/':'/'.' instead of reusing TIME's digit+unit-letter loop,
    // and parsing here is a straightforward positional scan rather than
    // TimeLiteral's segment table.
    internal static class DateTimeLiteral
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // DATE: whole calendar days since the 1970-01-01 UTC epoch,
        // represented in seconds like DATE_AND_TIME - D#2024-01-01.
        public static uint ParseDateSeconds(string text)
        {
            var pos = 0;
            var (year, month, day) = ReadDate(text, ref pos);
            if (pos != text.Length)
                throw new ParseException($"Unexpected trailing text '{text.Substring(pos)}' in DATE literal '{text}'");

            return ToEpochSeconds(year, month, day, 0, 0, 0, text);
        }

        // DATE_AND_TIME: seconds since the 1970-01-01 UTC epoch - no
        // fractional-second component (unlike TIME_OF_DAY), matching the
        // IEC 61131-3 DT grammar - DT#2024-01-01-10:00:00.
        public static uint ParseDateAndTimeSeconds(string text)
        {
            var pos = 0;
            var (year, month, day) = ReadDate(text, ref pos);
            Expect(text, ref pos, '-', text);
            var (hour, minute, second, fractionMs) = ReadTime(text, ref pos);
            if (fractionMs != null)
                throw new ParseException($"DATE_AND_TIME literal '{text}' doesn't support fractional seconds");
            if (pos != text.Length)
                throw new ParseException($"Unexpected trailing text '{text.Substring(pos)}' in DATE_AND_TIME literal '{text}'");

            return ToEpochSeconds(year, month, day, hour, minute, second, text);
        }

        // TIME_OF_DAY: milliseconds since midnight, same unit/range as TIME
        // - TOD#10:00:00 or TOD#10:00:00.500.
        public static uint ParseTimeOfDayMs(string text)
        {
            var pos = 0;
            var (hour, minute, second, fractionMs) = ReadTime(text, ref pos);
            if (pos != text.Length)
                throw new ParseException($"Unexpected trailing text '{text.Substring(pos)}' in TIME_OF_DAY literal '{text}'");

            return (uint)(((hour * 60 + minute) * 60 + second) * 1000 + (fractionMs ?? 0));
        }

        private static (int Year, int Month, int Day) ReadDate(string text, ref int pos)
        {
            var year = ReadDigits(text, ref pos, 4, "year", text);
            Expect(text, ref pos, '-', text);
            var month = ReadDigits(text, ref pos, 2, "month", text);
            Expect(text, ref pos, '-', text);
            var day = ReadDigits(text, ref pos, 2, "day", text);

            if (month < 1 || month > 12)
                throw new ParseException($"Month {month} out of range in DATE literal '{text}'");
            if (day < 1 || day > DateTime.DaysInMonth(year, month))
                throw new ParseException($"Day {day} out of range in DATE literal '{text}'");

            return (year, month, day);
        }

        private static (int Hour, int Minute, int Second, int? FractionMs) ReadTime(string text, ref int pos)
        {
            var hour = ReadDigits(text, ref pos, 2, "hour", text);
            Expect(text, ref pos, ':', text);
            var minute = ReadDigits(text, ref pos, 2, "minute", text);
            Expect(text, ref pos, ':', text);
            var second = ReadDigits(text, ref pos, 2, "second", text);

            if (hour > 23)
                throw new ParseException($"Hour {hour} out of range in literal '{text}'");
            if (minute > 59)
                throw new ParseException($"Minute {minute} out of range in literal '{text}'");
            if (second > 59)
                throw new ParseException($"Second {second} out of range in literal '{text}'");

            int? fractionMs = null;
            if (pos < text.Length && text[pos] == '.')
            {
                pos++;
                var fracStart = pos;
                while (pos < text.Length && char.IsDigit(text[pos]))
                    pos++;
                if (pos == fracStart)
                    throw new ParseException($"Expected digits after '.' in literal '{text}'");

                // Normalize to milliseconds regardless of how many fractional
                // digits were given (TOD#10:00:00.5 == 500ms, matching TIME's
                // ms-granularity representation).
                var fracText = text.Substring(fracStart, pos - fracStart);
                fracText = fracText.Length >= 3 ? fracText.Substring(0, 3) : fracText.PadRight(3, '0');
                fractionMs = int.Parse(fracText);
            }

            return (hour, minute, second, fractionMs);
        }

        private static int ReadDigits(string text, ref int pos, int count, string fieldName, string literalText)
        {
            if (pos + count > text.Length)
                throw new ParseException($"Expected {count}-digit {fieldName} at position {pos} in literal '{literalText}'");
            for (var j = 0; j < count; j++)
            {
                if (!char.IsDigit(text[pos + j]))
                    throw new ParseException($"Expected {count}-digit {fieldName} at position {pos} in literal '{literalText}'");
            }

            var value = int.Parse(text.Substring(pos, count));
            pos += count;
            return value;
        }

        private static void Expect(string text, ref int pos, char expected, string literalText)
        {
            if (pos >= text.Length || text[pos] != expected)
                throw new ParseException($"Expected '{expected}' at position {pos} in literal '{literalText}'");
            pos++;
        }

        private static uint ToEpochSeconds(int year, int month, int day, int hour, int minute, int second, string literalText)
        {
            var dt = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
            var totalSeconds = (dt - Epoch).TotalSeconds;
            if (totalSeconds < 0 || totalSeconds > uint.MaxValue)
                throw new ParseException($"Literal '{literalText}' is out of the representable DATE/DATE_AND_TIME range (1970-01-01 to 2106)");

            return (uint)totalSeconds;
        }
    }
}
