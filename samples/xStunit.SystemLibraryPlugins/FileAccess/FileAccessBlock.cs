using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Interpreter.Extensibility;

namespace xStunit.SystemLibraryPlugins.FileAccess
{
    // The bExecute/bBusy/bError handshake every Tc2_System file block shares,
    // in one place.
    //
    // THE TIMING MODEL, which is the thing to understand before writing a test
    // against these: the invocation carrying the RISING EDGE of bExecute starts
    // the command and reports bBusy TRUE having done nothing else; the NEXT
    // invocation performs it, publishes the outputs and clears bBusy. One cycle
    // of latency, not zero.
    //
    // Zero would be simpler and is wrong for the code under test. A real POU
    // driving one of these is a state machine, and the common shapes include
    // "trigger, then move on once I have seen bBusy" - which an
    // instantly-complete block never satisfies, so such a POU would hang in a
    // test while working perfectly on a PLC. One cycle satisfies every shape:
    // a state machine that instead waits for bBusy to fall sees it fall on the
    // second invocation, whether it holds bExecute high or drops it.
    //
    // Longer would be arbitrary. There is no ADS round trip and no disk here,
    // so any particular number of cycles would be a fiction a test then had to
    // encode.
    //
    // Public because its subclasses are: the plugin loader reflects over
    // exported types, and C# will not let a public class derive from an
    // internal base. Abstract, so the loader's own filter skips it.
    public abstract class FileAccessBlock : IXstunitNativeFunctionBlock
    {
        private bool _lastExecute;
        private bool _commandPending;

        public abstract string TypeName { get; }

        // sNetId and tTimeout exist so real source binds to them - every one of
        // these blocks declares both - but neither has anything to act on: the
        // virtual filesystem is local, and nothing here can time out.
        protected static IEnumerable<NativeFieldDeclaration> CommonInputs => new[]
        {
            new NativeFieldDeclaration("sNetId", string.Empty, "T_MaxString"),
            new NativeFieldDeclaration("bExecute", false),
            new NativeFieldDeclaration("tTimeout", 0u),
        };

        protected static IEnumerable<NativeFieldDeclaration> CommonOutputs => new[]
        {
            new NativeFieldDeclaration("bBusy", false),
            new NativeFieldDeclaration("bError", false),
            new NativeFieldDeclaration("nErrId", 0L),
        };

        protected static NativeFieldDeclaration[] FieldsOf(params IEnumerable<NativeFieldDeclaration>[] groups) =>
            groups.SelectMany(g => g).ToArray();

        public abstract IReadOnlyList<NativeFieldDeclaration> Fields { get; }

        public abstract IReadOnlyList<string> PositionalInputNames { get; }

        // None of the file blocks has a METHOD; they are driven cyclically.
        public IReadOnlyList<string> MethodNames => Array.Empty<string>();

        public abstract IXstunitNativeFunctionBlock CreateInstance();

        public object Invoke(NativeFunctionBlockCall call)
        {
            if (!call.IsBareInvocation)
            {
                throw new InvalidOperationException(
                    $"{TypeName} has no methods - drive it cyclically with bExecute");
            }

            var execute = Convert.ToBoolean(call.GetField("bExecute"));

            if (execute && !_lastExecute)
            {
                // Outputs from the previous command are cleared here rather
                // than left standing, so a POU cannot read a stale bError from
                // two commands ago and call it this one's.
                _commandPending = true;
                call.SetField("bBusy", true);
                call.SetField("bError", false);
                call.SetField("nErrId", FileError.None);
            }
            else if (_commandPending)
            {
                _commandPending = false;
                call.SetField("bBusy", false);
                Perform(call);
            }

            _lastExecute = execute;
            return null;
        }

        // Runs the command. Every failure arrives here as a
        // FileAccessException, so no implementation has to remember to set both
        // bError and nErrId on every path of its own.
        private void Perform(NativeFunctionBlockCall call)
        {
            try
            {
                Execute(call);
            }
            catch (FileAccessException ex)
            {
                call.SetField("bError", true);
                call.SetField("nErrId", ex.ErrorId);
                OnFailed(call);
            }
        }

        protected abstract void Execute(NativeFunctionBlockCall call);

        // A block whose own outputs need a defined value after a failure - a
        // seek position that must read -1 rather than a stale offset - says so
        // here. Most need nothing.
        protected virtual void OnFailed(NativeFunctionBlockCall call)
        {
        }

        protected static int HandleOf(NativeFunctionBlockCall call) =>
            Convert.ToInt32(call.GetField("hFile"));

        protected static string PathOf(NativeFunctionBlockCall call, string fieldName) =>
            Convert.ToString(call.GetField(fieldName));
    }
}
