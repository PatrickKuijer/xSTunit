using System;
using xStunit.Interpreter.Extensibility;

namespace xStunit.SystemLibraryPlugins
{
    // The epoch and unit Tc2_System's time functions answer in:
    //   FUNCTION F_GetSystemTime : ULINT
    //   FUNCTION F_GetTaskTime   : ULINT
    // both counting 100 ns intervals since 1 January 1601 UTC.
    //
    // That epoch is TwinCAT's knowledge, not the interpreter's - SimulatedTime
    // deals in plain UTC - so the conversion lives here, once, rather than in
    // each function.
    internal static class VendorFileTime
    {
        private static readonly DateTime Epoch = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // A DateTime tick IS a 100 ns interval, so the subtraction already
        // produces the vendor's unit with no scaling to get wrong.
        public static ulong From(DateTime utc) => (ulong)(utc - Epoch).Ticks;
    }

    // Reading wall time here would make every suite that touches it
    // non-deterministic - the same test passing on one machine and failing on
    // another - which is exactly what the simulated clock exists to prevent. A
    // suite advances the clock and asserts on the result.
    public sealed class GetSystemTimeFunction : IXstunitNativeFunction
    {
        public string Name => "F_GetSystemTime";

        public object Invoke(NativeCallContext context) => VendorFileTime.From(context.Time.UtcNow);
    }

    // The start of the cycle, not the current instant: the vendor doc is
    // explicit that this "always returns the start time of the task in which
    // the function was called". Two reads in one cycle therefore agree even
    // when the body advanced the clock between them, which is the whole
    // difference from F_GetSystemTime.
    public sealed class GetTaskTimeFunction : IXstunitNativeFunction
    {
        public string Name => "F_GetTaskTime";

        public object Invoke(NativeCallContext context) => VendorFileTime.From(context.Time.TaskStartUtc);
    }
}
