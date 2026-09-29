using System;
using System.Collections.Generic;

namespace xStunit.Interpreter
{
    // Enums that live in vendor libraries rather than in any file we parse, so
    // a Type.Member expression naming one still resolves without a DUT for it.
    // Both the type and its members match in any case, as IEC identifiers do.
    internal static class BuiltinEnums
    {
        public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Types =
            new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.OrdinalIgnoreCase)
            {
                ["TcEventSeverity"] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Verbose"] = 1,
                    ["Info"] = 2,
                    ["Warning"] = 3,
                    ["Error"] = 4,
                    ["Critical"] = 5,
                },
            };
    }
}
