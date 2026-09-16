using System;
using System.Collections.Generic;
using System.Linq;

namespace xStunit.Interpreter.Extensibility
{
    /// <summary>
    /// One named value a compiled-only library publishes - <c>FOPEN_MODEREAD</c>,
    /// <c>PATH_GENERIC</c>, <c>ADSLOG_MSGTYPE_ERROR</c>.
    /// </summary>
    /// <remarks>
    /// A library's constants live in its GVLs and ENUMs, which ship compiled just as
    /// its POUs do. Supplying the library's functions and blocks without them only
    /// half-solves the problem: source written the way real source is written -
    /// <c>nMode := FOPEN_MODEREAD OR FOPEN_MODEBINARY</c> - still fails on an
    /// unresolved identifier, and the POU is still skipped.
    /// </remarks>
    public sealed class NativeConstant
    {
        /// <param name="name">The identifier as source writes it unqualified (e.g. "PATH_GENERIC").</param>
        /// <param name="value">
        /// The value in the interpreter's CLR representation for its IEC type (see
        /// <see cref="IXstunitNativeFunction.Invoke"/> for the mapping) - a DWORD
        /// constant is a <see cref="long"/>, not an <see cref="int"/>.
        /// </param>
        /// <param name="qualifyingTypeName">
        /// The ENUM or GVL this also resolves under, so <c>E_OpenPath.PATH_GENERIC</c>
        /// and a bare <c>PATH_GENERIC</c> both reach it. Null for a constant with no
        /// qualified spelling.
        /// </param>
        /// <exception cref="ArgumentException"><paramref name="name"/> is null or blank.</exception>
        public NativeConstant(string name, object value, string qualifyingTypeName = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("a native constant needs a name", nameof(name));

            Name = name;
            Value = value;
            QualifyingTypeName = qualifyingTypeName;
        }

        public string Name { get; }

        public object Value { get; }

        /// <summary>
        /// The ENUM or GVL name this constant also answers to when qualified, or null.
        /// </summary>
        public string QualifyingTypeName { get; }
    }

    /// <summary>
    /// Supplies the named values of one compiled-only library.
    /// </summary>
    /// <remarks>
    /// A provider rather than one type per constant, unlike
    /// <see cref="IXstunitNativeFunction"/>: a constant has no behavior to put in a
    /// class of its own, and a library publishes them by the dozen.
    /// </remarks>
    public interface IXstunitNativeConstants
    {
        /// <summary>
        /// Every constant this provider publishes. Read once at registration, so the
        /// list must not depend on anything that changes afterwards.
        /// </summary>
        IReadOnlyList<NativeConstant> Constants { get; }
    }

    /// <summary>
    /// Name -&gt; value lookup the Engine consults as its last resort before reporting
    /// an identifier unknown.
    /// </summary>
    /// <remarks>
    /// Same ownership and duplicate rule as the function and block registries, and the
    /// same precedence: consulted only after locals, instance fields and real GVLs have
    /// all missed, so a plugin constant can never shadow a variable that exists.
    /// </remarks>
    public sealed class NativeConstantRegistry
    {
        // OrdinalIgnoreCase because IEC 61131-3 identifiers are, and real source is
        // inconsistent about how it spells a vendor constant.
        private readonly Dictionary<string, NativeConstant> _constants =
            new Dictionary<string, NativeConstant>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> _sources =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public int Count => _constants.Count;

        /// <summary>
        /// Every registered spelling, a qualified <c>Type.Member</c> counting separately
        /// from its bare form.
        /// </summary>
        public IEnumerable<string> Names => _constants.Keys;

        /// <param name="constant">The value to publish.</param>
        /// <param name="source">
        /// Where this came from (a plugin path, a test name), quoted back in the
        /// duplicate-registration message.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="constant"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Either spelling is already registered. Throws rather than winning: two
        /// libraries disagreeing about FOPEN_MODEREAD is a configuration mistake whose
        /// symptom would otherwise be a file opened in the wrong mode in a passing test.
        /// </exception>
        public void Register(NativeConstant constant, string source = null)
        {
            if (constant == null)
                throw new ArgumentNullException(nameof(constant));

            Claim(constant.Name, constant, source);

            // The qualified spelling is registered alongside the bare one rather than
            // instead of it: real source writes PATH_GENERIC unqualified, but
            // E_OpenPath.PATH_GENERIC is equally legal and both have to resolve.
            if (constant.QualifyingTypeName != null)
                Claim($"{constant.QualifyingTypeName}.{constant.Name}", constant, source);
        }

        /// <param name="constants">Values to register in order; null is treated as empty.</param>
        /// <param name="source">Attribution applied to every one of them.</param>
        /// <exception cref="InvalidOperationException">
        /// Any spelling is already registered. Not atomic: whatever preceded the clash
        /// stays registered.
        /// </exception>
        public void RegisterAll(IEnumerable<NativeConstant> constants, string source = null)
        {
            foreach (var constant in constants ?? Enumerable.Empty<NativeConstant>())
                Register(constant, source);
        }

        /// <param name="name">The identifier as source wrote it, bare or <c>Type.Member</c>.</param>
        /// <param name="value">The constant's value, or null when nothing claims the name.</param>
        /// <returns>False when no plugin publishes this name.</returns>
        public bool TryGet(string name, out object value)
        {
            if (name != null && _constants.TryGetValue(name, out var constant))
            {
                value = constant.Value;
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Each registered spelling paired with the source that claimed it, for a host
        /// reporting what a run has available.
        /// </summary>
        /// <returns>One <c>name (source)</c> line per registration, ordered by name.</returns>
        public IReadOnlyList<string> DescribeRegistrations() =>
            _constants.Keys
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Select(n => $"{n} ({_sources[n]})")
                .ToList();

        private void Claim(string name, NativeConstant constant, string source)
        {
            if (_constants.ContainsKey(name))
            {
                var existing = _sources.TryGetValue(name, out var s) ? s : "an earlier registration";
                throw new InvalidOperationException(
                    $"native constant '{name}' is already registered by {existing}" +
                    (source != null ? $"; {source} cannot register it again" : string.Empty));
            }

            _constants[name] = constant;
            _sources[name] = source ?? constant.GetType().FullName;
        }
    }
}
