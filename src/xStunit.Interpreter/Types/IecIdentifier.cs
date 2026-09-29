using System;
using System.Collections.Generic;

namespace xStunit.Interpreter
{
    // IEC 61131-3 identifiers - variable, field, POU, method, type and enum
    // member names, elementary type names included - are case-insensitive.
    // Name-keyed tables and name comparisons go through here so the rule has
    // one home: a table built with the default ordinal comparer instead
    // regresses silently, on whichever spelling the fixtures happen not to use.
    //
    // Not routed here: keywords, which the lexer and declaration readers fold
    // themselves, and type TEXT (POINTER TO x, ARRAY [..] OF x, STRING(n)),
    // which the *TypeInfo readers match by prefix.
    internal static class IecIdentifier
    {
        public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

        public static bool Matches(string a, string b) => Comparer.Equals(a, b);

        // The declared spelling that name matches, so a dispatch switch can
        // name each case once whatever case the call site used; null when it
        // matches none of them.
        public static string Canonical(IEnumerable<string> declared, string name)
        {
            if (name == null)
                return null;

            foreach (var candidate in declared)
                if (Matches(candidate, name))
                    return candidate;

            return null;
        }

        // Re-keys rather than adopts, so lookup is case-insensitive whatever
        // comparer the source was built with; of two keys differing only in
        // case, the later one wins.
        public static Dictionary<string, T> CopyOf<T>(IEnumerable<KeyValuePair<string, T>> source)
        {
            var copy = new Dictionary<string, T>(Comparer);
            if (source != null)
                foreach (var entry in source)
                    copy[entry.Key] = entry.Value;

            return copy;
        }
    }
}
