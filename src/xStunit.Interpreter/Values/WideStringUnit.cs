using xStunit.Runner;

namespace xStunit.Interpreter
{
    // The wide counterpart to NarrowStringByte, for the WSTRING side of the
    // byte model.
    //
    // TwinCAT encodes a WSTRING as UCS-2: exactly two bytes per character, with
    // no surrogate mechanism, so the representable set is the Basic
    // Multilingual Plane. A CLR string is UTF-16, which is the same encoding
    // for every BMP character and differs only above U+FFFF, where UTF-16
    // spends a surrogate PAIR - four bytes - on one character.
    //
    // Packing such a pair would put two units on the wire where TwinCAT holds
    // one, so every character after it lands at the wrong offset and the
    // terminator falls outside the declared capacity. It raises instead, for
    // the same reason the narrow path raises above Latin-1: a silently mangled
    // buffer is a wrong answer that looks like a right one.
    internal static class WideStringUnit
    {
        public static void RequireRepresentable(string text)
        {
            if (text == null)
                return;

            for (var i = 0; i < text.Length; i++)
            {
                if (!char.IsSurrogate(text[i]))
                    continue;

                var codePoint = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])
                    ? char.ConvertToUtf32(text[i], text[i + 1])
                    : text[i];

                throw new UnsupportedConstructException(
                    "WSTRING",
                    $"Character U+{codePoint:X4} is outside the Basic Multilingual Plane, which is all a " +
                    "WSTRING can hold: TwinCAT encodes one as UCS-2, two bytes per character with no " +
                    "surrogates. Nothing in IEC 61131-3 stores this character.");
            }
        }
    }
}
