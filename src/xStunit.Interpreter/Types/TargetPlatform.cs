using System;

namespace xStunit.Interpreter
{
    // The machine the PLC code under test is compiled for, reduced to the one
    // thing the layout rules cannot answer without it: how many bytes an
    // address occupies. Scalar widths, alignment and string encoding are the
    // same on every target TwinCAT builds for, so this is deliberately not a
    // catalogue of machine properties - if a second rule ever turns out to
    // differ, it belongs here rather than in a second target concept.
    public sealed class TargetPlatform
    {
        public static readonly TargetPlatform X86 = new TargetPlatform("x86", addressSize: 4);

        public static readonly TargetPlatform X64 = new TargetPlatform("x64", addressSize: 8);

        // What a run computes at when nothing selected a target. x86 is the
        // width every SIZEOF answer xStunit has ever given was computed at, so
        // defaulting to it changes no result that was already right.
        public static TargetPlatform Default => X86;

        private TargetPlatform(string name, int addressSize)
        {
            Name = name;
            AddressSize = addressSize;
        }

        public string Name { get; }

        // Bytes in a POINTER TO / REFERENCE TO / PVOID, which is also the
        // alignment one of them imposes on the type holding it.
        public int AddressSize { get; }

        // The spellings a user selects a target by. An unrecognised one is
        // false rather than the default, so a typo is a usage error instead of
        // a run that silently computes at the wrong width.
        public static bool TryParse(string text, out TargetPlatform platform)
        {
            if (string.Equals(text, X64.Name, StringComparison.OrdinalIgnoreCase))
            {
                platform = X64;
                return true;
            }

            if (string.Equals(text, X86.Name, StringComparison.OrdinalIgnoreCase))
            {
                platform = X86;
                return true;
            }

            platform = null;
            return false;
        }

        // The target a compiler-emitted module names in its own header, e.g.
        // "TwinCAT RT (x64)". Anything not naming x64 reads as x86: the file
        // was written by the compiler that produced the layout being compared,
        // so an unrecognised string is a 32-bit build rather than an unknown
        // machine, and the comparison is better run at a width than refused.
        public static TargetPlatform FromModuleTarget(string moduleTarget) =>
            moduleTarget != null && moduleTarget.IndexOf(X64.Name, StringComparison.OrdinalIgnoreCase) >= 0
                ? X64
                : X86;

        public override string ToString() => Name;
    }
}
