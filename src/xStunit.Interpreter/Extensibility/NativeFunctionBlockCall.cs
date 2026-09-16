using System;
using System.Collections.Generic;

namespace xStunit.Interpreter.Extensibility
{
    /// <summary>
    /// Everything an <see cref="IXstunitNativeFunctionBlock"/> is allowed to see about
    /// one invocation: which call it is, the arguments of a method call, and read/write
    /// access to its own instance's fields.
    /// </summary>
    /// <remarks>
    /// The same containment rule as <see cref="NativeCallContext"/>, with one deliberate
    /// widening. A stateful FB has to WRITE - its VAR_OUTPUTs are the only way a suite
    /// can observe it, and a vendor FB with a VAR_IN_OUT buffer (FB_FileRead filling the
    /// caller's array) must put bytes back where the caller pointed. So field writes and
    /// <see cref="WriteBytes"/> exist, where the pure-function surface offers neither.
    /// <para>
    /// The blast radius stays bounded: a plugin reaches its OWN instance's fields and
    /// whatever a pointer the caller explicitly handed it targets, never the Engine, the
    /// TypeRegistry, or the caller's Frame. It cannot evaluate ST, define types, or
    /// observe any scope it was not pointed at.
    /// </para>
    /// </remarks>
    public sealed class NativeFunctionBlockCall
    {
        private readonly IDictionary<string, Cell> _fields;
        private readonly Func<Pointer, int, byte[]> _readBytes;
        private readonly Action<Pointer, byte[]> _writeBytes;

        /// <param name="typeName">The plugin FB's own type name, for failure messages.</param>
        /// <param name="methodName">The method being called, or null for a bare <c>fb(...)</c> invocation.</param>
        /// <param name="arguments">The method call's evaluated arguments; null becomes an empty context, which is also what a bare invocation gets.</param>
        /// <param name="fields">The owning instance's live field storage. Held by reference, not copied - a write through <see cref="SetField"/> is what ST reads back.</param>
        /// <param name="simulatedTimeNs">The shared simulated clock's running total in nanoseconds, so a timeout-shaped FB measures the same time the timers do.</param>
        /// <param name="readBytes">Interpreter callback resolving a pointer and a byte count to the bytes behind it; null leaves <see cref="ReadBytes"/> throwing.</param>
        /// <param name="writeBytes">Interpreter callback writing bytes back behind a pointer; null leaves <see cref="WriteBytes"/> throwing.</param>
        /// <exception cref="ArgumentNullException"><paramref name="typeName"/> or <paramref name="fields"/> is null.</exception>
        public NativeFunctionBlockCall(
            string typeName,
            string methodName,
            NativeCallContext arguments,
            IDictionary<string, Cell> fields,
            long simulatedTimeNs,
            Func<Pointer, int, byte[]> readBytes,
            Action<Pointer, byte[]> writeBytes)
        {
            TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
            _fields = fields ?? throw new ArgumentNullException(nameof(fields));
            MethodName = methodName;
            Arguments = arguments ?? NativeCallContext.ForPositional(methodName ?? typeName);
            SimulatedTimeNs = simulatedTimeNs;
            _readBytes = readBytes;
            _writeBytes = writeBytes;
        }

        /// <summary>The plugin FB type this instance stands in for.</summary>
        public string TypeName { get; }

        /// <summary>
        /// The method the caller wrote, or null for a bare <c>fb(...)</c> invocation.
        /// </summary>
        /// <remarks>
        /// May differ in casing from the entry in
        /// <see cref="IXstunitNativeFunctionBlock.MethodNames"/> that matched it, since
        /// method routing is case-insensitive.
        /// </remarks>
        public string MethodName { get; }

        /// <summary>
        /// Whether this is a bare <c>fb(...)</c> call rather than one of
        /// <see cref="IXstunitNativeFunctionBlock.MethodNames"/>.
        /// </summary>
        public bool IsBareInvocation => MethodName == null;

        /// <summary>
        /// A method call's evaluated arguments.
        /// </summary>
        /// <remarks>
        /// Empty for a bare invocation: those arguments were already bound into the
        /// instance's fields before <see cref="IXstunitNativeFunctionBlock.Invoke"/> was
        /// entered, the way a real FB's VAR_INPUTs are written by the call, and reading
        /// them anywhere but <see cref="GetField"/> would see only what this one call
        /// happened to pass rather than what the input currently holds.
        /// </remarks>
        public NativeCallContext Arguments { get; }

