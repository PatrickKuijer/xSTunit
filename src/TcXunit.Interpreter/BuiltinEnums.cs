using System.Collections.Generic;

namespace TcXunit.Interpreter
{
    // Beckhoff library enums with no source in the solution (e.g.
    // TcEventSeverity from Tc3_EventLogger) - registered here so
    // Type.Member expressions resolve without needing a full DUT/library
    // definition (TcXunit-f6b / TcXunit-qit).
    internal static class BuiltinEnums
    {
        public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Types =
            new Dictionary<string, IReadOnlyDictionary<string, int>>
            {
                ["TcEventSeverity"] = new Dictionary<string, int>
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
