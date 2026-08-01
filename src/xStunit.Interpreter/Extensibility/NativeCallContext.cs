using System;
using System.Collections.Generic;
using System.Linq;

namespace xStunit.Interpreter.Extensibility
{
    /// <summary>
    /// Everything an <see cref="IXstunitNativeFunction"/> is allowed to see about the
    /// call being made: the function's name, its already-evaluated arguments, and a way
    /// to read bytes behind a POINTER argument.
    /// </summary>
    /// <remarks>
    /// Arguments arrive evaluated - a plugin gets CLR values, never Expr trees or
    /// Frames - so plugin code cannot re-enter the interpreter, evaluate arbitrary ST,
    /// or observe the caller's scope. The one interpreter service exposed is byte
    /// reading, because the byte-layout machinery behind a POINTER TO BYTE
    /// (Engine.ByteLayout.cs) is not something a plugin could reasonably reimplement,
    /// and a large share of real library functions (checksums, CRCs, serializers) exist
    /// precisely to walk a byte buffer.
    /// </remarks>
    public sealed class NativeCallContext
    {
        private readonly Func<Pointer, int, byte[]> _readBytes;

        /// <param name="functionName">The identifier as written at the PLC call site.</param>
        /// <param name="positionalArgs">Evaluated positional arguments in source order; null is treated as empty.</param>
        /// <param name="namedArgs">Evaluated <c>name := value</c> arguments keyed by the name as written; null is treated as empty.</param>
        /// <param name="readBytes">
        /// Interpreter callback resolving a pointer and byte count to the bytes behind
        /// it. Null leaves the context unable to serve
        /// <see cref="RequireBytes"/>, which then throws.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="functionName"/> is null.</exception>
        public NativeCallContext(
            string functionName,
            IReadOnlyList<object> positionalArgs,
            IReadOnlyDictionary<string, object> namedArgs,
            Func<Pointer, int, byte[]> readBytes)
        {
            FunctionName = functionName ?? throw new ArgumentNullException(nameof(functionName));
            PositionalArgs = positionalArgs ?? Array.Empty<object>();
            NamedArgs = namedArgs ?? new Dictionary<string, object>();
            _readBytes = readBytes;
        }

        /// <summary>
        /// The ST identifier as the caller wrote it, which may differ in casing from
        /// <see cref="IXstunitNativeFunction.Name"/> (lookup is case-insensitive).
        /// </summary>
        public string FunctionName { get; }

        /// <summary>
        /// Arguments passed positionally, in source order. A call that names every
        /// argument leaves this empty.
        /// </summary>
        public IReadOnlyList<object> PositionalArgs { get; }

        /// <summary>
        /// Arguments passed as <c>name := value</c>, keyed by the name as written.
        /// </summary>
        public IReadOnlyDictionary<string, object> NamedArgs { get; }

        // Accumulated as the plugin queries them: a named argument can occupy any
        // declared position, so every parameter after it sits one PositionalArgs slot
        // earlier than its declared index.
        private readonly HashSet<int> _namedPositions = new HashSet<int>();

        /// <summary>
        /// Resolves one declared parameter the way TwinCAT call syntax allows it to be
        /// supplied: by name, else by position - a positional argument fills the next
        /// declared parameter not already spoken for by name. Same rule as the
        /// interpreter's own argument binding (Engine.Expressions.cs's
        /// ResolveIntrinsicArgs, ArgBinder).
        /// </summary>
        /// <param name="paramName">Declared parameter name to match against <see cref="NamedArgs"/>; null skips the by-name attempt.</param>
        /// <param name="position">
        /// The parameter's 0-based index in the DECLARED signature, never a slot within
        /// <see cref="PositionalArgs"/>; negative skips the by-position attempt.
        /// </param>
        /// <param name="value">The argument's value, or null when none was supplied.</param>
        /// <returns>
        /// False when the argument was omitted, letting a plugin model an optional
        /// trailing parameter (CONCAT's STR3..STR10 shape).
        /// </returns>
        /// <remarks>
        /// A plugin cannot know at compile time which of its parameters a given call
        /// will name, so declared index and positional slot coincide only until some
        /// earlier parameter is passed by name, after which everything following it
        /// shifts one slot left per preceding named argument. Without that correction
        /// <c>FIND(STR1 := s, '[')</c> would send STR2 to <c>PositionalArgs[1]</c>,
        /// which does not exist - the single positional argument is at index 0.
        /// <para>
        /// The shift is only correct while a plugin queries its parameters in ascending
        /// declared order, since named positions are discovered as they are asked for
        /// and nothing here receives the plugin's full parameter list up front.
        /// </para>
        /// </remarks>
        public bool TryGetArg(string paramName, int position, out object value)
        {
            if (paramName != null && NamedArgs.TryGetValue(paramName, out value))
            {
                if (position >= 0)
                    _namedPositions.Add(position);
                return true;
            }

            if (position >= 0)
            {
                var precedingNamedCount = 0;
                foreach (var namedPosition in _namedPositions)
                    if (namedPosition < position)
                        precedingNamedCount++;

                var slot = position - precedingNamedCount;
                if (slot >= 0 && slot < PositionalArgs.Count)
                {
                    value = PositionalArgs[slot];
                    return true;
                }
            }

            value = null;
            return false;
        }