        /// <summary>
        /// The shared simulated clock's total elapsed nanoseconds - never wall time, so
        /// a suite that advances the clock gets a deterministic result.
        /// </summary>
        public long SimulatedTimeNs { get; }

        /// <param name="name">The field name, as declared in <see cref="IXstunitNativeFunctionBlock.Fields"/>.</param>
        /// <returns>The field's current value, which ST may have assigned since the last invocation.</returns>
        /// <exception cref="InvalidOperationException">No such field was declared.</exception>
        public object GetField(string name)
        {
            if (TryGetField(name, out var value))
                return value;

            throw new InvalidOperationException(
                $"{TypeName} has no field '{name}' - every field a native function block reads or writes " +
                $"must appear in its {nameof(IXstunitNativeFunctionBlock.Fields)}");
        }

        /// <summary>
        /// <see cref="GetField"/> for a field whose absence is not an error, e.g. one an
        /// older revision of the plugin's own declaration did not carry.
        /// </summary>
        /// <param name="name">The field name.</param>
        /// <param name="value">The field's value, or null when there is no such field.</param>
        /// <returns>False when no such field was declared.</returns>
        public bool TryGetField(string name, out object value)
        {
            if (name != null && _fields.TryGetValue(name, out var cell))
            {
                value = cell.Value;
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Publishes a VAR_OUTPUT, which ST then reads back by plain dot access.
        /// </summary>
        /// <param name="name">The field name, as declared in <see cref="IXstunitNativeFunctionBlock.Fields"/>.</param>
        /// <param name="value">
        /// The new value in the interpreter's CLR representation for the field's IEC type
        /// (see <see cref="IXstunitNativeFunction.Invoke"/>); a mismatch surfaces later,
        /// as a wrong comparison in whatever assertion reads the field.
        /// </param>
        /// <exception cref="InvalidOperationException">No such field was declared.</exception>
        public void SetField(string name, object value)
        {
            if (name == null || !_fields.TryGetValue(name, out var cell))
            {
                throw new InvalidOperationException(
                    $"{TypeName} has no field '{name}' - every field a native function block reads or writes " +
                    $"must appear in its {nameof(IXstunitNativeFunctionBlock.Fields)}");
            }

            cell.Value = value;
        }

        /// <summary>
        /// Reads <paramref name="count"/> bytes behind a POINTER the caller supplied,
        /// through the same byte-layout rules MEMCPY uses.
        /// </summary>
        /// <param name="pointer">A pointer value taken from a field, i.e. what ST passed as ADR(buf).</param>
        /// <param name="count">How many bytes to read; must be &gt;= 0 and within the buffer.</param>
        /// <returns>A fresh copy of the bytes.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="pointer"/> is null, which is how an unset POINTER field reads.</exception>
        /// <exception cref="InvalidOperationException">This call was built without a byte reader, i.e. outside the interpreter.</exception>
        public byte[] ReadBytes(Pointer pointer, int count)
        {
            if (pointer == null)
                throw new ArgumentNullException(nameof(pointer), $"{TypeName} was given a null pointer to read from");

            if (_readBytes == null)
                throw new InvalidOperationException(
                    $"{TypeName} asked to read pointer bytes, but this call was created without a byte " +
                    "reader - only the interpreter can supply one");

            return _readBytes(pointer, count);
        }

        /// <summary>
        /// Writes bytes back behind a POINTER the caller supplied - the VAR_IN_OUT half
        /// a read-shaped vendor FB needs, and the one thing the pure-function surface
        /// deliberately withholds.
        /// </summary>
        /// <param name="pointer">A pointer value taken from a field, i.e. what ST passed as ADR(buf).</param>
        /// <param name="bytes">The bytes to write; must fit within the buffer the pointer targets.</param>
        /// <exception cref="ArgumentNullException"><paramref name="pointer"/> or <paramref name="bytes"/> is null.</exception>
        /// <exception cref="InvalidOperationException">This call was built without a byte writer, i.e. outside the interpreter.</exception>
        public void WriteBytes(Pointer pointer, byte[] bytes)
        {
            if (pointer == null)
                throw new ArgumentNullException(nameof(pointer), $"{TypeName} was given a null pointer to write to");
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            if (_writeBytes == null)
                throw new InvalidOperationException(
                    $"{TypeName} asked to write pointer bytes, but this call was created without a byte " +
                    "writer - only the interpreter can supply one");

            _writeBytes(pointer, bytes);
        }
    }
}
