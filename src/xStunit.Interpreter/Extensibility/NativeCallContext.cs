using System;
using System.Collections.Generic;
using System.Linq;

namespace xStunit.Interpreter.Extensibility
{
    // Everything an IXstunitNativeFunction is allowed to see about the call
    // being made (TcXunit-6k2): the function's name, its already-evaluated
    // arguments, and a way to read bytes behind a POINTER argument.
    //
    // Arguments arrive evaluated - a plugin gets CLR values, never Expr trees
    // or Frames - so plugin code can't re-enter the interpreter, evaluate
    // arbitrary ST, or observe the caller's scope. The one interpreter service
    // exposed is ReadBytes, because the byte-layout machinery behind a POINTER
    // TO BYTE (Engine.ByteLayout.cs) is not something a plugin could
    // reasonably reimplement, and a large share of real library functions
    // (checksums, CRCs, serializers) exist precisely to walk a byte buffer.
    public sealed class NativeCallContext
    {
        private readonly Func<Pointer, int, byte[]> _readBytes;

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

        // The ST identifier as the caller wrote it, which may differ in casing
        // from IXstunitNativeFunction.Name (lookup is case-insensitive). Useful
        // for error messages that should echo the source text.
        public string FunctionName { get; }

        // Arguments passed positionally, in source order. A call that names
        // every argument leaves this empty.
        public IReadOnlyList<object> PositionalArgs { get; }

        // Arguments passed as `name := value`, keyed by the name as written.
        public IReadOnlyDictionary<string, object> NamedArgs { get; }

        // TcXunit-kuc: positions this call has confirmed are filled by a named
        // argument, discovered as the plugin queries them (a plugin's `position`
        // argument is always the parameter's declared index in the signature -
        // see every IXstunitNativeFunction under samples/ - never a running
        // "slot within PositionalArgs" counter, since a plugin has no way to
        // know at compile time which of its parameters a given call will name).
        // Needed because a named argument can occupy any declared position, so
        // a later positional argument must skip it rather than being read
        // straight out of PositionalArgs by its raw declared index (that was
        // the bug: `FIND(STR1 := s, '[')` left STR2 reading PositionalArgs[1],
        // which doesn't exist - the one positional arg supplied belongs at
        // PositionalArgs[0]).
        private readonly HashSet<int> _namedPositions = new HashSet<int>();

        // Resolves one declared parameter the way TwinCAT call syntax allows it
        // to be supplied: by name, else by position. Mirrors the interpreter's
        // own intrinsic-argument resolution (Engine.Expressions.cs's
        // ResolveIntrinsicArgs) and BindParams (Engine.Invocation.cs) /
        // ArgBinder: a positional argument fills the next declared parameter
        // that isn't already spoken for by name, not the PositionalArgs slot
        // matching its own declared index.
        //
        // `position` is always the parameter's 0-based declared index in the
        // signature (matching every IXstunitNativeFunction under samples/,
        // which query params left-to-right by that index). The declared index
        // and the PositionalArgs slot coincide only once every parameter
        // before this one has also been resolved positionally; the moment one
        // of them is named, everything after it shifts left by one slot per
        // named parameter that precedes it. That shift is computed here as
        // `position` minus however many named positions less than `position`
        // have been discovered so far (this call included, via the branch
        // above) - correct as long as parameters are queried in ascending
        // declared-index order, which is how every plugin in this codebase
        // (and the natural way to write one) reads its own arguments. A
        // plugin that deliberately queries out of order (e.g. reading a later
        // parameter before an earlier one purely for local computation, as
        // CheckSum16Function does) is unaffected as long as it does so only
        // among parameters that end up all-positional or all-named for that
        // call - mixing an out-of-order read with a *named* earlier parameter
        // it hasn't queried yet is the one combination this can't see coming,
        // since nothing this class receives records a plugin's full parameter
        // list up front.
        //
        // Returns false when the argument was omitted, letting a plugin model
        // an optional trailing parameter (CONCAT's STR3..STR10 shape).
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

        // TryGetArg for a parameter that isn't optional. Throwing here produces
        // the same fault shape as any other bad call: attributed to the PLC
        // call site, with the function name in the message.
        public object RequireArg(string paramName, int position)
        {
            if (TryGetArg(paramName, position, out var value))
                return value;

            // TcXunit-kuc: name the actual shortfall rather than a flat
            // positional/named count that reads the same whether the caller
            // wrote too few arguments or wrote enough but with a named
            // argument occupying a declared position before this one - the
            // latter shifts which PositionalArgs slot this parameter needs by
            // however many named arguments precede it, so "missing" here
            // means that adjusted slot didn't exist, not that PositionalArgs
            // itself came up short by the raw declared position.
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

        // Integer accessor covering every IEC integer type at once: the
        // interpreter represents SINT/USINT/BYTE/INT/UINT/WORD/DINT as int but
        // UDINT/DWORD/LINT as long and ULINT/LWORD as ulong, so a plugin that
        // pattern-matched on `is int` alone would break the moment a caller
        // passed a UDINT - which is exactly what a size/length argument
        // usually is. Rejects bool and floating-point rather than silently
        // truncating them.
        public int RequireInt32(string paramName, int position) =>
            checked((int)RequireInt64(paramName, position));

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

        public string RequireString(string paramName, int position)
        {
            var value = RequireArg(paramName, position);
            if (value is string s)
                return s;

            throw new InvalidOperationException(
                $"{FunctionName} argument '{paramName}' must be a STRING, got {Describe(value)}");
        }

        public bool RequireBool(string paramName, int position)
        {
            var value = RequireArg(paramName, position);
            if (value is bool b)
                return b;

            throw new InvalidOperationException(
                $"{FunctionName} argument '{paramName}' must be a BOOL, got {Describe(value)}");
        }

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

        // Reads `count` bytes starting at a POINTER argument - the ADR(buf) /
        // ADR(buf[i]) / ADR(someStruct) shapes MEMCPY already accepts, resolved
        // through the same byte-layout rules (Engine.ByteLayout.cs), so a
        // plugin sees exactly the bytes MEMCPY would have copied.
        //
        // Read-only by design. A plugin can inspect a buffer to compute a
        // checksum or parse a record, but cannot write back into interpreted
        // program state; a library function that mutates its output parameter
        // is out of scope for this extension point rather than something to
        // reach through here.
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

        // Convenience for the common "all arguments were positional" test/host
        // case, so a caller doesn't have to hand-build an empty named map.
        public static NativeCallContext ForPositional(
            string functionName, params object[] positionalArgs) =>
            new NativeCallContext(
                functionName,
                positionalArgs?.ToList() ?? new List<object>(),
                new Dictionary<string, object>(),
                readBytes: null);
    }
}
