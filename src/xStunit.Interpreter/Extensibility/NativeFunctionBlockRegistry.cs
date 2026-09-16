using System;
using System.Collections.Generic;
using System.Linq;

namespace xStunit.Interpreter.Extensibility
{
    /// <summary>
    /// Type name -&gt; <see cref="IXstunitNativeFunctionBlock"/> prototype lookup the
    /// Engine consults when an FB's ancestry names a type no source and no in-tree stub
    /// accounts for.
    /// </summary>
    /// <remarks>
    /// <see cref="NativeFunctionRegistry"/>'s FB-shaped sibling, with the same ownership
    /// and the same duplicate rule. Owned by the host: an Engine given none of these
    /// behaves exactly as before, so registering is purely additive.
    /// <para>
    /// What is stored is the prototype, never a live instance - the Engine calls
    /// <see cref="IXstunitNativeFunctionBlock.CreateInstance"/> once per declared
    /// variable. A registry is therefore safely shared across suites and runs.
    /// </para>
    /// </remarks>
    public sealed class NativeFunctionBlockRegistry
    {
        // OrdinalIgnoreCase for the same reason as NativeFunctionRegistry: IEC 61131-3
        // type names are case-insensitive, and PLC source is inconsistent about how it
        // spells a vendor FB.
        private readonly Dictionary<string, IXstunitNativeFunctionBlock> _blocks =
            new Dictionary<string, IXstunitNativeFunctionBlock>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> _sources =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public int Count => _blocks.Count;

        /// <summary>
        /// The registered type names, each cased as its implementation declared it.
        /// </summary>
        public IEnumerable<string> TypeNames => _blocks.Keys;

        /// <param name="functionBlock">
        /// The prototype to make instantiable under its own
        /// <see cref="IXstunitNativeFunctionBlock.TypeName"/>. Only its metadata is read;
        /// the Engine drives <see cref="IXstunitNativeFunctionBlock.CreateInstance"/>
        /// products instead.
        /// </param>
        /// <param name="source">
        /// Where this came from (a plugin path, a test name), quoted back in the
        /// duplicate-registration message. Defaults to the implementation's type name.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="functionBlock"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// The prototype reports a null/blank <see cref="IXstunitNativeFunctionBlock.TypeName"/>,
        /// or a null <see cref="IXstunitNativeFunctionBlock.Fields"/>,
        /// <see cref="IXstunitNativeFunctionBlock.PositionalInputNames"/> or
        /// <see cref="IXstunitNativeFunctionBlock.MethodNames"/> - each would otherwise
        /// fail far later, at instantiation or dispatch, with nothing left to name the
        /// culprit.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The type name is already registered. A second registration throws rather than
        /// winning: two plugins disagreeing about FB_FileOpen is a configuration mistake
        /// whose symptom would otherwise be a wrong result in a passing test. The host
        /// decides how loud to be - the CLI catches this and skips the offending DLL
        /// rather than aborting the run.
        /// </exception>
        public void Register(IXstunitNativeFunctionBlock functionBlock, string source = null)
        {
            if (functionBlock == null)
                throw new ArgumentNullException(nameof(functionBlock));

            if (string.IsNullOrWhiteSpace(functionBlock.TypeName))
                throw new ArgumentException($"{functionBlock.GetType().FullName} returned a null/blank TypeName", nameof(functionBlock));

            // All three lists are read by the Engine without a null check of
            // its own, at instantiation and dispatch respectively - far enough
            // from here that a NullReferenceException there names nothing
            // useful. "Declares none" is spelled as an empty list.
            if (functionBlock.Fields == null)
                throw new ArgumentException($"{functionBlock.TypeName} returned a null Fields", nameof(functionBlock));

            if (functionBlock.PositionalInputNames == null)
                throw new ArgumentException($"{functionBlock.TypeName} returned a null PositionalInputNames", nameof(functionBlock));

            if (functionBlock.MethodNames == null)
                throw new ArgumentException($"{functionBlock.TypeName} returned a null MethodNames", nameof(functionBlock));

            if (_blocks.ContainsKey(functionBlock.TypeName))
            {
                var existing = _sources.TryGetValue(functionBlock.TypeName, out var s) ? s : "an earlier registration";
                throw new InvalidOperationException(
                    $"native function block '{functionBlock.TypeName}' is already registered by {existing}" +
                    (source != null ? $"; {source} cannot register it again" : string.Empty));
            }

            _blocks[functionBlock.TypeName] = functionBlock;
            _sources[functionBlock.TypeName] = source ?? functionBlock.GetType().FullName;
        }

        /// <param name="functionBlocks">Prototypes to register in order; null is treated as empty.</param>
        /// <param name="source">Attribution applied to every one of them.</param>
        /// <exception cref="InvalidOperationException">
        /// Any type name is already registered. Not atomic: whatever preceded the clash
        /// stays registered, so a host that wants all-or-nothing must discard the whole
        /// registry.
        /// </exception>
        public void RegisterAll(IEnumerable<IXstunitNativeFunctionBlock> functionBlocks, string source = null)
        {
            foreach (var block in functionBlocks ?? Enumerable.Empty<IXstunitNativeFunctionBlock>())
                Register(block, source);
        }

        /// <param name="typeName">The ST type name as source spells it.</param>
        /// <param name="functionBlock">The registered prototype, or null when none matches.</param>
        /// <returns>False when no plugin claims this type name.</returns>
        public bool TryGet(string typeName, out IXstunitNativeFunctionBlock functionBlock)
        {
            if (typeName == null)
            {
                functionBlock = null;
                return false;
            }

            return _blocks.TryGetValue(typeName, out functionBlock);
        }

        /// <summary>
        /// Whether a VAR declared of this type should be constructed as a native FB
        /// instance rather than an elementary-type default.
        /// </summary>
        /// <param name="typeName">The ST type name as source spells it.</param>
        /// <returns>True when some plugin claims this type name.</returns>
        public bool Contains(string typeName) => typeName != null && _blocks.ContainsKey(typeName);

        /// <summary>
        /// Each registered type name paired with the source that claimed it, for a host
        /// reporting what a run has available.
        /// </summary>
        /// <returns>One <c>name (source)</c> line per registration, ordered by name.</returns>
        public IReadOnlyList<string> DescribeRegistrations() =>
            _blocks.Keys
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Select(n => $"{n} ({_sources[n]})")
                .ToList();
    }
}
