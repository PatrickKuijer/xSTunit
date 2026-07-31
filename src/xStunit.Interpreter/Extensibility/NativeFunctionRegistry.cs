using System;
using System.Collections.Generic;
using System.Linq;

namespace xStunit.Interpreter.Extensibility
{
    // Name -> IXstunitNativeFunction lookup the Engine consults as its last
    // resort before reporting a call unresolved (TcXunit-6k2).
    //
    // Owned by the host, not the Engine: an Engine built without one behaves
    // exactly as it always has (every unresolved call is still an error), so
    // this is purely additive to existing call sites. The CLI populates one
    // from a plugin directory; a test populates one inline.
    public sealed class NativeFunctionRegistry
    {
        // OrdinalIgnoreCase because IEC 61131-3 identifiers are
        // case-insensitive: PLC source calling F_CheckSum16, F_CHECKSUM16, or
        // f_checksum16 must all reach the same registration. (The interpreter's
        // own POU/method dispatch is ordinal - a pre-existing inconsistency
        // rather than something this type should quietly diverge on, but a
        // case-sensitive plugin lookup would fail in a way the user cannot
        // debug from the error message alone, so the forgiving rule wins here.)
        private readonly Dictionary<string, IXstunitNativeFunction> _functions =
            new Dictionary<string, IXstunitNativeFunction>(StringComparer.OrdinalIgnoreCase);

        // Where each name came from, for the duplicate-registration message -
        // with plugins loaded from a folder, "which DLL already claimed this?"
        // is the only useful thing to say.
        private readonly Dictionary<string, string> _sources =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public int Count => _functions.Count;

        public IEnumerable<string> Names => _functions.Keys;

        // A second registration of the same name is a hard error rather than
        // last-one-wins: two plugins disagreeing about F_CheckSum16 is a
        // configuration mistake whose symptom would otherwise be a wrong
        // number in a passing test, which is far worse than a startup failure.
        // The host decides how loud to be - the CLI turns this into a skip
        // line for the offending DLL instead of aborting the run.
        public void Register(IXstunitNativeFunction function, string source = null)
        {
            if (function == null)
                throw new ArgumentNullException(nameof(function));

            if (string.IsNullOrWhiteSpace(function.Name))
                throw new ArgumentException($"{function.GetType().FullName} returned a null/blank Name", nameof(function));

            if (_functions.ContainsKey(function.Name))
            {
                var existing = _sources.TryGetValue(function.Name, out var s) ? s : "an earlier registration";
                throw new InvalidOperationException(
                    $"native function '{function.Name}' is already registered by {existing}" +
                    (source != null ? $"; {source} cannot register it again" : string.Empty));
            }

            _functions[function.Name] = function;
            _sources[function.Name] = source ?? function.GetType().FullName;
        }

        public void RegisterAll(IEnumerable<IXstunitNativeFunction> functions, string source = null)
        {
            foreach (var function in functions ?? Enumerable.Empty<IXstunitNativeFunction>())
                Register(function, source);
        }

        public bool TryGet(string name, out IXstunitNativeFunction function)
        {
            if (name == null)
            {
                function = null;
                return false;
            }

            return _functions.TryGetValue(name, out function);
        }

        // Names in a stable, human-readable order, for a host that wants to
        // report what a run has available (the CLI's --list-plugins).
        public IReadOnlyList<string> DescribeRegistrations() =>
            _functions.Keys
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Select(n => $"{n} ({_sources[n]})")
                .ToList();
    }
}
