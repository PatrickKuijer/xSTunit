using System;

namespace xStunit.Interpreter
{
    // The single POINTER TO / REFERENCE TO prefix test. The two spellings are
    // one predicate because every caller treats them alike: both hold the
    // address of another variable, so both are pointer-width to TypeLayout,
    // both start unbound (null) rather than at a zero value, and both are what
    // __ISVALIDREF is allowed to ask about.
    internal static class AddressTypeInfo
    {
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

            var trimmed = typeName.TrimStart();
            return trimmed.StartsWith("POINTER TO", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("REFERENCE TO", StringComparison.OrdinalIgnoreCase);
        }
    }
}
