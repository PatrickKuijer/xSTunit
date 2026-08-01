using xStunit.Runner;

namespace xStunit.Interpreter
{
    // The one definition of "a byte of a narrow STRING", shared by the engine's
    // s[n] accessors, the MEMCPY byte layout, and the narrow CharacterMeasure
    // in samples/xStunit.StandardStringPlugins - so the interpreter and the
    // plugins standing in for Tc2_Standard cannot drift apart on what a byte is.
    //
    // TwinCAT encodes a narrow STRING as Latin-1 (ISO/IEC 8859-1) by default:
    // one byte per character, and that byte's value IS the UTF-16 code unit,
    // which is why a CLR string holds one losslessly and why counting code
    // units already counts bytes correctly.
    //
    // Above U+00FF there is no Latin-1 byte to hold the character. TwinCAT can
    // only store one under the per-variable {attribute 'TcEncoding':='UTF-8'}
    // pragma, which this interpreter does not model - the lexer skips pragmas
    // and nothing parses TcEncoding - so such a character raises instead of
    // losing its high bits. A silently truncated low byte is a wrong answer
    // that looks like a right one.
    public static class NarrowStringByte
    {
        private const char MaxChar = 'ÿ';

        public static byte FromChar(char ch)
        {
            if (ch > MaxChar)
                throw new UnsupportedConstructException(
                    "STRING",
                    $"Character '{ch}' (U+{(int)ch:X4}) is outside Latin-1, the encoding TwinCAT gives a " +
                    "narrow STRING by default - one byte per character, U+0000..U+00FF. Per-variable " +
                    "{attribute 'TcEncoding':='UTF-8'} is not modeled here; use a WSTRING for text beyond Latin-1.");

            return (byte)ch;
        }

        // Masked to the low byte because every caller already has one: a
        // BYTE-typed write into s[n] and a byte lifted out of a packed buffer
        // are both 0..255, so nothing a caller meant to keep is discarded.
        public static char ToChar(int byteValue) => (char)(byteValue & 0xFF);

        public static void RequireRepresentable(string text)
        {
            if (text == null)
                return;

            for (var i = 0; i < text.Length; i++)
                FromChar(text[i]);
        }
    }
}
