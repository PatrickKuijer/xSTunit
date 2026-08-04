using System;

namespace xStunit.Interpreter
{
    // The single test for a declaration that holds an address. The spellings
    // are one predicate because every caller treats them alike: each holds the
    // address of something else, so each is address-width to TypeLayout, each
    // starts unbound (null) rather than at a zero value, and each is what
    // __ISVALIDREF is allowed to ask about.
    //
    // PVOID is in here rather than in BuiltinAliases because it is an address
    // and not an alias of any elementary type: it says nothing about what it
    // points at, but it is exactly as wide as the target's other addresses,
    // which is why TwinCAT declares it 32 bits on x86 and 64 on x64.
    internal static class AddressTypeInfo
    {
        private const string VoidPointer = "PVOID";

        // IEC 61131-3 type names are case-insensitive ('pointer to BYTE' is as
        // valid as 'POINTER TO BYTE'), hence OrdinalIgnoreCase - the same
        // decision as IecNumericType, IecElementaryDefault, StringTypeInfo,
        // ArrayTypeInfo and TypeRegistry. The alternative, an ordinal compare,
        // would make the answer depend on how the declaring DUT happened to be
        // typed; the culture-sensitive default overload is worse still, since
        // it would also make it depend on the machine's current culture.
        public static bool IsAddressType(string typeName)
        {
            if (typeName == null)
                return false;

            var trimmed = typeName.Trim();
            return trimmed.StartsWith("POINTER TO", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("REFERENCE TO", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, VoidPointer, StringComparison.OrdinalIgnoreCase);
        }
    }
}
