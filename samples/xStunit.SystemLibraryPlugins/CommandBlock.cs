using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Interpreter.Extensibility;

namespace xStunit.SystemLibraryPlugins
{
    // Raised by an operation that cannot be performed, and turned into the
    // block's own error outputs by CommandBlock. An exception rather than a
    // return code because every failure has to reach the same two fields, and a
    // returned code invites a caller to set one and forget the other.
    internal sealed class CommandFailedException : Exception
    {
        public CommandFailedException(long errorId, string message)
            : base(message)
        {
            ErrorId = errorId;
        }

        public long ErrorId { get; }
    }

    // The trigger/busy/error handshake shared by every Tc2_System block that
    // models an asynchronous command - the file family and the ADS family
    // alike. Only the FIELD NAMES differ between them (bExecute/bBusy/bError
    // vs READ/BUSY/ERR), which is why they are asked for rather than assumed.
    //
    // THE TIMING MODEL, which is the thing to understand before writing a test
    // against any of these: the invocation carrying the RISING EDGE of the
    // trigger starts the command and reports busy having done nothing else; a
    // LATER invocation performs it, publishes the outputs and clears busy. One
    // cycle of latency at minimum, never zero.
    //
    // Zero would be simpler and is wrong for the code under test. A real POU
    // driving one of these is a state machine, and the common shapes include
    // "trigger, then move on once I have seen busy" - which an
    // instantly-complete block never satisfies, so such a POU would hang in a
    // test while working perfectly on a PLC. One cycle satisfies every shape: a
    // state machine that instead waits for busy to fall sees it fall on the
    // second invocation, whether it holds the trigger high or drops it.
    //
    // More than one cycle only happens when Execute asks for it by returning
    // false, which is how a scripted ADS timeout stays busy until the simulated
    // clock has passed TMOUT. An arbitrary fixed latency would be a fiction
    // every test then had to encode.
    //
    // Public because its subclasses are: the plugin loader reflects over
    // exported types, and C# will not let a public class derive from an
    // internal base. Abstract, so the loader's own filter skips it.
    public abstract class CommandBlock : IXstunitNativeFunctionBlock
    {
        private bool _lastTrigger;
        private bool _commandPending;

        public abstract string TypeName { get; }

        public abstract IReadOnlyList<NativeFieldDeclaration> Fields { get; }

        public abstract IReadOnlyList<string> PositionalInputNames { get; }

        // None of these blocks has a METHOD; they are driven cyclically.
        public IReadOnlyList<string> MethodNames => Array.Empty<string>();

        public abstract IXstunitNativeFunctionBlock CreateInstance();

        protected abstract string TriggerFieldName { get; }

        protected abstract string BusyFieldName { get; }

        protected abstract string ErrorFieldName { get; }

        protected abstract string ErrorIdFieldName { get; }

        protected static NativeFieldDeclaration[] FieldsOf(params IEnumerable<NativeFieldDeclaration>[] groups) =>
            groups.SelectMany(g => g).ToArray();

        public object Invoke(NativeFunctionBlockCall call)
        {
            if (!call.IsBareInvocation)
            {
                throw new InvalidOperationException(
                    $"{TypeName} has no methods - drive it cyclically with {TriggerFieldName}");
            }

            var trigger = Convert.ToBoolean(call.GetField(TriggerFieldName));

            if (trigger && !_lastTrigger)
            {
                // Outputs from the previous command are cleared here rather
                // than left standing, so a POU cannot read a stale error from
                // two commands ago and call it this one's.
                _commandPending = true;
                call.SetField(BusyFieldName, true);
                call.SetField(ErrorFieldName, false);
                call.SetField(ErrorIdFieldName, 0L);
                OnStarted(call);
            }
            else if (_commandPending && TryComplete(call))
            {
                _commandPending = false;
                call.SetField(BusyFieldName, false);
            }

            _lastTrigger = trigger;
            return null;
        }

        // A failure completes the command: the block reports it and stops being
        // busy, rather than retrying forever on its own.
        private bool TryComplete(NativeFunctionBlockCall call)
        {
            try
            {
                return Execute(call);
            }
            catch (CommandFailedException ex)
            {
                call.SetField(ErrorFieldName, true);
                call.SetField(ErrorIdFieldName, ex.ErrorId);
                OnFailed(call);
                return true;
            }
        }

        // Returns false to stay busy and be asked again on the next
        // invocation, which is how a scripted timeout waits.
        protected abstract bool Execute(NativeFunctionBlockCall call);

        // Called on the invocation that starts the command, for a block that
        // has to remember when that was.
        protected virtual void OnStarted(NativeFunctionBlockCall call)
        {
        }

        // A block whose own outputs need a defined value after a failure - a
        // seek position that must read -1 rather than a stale offset - says so
        // here. Most need nothing.
        protected virtual void OnFailed(NativeFunctionBlockCall call)
        {
        }
    }
}
