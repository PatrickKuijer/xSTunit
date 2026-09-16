using System;
using System.Collections.Generic;
using xStunit.Interpreter.Extensibility;

namespace xStunit.SystemLibraryPlugins
{
    // One recorded ADSLOG* call: the control mask, the format string as the
    // caller wrote it, and whichever argument that overload carries.
    //
    // The argument is kept in its own type rather than substituted into the
    // format string, deliberately. TwinCAT's %s/%d/%f substitution is C printf
    // formatting whose exact rendering (a %f's decimal count, in particular) is
    // not documented and has not been measured against a real PLC. Recording
    // what was passed asserts everything a suite actually cares about - that
    // this message, with this value, at this severity, was logged - without
    // claiming a rendering that might be wrong.
    internal sealed class AdsLogEntry
    {
        public AdsLogEntry(long mask, string format, object argument)
        {
            Mask = mask;
            Format = format;
            Argument = argument;
        }

        public long Mask { get; }

        public string Format { get; }

        public object Argument { get; }
    }

    // The sink itself, shared by every function in this assembly.
    //
    // Static because the functions that write it and the functions that read it
    // back are separate registrations with no object between them - a native
    // FUNCTION has no instance to hang state on, which is the whole difference
    // from the function-block surface. The cost is that entries outlive a
    // suite: the CLI loads one set of plugins for a whole run, so a suite that
    // asserts on a count must call F_AdsLogClear() first.
    internal static class AdsLogSink
    {
        private static readonly List<AdsLogEntry> Entries = new List<AdsLogEntry>();

        public static int Count => Entries.Count;

        public static void Record(NativeCallContext context, object argument) =>
            Entries.Add(new AdsLogEntry(
                context.RequireInt64("msgCtrlMask", 0),
                context.RequireString("msgFmtStr", 1),
                argument));

        public static void Clear() => Entries.Clear();

        public static AdsLogEntry At(NativeCallContext context)
        {
            var index = context.RequireInt32("nIndex", 0);
            if (index < 0 || index >= Entries.Count)
            {
                throw new InvalidOperationException(
                    $"{context.FunctionName}: no ADS log entry at index {index} - " +
                    $"{Entries.Count} entry/entries have been recorded since the last F_AdsLogClear()");
            }

            return Entries[index];
        }

        public static T ArgumentAs<T>(NativeCallContext context)
        {
            var entry = At(context);
            if (entry.Argument is T typed)
                return typed;

            throw new InvalidOperationException(
                $"{context.FunctionName}: the ADS log entry at that index carries a " +
                $"{entry.Argument?.GetType().Name ?? "null"} argument, not the requested type - " +
                "ADSLOGSTR, ADSLOGDINT and ADSLOGLREAL each record their own");
        }
    }

    // The three vendor overloads:
    //   FUNCTION ADSLOGSTR   : DINT (msgCtrlMask : DWORD; msgFmtStr : T_MaxString; strArg   : T_MaxString)
    //   FUNCTION ADSLOGDINT  : DINT (msgCtrlMask : DWORD; msgFmtStr : T_MaxString; dintArg  : DINT)
    //   FUNCTION ADSLOGLREAL : DINT (msgCtrlMask : DWORD; msgFmtStr : T_MaxString; lrealArg : LREAL)
    //
    // Their whole effect on a real system is a side effect a suite cannot see -
    // an event-log entry or a message box - so there is no return value worth
    // computing and, without a sink, nothing to assert beyond "the call didn't
    // fault". 0 is the vendor's success code.
    public sealed class AdsLogStrFunction : IXstunitNativeFunction
    {
        public string Name => "ADSLOGSTR";

        public object Invoke(NativeCallContext context)
        {
            AdsLogSink.Record(context, context.RequireString("strArg", 2));
            return 0;
        }
    }

    public sealed class AdsLogDintFunction : IXstunitNativeFunction
    {
        public string Name => "ADSLOGDINT";

        public object Invoke(NativeCallContext context)
        {
            // Narrowed to int because DINT is int in the value model: recorded
            // as a long it would not match an AssertEquals_DINT on the way back
            // out.
            AdsLogSink.Record(context, context.RequireInt32("dintArg", 2));
            return 0;
        }
    }

    public sealed class AdsLogLrealFunction : IXstunitNativeFunction
    {
        public string Name => "ADSLOGLREAL";

        public object Invoke(NativeCallContext context)
        {
            AdsLogSink.Record(context, context.RequireReal("lrealArg", 2));
            return 0;
        }
    }

    // The read side. These are xStunit's own, NOT Tc2_System symbols: there is
    // nothing in the vendor library to read the event log back, because on a
    // real system the log is not the PLC's to read. They exist so a test can
    // assert that a POU logged what it should have, rather than merely that the
    // call did not fault.
    public sealed class AdsLogCountFunction : IXstunitNativeFunction
    {
        public string Name => "F_AdsLogCount";

        public object Invoke(NativeCallContext context) => AdsLogSink.Count;
    }

    // Returns 0 so a suite can call it as an expression where a statement won't
    // do; the value carries no meaning.
    public sealed class AdsLogClearFunction : IXstunitNativeFunction
    {
        public string Name => "F_AdsLogClear";

        public object Invoke(NativeCallContext context)
        {
            AdsLogSink.Clear();
            return 0;
        }
    }

    public sealed class AdsLogMaskFunction : IXstunitNativeFunction
    {
        public string Name => "F_AdsLogMask";

        public object Invoke(NativeCallContext context) => AdsLogSink.At(context).Mask;
    }

    public sealed class AdsLogFormatFunction : IXstunitNativeFunction
    {
        public string Name => "F_AdsLogFormat";

        public object Invoke(NativeCallContext context) => AdsLogSink.At(context).Format;
    }

    public sealed class AdsLogStringArgFunction : IXstunitNativeFunction
    {
        public string Name => "F_AdsLogStringArg";

        public object Invoke(NativeCallContext context) => AdsLogSink.ArgumentAs<string>(context);
    }

    public sealed class AdsLogDintArgFunction : IXstunitNativeFunction
    {
        public string Name => "F_AdsLogDintArg";

        public object Invoke(NativeCallContext context) => AdsLogSink.ArgumentAs<int>(context);
    }

    public sealed class AdsLogRealArgFunction : IXstunitNativeFunction
    {
        public string Name => "F_AdsLogRealArg";

        public object Invoke(NativeCallContext context) => AdsLogSink.ArgumentAs<double>(context);
    }
}