        /// <summary>
        /// <see cref="TryGetArg"/> for a parameter that isn't optional.
        /// </summary>
        /// <param name="paramName">Declared parameter name, also used in the failure message.</param>
        /// <param name="position">The parameter's 0-based index in the declared signature.</param>
        /// <returns>The argument's value, which may itself be null if the caller passed null.</returns>
        /// <exception cref="InvalidOperationException">
        /// The argument was omitted. Throwing here produces the same fault shape as any
        /// other bad call: attributed to the PLC call site, with the function name in
        /// the message.
        /// </exception>
        public object RequireArg(string paramName, int position)
        {
            if (TryGetArg(paramName, position, out var value))
                return value;

            // A flat positional/named count reads identically whether the caller
            // supplied too few arguments or supplied enough but named one that occupies
            // an earlier declared position. Only the latter shifts the slot this
            // parameter needs, so say which happened.
            var precedingNamedCount = 0;
            foreach (var namedPosition in _namedPositions)
                if (namedPosition < position)
                    precedingNamedCount++;

            var detail = precedingNamedCount > 0
                ? $"after {precedingNamedCount} preceding named argument(s), " +
                  $"only {PositionalArgs.Count} positional argument(s) were supplied - " +
                  $"none remained for this parameter"
                : $"got {PositionalArgs.Count} positional and {NamedArgs.Count} named argument(s)";

            throw new InvalidOperationException(
                $"{FunctionName} missing required argument '{paramName}' (position {position}); {detail}");
        }

        /// <summary>
        /// <see cref="RequireInt64"/> narrowed to 32 bits.
        /// </summary>
        /// <param name="paramName">Declared parameter name.</param>
        /// <param name="position">The parameter's 0-based index in the declared signature.</param>
        /// <returns>The argument as an <see cref="int"/>.</returns>
        /// <exception cref="OverflowException">The argument does not fit in 32 bits.</exception>
        /// <exception cref="InvalidOperationException">The argument is missing or not an integer.</exception>
        public int RequireInt32(string paramName, int position) =>
            checked((int)RequireInt64(paramName, position));

        /// <summary>
        /// Reads an argument of any IEC integer type, whatever CLR type the interpreter
        /// chose to represent it with.
        /// </summary>
        /// <param name="paramName">Declared parameter name.</param>
        /// <param name="position">The parameter's 0-based index in the declared signature.</param>
        /// <returns>The argument widened to 64 bits.</returns>
        /// <exception cref="InvalidOperationException">The argument is missing, or is a BOOL/REAL/STRING rather than an integer.</exception>
        /// <remarks>
        /// Covers every IEC integer type at once: the interpreter represents
        /// SINT/USINT/BYTE/INT/UINT/WORD/DINT as int but UDINT/DWORD/LINT as long and
        /// ULINT/LWORD as ulong, so a plugin pattern-matching on <c>is int</c> alone
        /// breaks the moment a caller passes a UDINT - which is what a size/length
        /// argument usually is. Rejects bool and floating-point rather than silently
        /// truncating them.
        /// </remarks>
        public long RequireInt64(string paramName, int position)
        {
            var value = RequireArg(paramName, position);
            switch (value)
            {
                case int i: return i;
                case long l: return l;
                case ulong u: return checked((long)u);
                case short s: return s;
                case byte b: return b;
                case sbyte sb: return sb;
                case ushort us: return us;
                case uint ui: return ui;
                default:
                    throw new InvalidOperationException(
                        $"{FunctionName} argument '{paramName}' must be an integer, got {Describe(value)}");
            }
        }

