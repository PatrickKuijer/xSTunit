using System;
using System.Collections.Generic;

namespace xStunit.Interpreter
{
    // Types a vendor library declares as an alias of an IEC elementary type,
    // so a VAR or member naming one resolves without a DUT of ours declaring
    // it - the alias counterpart to BuiltinEnums.
    internal static class BuiltinAliases
    {
        // An object-type-class id is 32 bits on every target: both golden .tmc
        // modules declare PlcAppSystemInfo.ObjId at that width, the x86 build
        // and the x64 one alike, so it can be sized without knowing which
        // target the code is built for.
        //
        // Nothing address-shaped belongs in here. A void pointer is 32 bits on
        // x86 and 64 on x64 - the same two files disagree about it - so any
        // entry for one would be right on one target and wrong on the other.
        private static readonly IReadOnlyDictionary<string, string> UnderlyingTypes =
            new Dictionary<string, string>(IecIdentifier.Comparer)
            {
                ["OTCID"] = "UDINT",
            };

        public static bool TryGetUnderlyingType(string typeName, out string underlyingType) =>
            UnderlyingTypes.TryGetValue(typeName, out underlyingType);
    }
}
