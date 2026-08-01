using System;
using xStunit.Interpreter.Extensibility;

namespace xStunit.StandardStringPlugins
{
    // The Tc2_Standard string-function bodies, written once and shared by the
    // narrow (STRING) and wide (WSTRING) registrations in StringFunction.cs.
    //
    // Every body takes its character arithmetic from the CharacterMeasure it
    // is handed, never from string.Length/Substring directly, so a narrow/wide
    // asymmetry in how characters are counted stays expressible in the measure
    // instead of forcing these bodies back out into per-half copies.
    //
    // Position arguments are 1-based, as in IEC, and out-of-range positions
    // and sizes clamp rather than throw.
    internal static class StringOperations
    {
        public static object Delete(NativeCallContext context, CharacterMeasure measure)
        {
            var str = context.RequireString("STR", 0);
            var len = context.RequireInt32("LEN", 1);
            var pos = context.RequireInt32("POS", 2);

            var length = measure.Length(str);
            if (pos < 1 || pos > length || len <= 0)
                return str;

            var start = pos - 1;
            var remove = Math.Min(len, length - start);
            return measure.Substring(str, 0, start) +
                   measure.Substring(str, start + remove, length - start - remove);
        }

        public static object Find(NativeCallContext context, CharacterMeasure measure)
        {
            var str1 = context.RequireString("STR1", 0);
            var str2 = context.RequireString("STR2", 1);

            if (string.IsNullOrEmpty(str2))
                return 0;

            var index = measure.IndexOf(str1, str2);
            return index < 0 ? 0 : index + 1;
        }

        // INSERT puts STR2 after the 1-based POS, so unlike its neighbours it
        // uses POS unshifted rather than POS - 1, and POS = 0 (insert before
        // the first character) is in range rather than an under-run.
        public static object Insert(NativeCallContext context, CharacterMeasure measure)
        {
            var str1 = context.RequireString("STR1", 0);
            var str2 = context.RequireString("STR2", 1);
            var pos = context.RequireInt32("POS", 2);

            measure.Validate(str2);
            var length = measure.Length(str1);
            var at = Math.Min(Math.Max(pos, 0), length);
            return measure.Substring(str1, 0, at) + str2 +
                   measure.Substring(str1, at, length - at);
        }

        public static object Left(NativeCallContext context, CharacterMeasure measure)
        {
            var str = context.RequireString("STR", 0);
            var size = context.RequireInt32("SIZE", 1);

            var take = Math.Min(Math.Max(size, 0), measure.Length(str));
            return measure.Substring(str, 0, take);
        }

        public static object Len(NativeCallContext context, CharacterMeasure measure)
        {
            var str = context.RequireString("STR", 0);
            return measure.Length(str);
        }

        public static object Mid(NativeCallContext context, CharacterMeasure measure)
        {
            var str = context.RequireString("STR", 0);
            var len = context.RequireInt32("LEN", 1);
            var pos = context.RequireInt32("POS", 2);

            var length = measure.Length(str);
            if (pos < 1 || pos > length || len <= 0)
                return string.Empty;

            var start = pos - 1;
            var take = Math.Min(len, length - start);
            return measure.Substring(str, start, take);
        }

        public static object Replace(NativeCallContext context, CharacterMeasure measure)
        {
            var str1 = context.RequireString("STR1", 0);
            var str2 = context.RequireString("STR2", 1);
            var l = context.RequireInt32("L", 2);
            var p = context.RequireInt32("P", 3);

            measure.Validate(str2);
            var length = measure.Length(str1);
            if (p < 1 || p > length)
                return str1;

            var start = p - 1;
            var remove = Math.Min(Math.Max(l, 0), length - start);
            return measure.Substring(str1, 0, start) + str2 +
                   measure.Substring(str1, start + remove, length - start - remove);
        }

        public static object Right(NativeCallContext context, CharacterMeasure measure)
        {
            var str = context.RequireString("STR", 0);
            var size = context.RequireInt32("SIZE", 1);

            var length = measure.Length(str);
            var take = Math.Min(Math.Max(size, 0), length);
            return measure.Substring(str, length - take, take);
        }
    }
}
