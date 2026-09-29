using System;
using System.Collections.Generic;

namespace xStunit.Interpreter
{
    // Enums that live in vendor libraries rather than in any file we parse, so
    // a Type.Member expression naming one still resolves without a DUT for it.
    internal static class BuiltinEnums
    {
        public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Types =
            new Dictionary<string, IReadOnlyDictionary<string, int>>(IecIdentifier.Comparer)
            {
                ["TcEventSeverity"] = new Dictionary<string, int>(IecIdentifier.Comparer)
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
