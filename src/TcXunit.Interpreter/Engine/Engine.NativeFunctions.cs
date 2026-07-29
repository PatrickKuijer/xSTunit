using System;
using System.Collections.Generic;
using System.Linq;
using TcXunit.Interpreter.Extensibility;

namespace TcXunit.Interpreter
{
    public sealed partial class Engine
    {
        // Bridge between the interpreter's call machinery and a host-registered
        // stand-in for a compiled-only TwinCAT library function (TcXunit-6k2).
        //
        // Everything crosses the boundary already evaluated: the plugin gets
        // CLR values, not Expr trees, and never sees the Frame. That is the
        // whole point of the boundary - plugin code supplies a pure
        // arguments-in/value-out computation and cannot re-enter the
        // interpreter, evaluate ST, or observe the caller's scope. The one
        // service it does get is byte-level read access behind a POINTER
        // argument (see ReadPointerBytes below), because that is what the
        // checksum/CRC/serializer shape of library function actually needs and
        // it is not reimplementable outside the engine.
        private object InvokeNativeFunction(
            ITcXunitNativeFunction function,
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

            // methodName (as written at the call site), not function.Name -
            // lookup is case-insensitive, so the two can differ, and an error
            // message should echo the source the user can actually go read.
            var context = new NativeCallContext(
                methodName,
                evaluatedPositional,
                evaluatedNamed,
                (ptr, count) => ReadPointerBytes(ptr, count, methodName, callerFrame));

            return function.Invoke(context);
        }

        // Reads count bytes from behind a POINTER argument using the same
        // byte-layout rules MEMCPY uses (Engine.ByteLayout.cs's
        // ResolveByteTarget), so a plugin sees exactly the bytes MEMCPY would
        // have copied out of the same pointer - whether it points at a real
        // BYTE array element, or at a scalar/STRUCT Cell that gets packed into
        // a byte view on the fly.
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
    }
}