        /// <exception cref="InvalidOperationException">The argument is missing or is not a STRING.</exception>
        public string RequireString(string paramName, int position)
        {
            var value = RequireArg(paramName, position);
            if (value is string s)
                return s;

            throw new InvalidOperationException(
                $"{FunctionName} argument '{paramName}' must be a STRING, got {Describe(value)}");
        }

        /// <exception cref="InvalidOperationException">The argument is missing or is not a BOOL.</exception>
        public bool RequireBool(string paramName, int position)
        {
            var value = RequireArg(paramName, position);
            if (value is bool b)
                return b;

            throw new InvalidOperationException(
                $"{FunctionName} argument '{paramName}' must be a BOOL, got {Describe(value)}");
        }

        /// <summary>
        /// Reads a REAL or LREAL argument, also accepting an integer where the caller
        /// passed a whole number.
        /// </summary>
        /// <param name="paramName">Declared parameter name.</param>
        /// <param name="position">The parameter's 0-based index in the declared signature.</param>
        /// <returns>The argument widened to <see cref="double"/>.</returns>
        /// <exception cref="InvalidOperationException">The argument is missing, or is neither numeric type.</exception>
        public double RequireReal(string paramName, int position)
        {
            var value = RequireArg(paramName, position);
            switch (value)
            {
                case float f: return f;
                case double d: return d;
                case int i: return i;
                case long l: return l;
                default:
                    throw new InvalidOperationException(
                        $"{FunctionName} argument '{paramName}' must be REAL or LREAL, got {Describe(value)}");
            }
        }

        /// <summary>
        /// Reads <paramref name="count"/> bytes behind a POINTER argument - the
        /// ADR(buf) / ADR(buf[i]) / ADR(someStruct) shapes MEMCPY already accepts,
        /// resolved through the same byte-layout rules (Engine.ByteLayout.cs), so a
        /// plugin sees exactly the bytes MEMCPY would have copied.
        /// </summary>
        /// <param name="paramName">Declared parameter name.</param>
        /// <param name="position">The parameter's 0-based index in the declared signature.</param>
        /// <param name="count">How many bytes to read; must be &gt;= 0.</param>
        /// <returns>A fresh copy of the bytes; writing to it does not touch interpreted state.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
        /// <exception cref="InvalidOperationException">
        /// The argument is missing or is not a <see cref="Pointer"/>, or this context
        /// was built without a byte reader (as <see cref="ForPositional"/> does).
        /// </exception>
        /// <remarks>
        /// Read-only by design: a plugin can inspect a buffer to compute a checksum or
        /// parse a record, but cannot write back into interpreted program state. A
        /// library function that mutates an output parameter is out of scope for this
        /// extension point.
        /// </remarks>
        public byte[] RequireBytes(string paramName, int position, int count)
        {
            var value = RequireArg(paramName, position);
            if (!(value is Pointer ptr))
            {
                throw new InvalidOperationException(
                    $"{FunctionName} argument '{paramName}' must be a POINTER (e.g. ADR(buf) or ADR(buf[i])), " +
                    $"got {Describe(value)}");
            }

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), $"{FunctionName} byte count must be >= 0");

            if (_readBytes == null)
            {
                throw new InvalidOperationException(
                    $"{FunctionName} asked to read pointer bytes, but this call context was created without " +
                    "a byte reader - only the interpreter can supply one");
            }

            return _readBytes(ptr, count);
        }

        private static string Describe(object value) =>
            value == null ? "null" : $"{value.GetType().Name} ({value})";

        /// <summary>
        /// Builds a context for a call with no named arguments and no interpreter
        /// behind it, for hosts and tests invoking a plugin directly.
        /// </summary>
        /// <param name="functionName">The identifier to report in failure messages.</param>
        /// <param name="positionalArgs">Evaluated arguments in source order.</param>
        /// <returns>A context whose <see cref="RequireBytes"/> throws, since no byte reader exists outside the interpreter.</returns>
        public static NativeCallContext ForPositional(
            string functionName, params object[] positionalArgs) =>
            new NativeCallContext(
                functionName,
                positionalArgs?.ToList() ?? new List<object>(),
                new Dictionary<string, object>(),
                readBytes: null);
    }
}
