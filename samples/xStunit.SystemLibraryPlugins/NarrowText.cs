using xStunit.Interpreter;

namespace xStunit.SystemLibraryPlugins
{
    // Bytes <-> text for the buffers this library's blocks move around, in one
    // place so the virtual filesystem and the loopback ADS device cannot drift
    // apart on what a byte of a STRING is.
    //
    // Delegates to the interpreter's own NarrowStringByte rather than casting,
    // so a file written here and read back through s[n], MEMCPY or F_ToASC all
    // agree: TwinCAT gives a narrow STRING Latin-1, where the byte value IS the
    // character, and text with no one-byte encoding raises instead of silently
    // losing its high bits.
    internal static class NarrowText
    {
        public static string FromBytes(byte[] bytes)
        {
            var text = new char[bytes.Length];
            for (var i = 0; i < bytes.Length; i++)
                text[i] = NarrowStringByte.ToChar(bytes[i]);

            return new string(text);
        }

        public static byte[] ToBytes(string text)
        {
            var bytes = new byte[text.Length];
            for (var i = 0; i < text.Length; i++)
                bytes[i] = NarrowStringByte.FromChar(text[i]);

            return bytes;
        }
    }
}
