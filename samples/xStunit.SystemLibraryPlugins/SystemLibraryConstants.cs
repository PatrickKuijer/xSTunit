using System.Collections.Generic;
using xStunit.Interpreter.Extensibility;

namespace xStunit.SystemLibraryPlugins
{
    // The named values Tc2_System publishes from its GVLs and ENUMs.
    //
    // Supplying the library's blocks without these would only half-solve the
    // problem: real source never writes nMode := 33, it writes
    // FOPEN_MODEREAD OR FOPEN_MODETEXT, and an unresolved identifier skips the
    // POU just as an unresolved call does.
    //
    // HOW SURE ARE WE OF THESE NUMBERS
    //
    // FOPEN_MODEBINARY (16) and FOPEN_MODETEXT (32) are documented; the other
    // four are the same bit sequence continued downward, which is the only
    // arrangement consistent with those two and with the documented rule that
    // the read/write and binary/text pairs are each mutually exclusive.
    // E_SeekOrigin is assumed to follow C's SEEK_SET/SEEK_CUR/SEEK_END = 0/1/2,
    // which its own member names say it mirrors. E_OpenPath's ordering is the
    // order the documentation lists it in. None of the three has published
    // numeric values, and only the relative behavior - which mode bit means
    // what - is load-bearing here: the virtual filesystem reads the bits, so a
    // wrong absolute value would only matter to source that hard-codes one.
    public sealed class SystemLibraryConstants : IXstunitNativeConstants
    {
        public IReadOnlyList<NativeConstant> Constants => new[]
        {
            // FB_FileOpen's nMode. DWORD, so long in the value model.
            new NativeConstant("FOPEN_MODEREAD", 0x01L),
            new NativeConstant("FOPEN_MODEWRITE", 0x02L),
            new NativeConstant("FOPEN_MODEAPPEND", 0x04L),
            new NativeConstant("FOPEN_MODEPLUS", 0x08L),
            new NativeConstant("FOPEN_MODEBINARY", 0x10L),
            new NativeConstant("FOPEN_MODETEXT", 0x20L),

            // E_OpenPath, the TwinCAT system directory a relative path resolves
            // against. Accepted and ignored by the blocks - the virtual
            // filesystem has one flat namespace - but source names them, so
            // they have to resolve.
            new NativeConstant("PATH_GENERIC", 1, "E_OpenPath"),
            new NativeConstant("PATH_BOOTPATH", 2, "E_OpenPath"),
            new NativeConstant("PATH_BOOTPRJ", 3, "E_OpenPath"),
            new NativeConstant("PATH_BOOTDATA", 4, "E_OpenPath"),
            new NativeConstant("PATH_USERPATH1", 11, "E_OpenPath"),
            new NativeConstant("PATH_USERPATH2", 12, "E_OpenPath"),

            // E_SeekOrigin, FB_FileSeek's eOrigin.
            new NativeConstant("SEEK_SET", 0, "E_SeekOrigin"),
            new NativeConstant("SEEK_CUR", 1, "E_SeekOrigin"),
            new NativeConstant("SEEK_END", 2, "E_SeekOrigin"),

            // ADSLOG_MSGTYPE_*, the ADSLOG* control mask. Recorded verbatim by
            // the log sink, so a suite asserting on a severity compares the
            // same numbers the caller passed.
            new NativeConstant("ADSLOG_MSGTYPE_HINT", 0x01L),
            new NativeConstant("ADSLOG_MSGTYPE_WARN", 0x02L),
            new NativeConstant("ADSLOG_MSGTYPE_ERROR", 0x04L),
            new NativeConstant("ADSLOG_MSGTYPE_LOG", 0x10L),
            new NativeConstant("ADSLOG_MSGTYPE_MSGBOX", 0x20L),
            new NativeConstant("ADSLOG_MSGTYPE_RESOURCE", 0x40L),
            new NativeConstant("ADSLOG_MSGTYPE_STRING", 0x80L),

            // The tTimeout/TMOUT default every file and ADS block declares.
            // TIME, which the interpreter carries as milliseconds.
            new NativeConstant("DEFAULT_ADS_TIMEOUT", 5000u),
        };
    }
}
