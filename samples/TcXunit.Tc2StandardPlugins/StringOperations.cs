using System;
using xStunit.Interpreter.Extensibility;

namespace TcXunit.Tc2StandardPlugins
{
    // The Tc2_Standard string-function bodies, written once and shared by the
    // narrow (STRING) and wide (WSTRING) registrations in StringFunction.cs
    // (TcXunit-8po, TcXunit-93l9, collapsed by TcXunit-p4qb).
    //
    // Every body takes its character arithmetic from the CharacterMeasure it
    // is handed rather than from string.Length/Substring directly, so the one
    // real narrow/wide asymmetry - TwinCAT counts STRING in bytes and WSTRING
    // in UTF-16 code units - stays expressible in the measure instead of
    // having to be re-forked back out into per-half copies.
    //
    // Out-of-range position/size arguments clamp rather than throw, matching
    // each function's acceptance criteria (TcXunit-8po.2 through
    // TcXunit-8po.9).
    internal static class StringOperations
    {
        // DELETE(STR, LEN, POS): removes LEN characters from STR starting at
        // the 1-based POS, clamped when POS+LEN exceeds STR's length. POS
        // outside [1, STR's length], or a non-positive LEN, leaves STR
        // unchanged.
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

        // FIND(STR1, STR2): 1-based position of the first occurrence of STR2
        // within STR1, or 0 if STR2 is empty or not found.
        public static object Find(NativeCallContext context, CharacterMeasure measure)
        {
            var str1 = context.RequireString("STR1", 0);
            var str2 = context.RequireString("STR2", 1);

            if (string.IsNullOrEmpty(str2))
                return 0;

            var index = measure.IndexOf(str1, str2);
            return index < 0 ? 0 : index + 1;
        }

        // INSERT(STR1, STR2, POS): inserts STR2 into STR1 immediately after
        // the 1-based POS. POS is clamped to [0, STR1's length]; POS = 0
        // inserts before the first character.
        public static object Insert(NativeCallContext context, CharacterMeasure measure)
        {
            var str1 = context.RequireString("STR1", 0);
            var str2 = context.RequireString("STR2", 1);
            var pos = context.RequireInt32("POS", 2);

            var length = measure.Length(str1);
            var at = Math.Min(Math.Max(pos, 0), length);
            return measure.Substring(str1, 0, at) + str2 +
                   measure.Substring(str1, at, length - at);
        }

        // LEFT(STR, SIZE): leftmost SIZE characters of STR, clamped to
        // [0, STR's length].
        public static object Left(NativeCallContext context, CharacterMeasure measure)
        {
            var str = context.RequireString("STR", 0);
            var size = context.RequireInt32("SIZE", 1);

            var take = Math.Min(Math.Max(size, 0), measure.Length(str));
            return measure.Substring(str, 0, take);
        }

        // LEN(STR): character count of STR.
        public static object Len(NativeCallContext context, CharacterMeasure measure)
        {
            var str = context.RequireString("STR", 0);
            return measure.Length(str);
        }

        // MID(STR, LEN, POS): LEN characters of STR starting at the 1-based
        // POS, clamped when POS+LEN exceeds STR's length. POS outside
        // [1, STR's length], or a non-positive LEN, yields an empty string.
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

        // REPLACE(STR1, STR2, L, P): replaces L characters in STR1 starting at
        // the 1-based P with STR2, clamped when P+L exceeds STR1's length. P
        // outside [1, STR1's length] leaves STR1 unchanged, apart from
        // whatever a non-positive L would already make a no-op.
        public static object Replace(NativeCallContext context, CharacterMeasure measure)
        {
            var str1 = context.RequireString("STR1", 0);
            var str2 = context.RequireString("STR2", 1);
            var l = context.RequireInt32("L", 2);
            var p = context.RequireInt32("P", 3);

            var length = measure.Length(str1);
            if (p < 1 || p > length)
                return str1;

            var start = p - 1;
            var remove = Math.Min(Math.Max(l, 0), length - start);
            return measure.Substring(str1, 0, start) + str2 +
                   measure.Substring(str1, start + remove, length - start - remove);
        }

        // RIGHT(STR, SIZE): rightmost SIZE characters of STR, clamped to
        // [0, STR's length].
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
