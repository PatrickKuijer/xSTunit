using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Interpreter.Extensibility;

namespace xStunit.Interpreter
{
    public sealed partial class Engine
    {
        // Bridge between the interpreter's call machinery and a host-registered
        // stand-in for a compiled-only TwinCAT library function. The boundary
        // itself is NewNativeCallContext's, below.
        private object InvokeNativeFunction(
            IXstunitNativeFunction function,
            string methodName,
            IReadOnlyList<Expr> positionalArgs,
            IReadOnlyList<NamedArg> namedArgs,
            Frame callerFrame)
        {
            return function.Invoke(NewNativeCallContext(methodName, positionalArgs, namedArgs, callerFrame));
        }

        // Evaluates a call's arguments in the caller's scope and wraps them in
        // the plugin-facing context. Shared by the function surface above and
        // the method half of the function-block surface, so both see arguments
        // bound by the same rules.
        private NativeCallContext NewNativeCallContext(
            string methodName,
            IReadOnlyList<Expr> positionalArgs,
            IReadOnlyList<NamedArg> namedArgs,
            Frame callerFrame)
        {
            var evaluatedPositional = positionalArgs.Select(e => Evaluate(e, callerFrame)).ToList();

            // Last-one-wins on a duplicated name rather than throwing: matches
            // how the suite-host bridge builds its own named-arg dictionary,
            // and a duplicate named argument is a source-level mistake the
            // parser is the right place to reject, not this call site.
            var evaluatedNamed = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var arg in namedArgs)
                evaluatedNamed[arg.Name] = Evaluate(arg.Value, callerFrame);

            // methodName (as written at the call site), not the registered
            // name - lookup is case-insensitive, so the two can differ, and an
            // error message should echo the source the user can actually go
            // read.
            return new NativeCallContext(
                methodName,
                evaluatedPositional,
                evaluatedNamed,
                (ptr, count) => ReadPointerBytes(ptr, count, methodName, callerFrame),
                CurrentSimulatedTime());
        }

        // One snapshot shape for both plugin surfaces, taken at the call rather
        // than handed out as the live Clock: a plugin that could advance the
        // clock would move time for every timer in the run.
        private SimulatedTime CurrentSimulatedTime() =>
            new SimulatedTime(Clock.TotalNs, Clock.UtcNow, Clock.TaskStartUtc);

        // Whether methodName is one the plugin claims. Case-insensitive, like
        // every other IEC identifier match, and like the type-name lookup that
        // found this plugin in the first place - a plugin declaring "Enter"
        // must answer source written 'ENTER()'.
        private static bool DeclaresMethod(IXstunitNativeFunctionBlock functionBlock, string methodName)
        {
            foreach (var declared in functionBlock.MethodNames)
                if (string.Equals(declared, methodName, StringComparison.OrdinalIgnoreCase))
                    return true;

            return false;
        }

        // The function-block counterpart: adds the instance's own field storage
        // and the pointer write-back a read-shaped vendor FB needs, neither of
        // which the pure-function context has any business offering.
        private NativeFunctionBlockCall NewFunctionBlockCall(
            FbInstance instance,
            string methodName,
            NativeCallContext arguments,
            Frame callerFrame)
        {
            var label = methodName ?? instance.ActualTypeName;

            return new NativeFunctionBlockCall(
                instance.ActualTypeName,
                methodName,
                arguments,
                instance.Fields,
                CurrentSimulatedTime(),
                (ptr, count) => ReadPointerBytes(ptr, count, label, callerFrame),
                (ptr, bytes) => WritePointerBytes(ptr, bytes, label, callerFrame));
        }

        // Reads count bytes from behind a POINTER argument through
        // ResolveByteTarget, so a plugin sees exactly the bytes MEMCPY would
        // have copied out of the same pointer - whether it points at a real BYTE
        // array element or at a scalar/STRUCT Cell packed into a byte view on
        // the fly.
        //
        // Read-only: ResolveByteTarget's write-back Commit is deliberately
        // discarded. A plugin can inspect interpreted program state through a
        // pointer but cannot mutate it.
        private byte[] ReadPointerBytes(Pointer ptr, int count, string methodName, Frame frame)
        {
            var (array, index, _) = ResolveByteTarget(ptr, methodName, "pointer", frame);

            // Bounds-check up front rather than letting the element loop throw
            // IndexOutOfRange: a plugin reading past the end of a buffer is a
            // PLC-source bug (wrong size argument), and the message needs to
            // say so in those terms. A real PLC would happily read adjacent
            // memory here; the interpreter has no adjacent memory to read, so
            // refusing is the only honest option.
            if (count > array.Elements.Length - index)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    $"{methodName} asked to read {count} byte(s) from an offset with only " +
                    $"{array.Elements.Length - index} byte(s) available - check the size argument at the call site");
            }

            var bytes = new byte[count];
            for (var i = 0; i < count; i++)
                bytes[i] = (byte)(Convert.ToInt32(array.Elements[index + i]) & 0xFF);

            return bytes;
        }

        // ReadPointerBytes' write half, for the VAR_IN_OUT buffer a stateful
        // library FB fills on the caller's behalf. Commit is invoked here where
        // ReadPointerBytes discards it: a pointer at a scalar or STRUCT Cell is
        // served through a packed byte view, and without the unpack back into
        // the Cell the write would land in a temporary and vanish.
        private void WritePointerBytes(Pointer ptr, byte[] bytes, string methodName, Frame frame)
        {
            var (array, index, commit) = ResolveByteTarget(ptr, methodName, "pointer", frame);

            if (bytes.Length > array.Elements.Length - index)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bytes),
                    $"{methodName} asked to write {bytes.Length} byte(s) to an offset with only " +
                    $"{array.Elements.Length - index} byte(s) available - check the size argument at the call site");
            }

            for (var i = 0; i < bytes.Length; i++)
                array.SetElement(index + i, (int)bytes[i]);

            commit?.Invoke();
        }
    }
}
