using System;
using System.Collections.Generic;
using xStunit.Interpreter.Extensibility;

namespace xStunit.SystemLibraryPlugins.FileAccess
{
    // The file family's share of the command handshake: CommandBlock owns the
    // trigger/busy/error machinery and the timing model, and this fixes the
    // field names and the common inputs and outputs every file block declares.
    //
    // Every file operation completes on the first invocation that attempts it,
    // so Execute here is void and the multi-cycle path CommandBlock allows is
    // never taken - there is no disk to wait for.
    public abstract class FileAccessBlock : CommandBlock
    {
        protected override string TriggerFieldName => "bExecute";

        protected override string BusyFieldName => "bBusy";

        protected override string ErrorFieldName => "bError";

        protected override string ErrorIdFieldName => "nErrId";

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

        protected sealed override bool Execute(NativeFunctionBlockCall call)
        {
            Perform(call);
            return true;
        }

        protected abstract void Perform(NativeFunctionBlockCall call);

        protected static int HandleOf(NativeFunctionBlockCall call) =>
            Convert.ToInt32(call.GetField("hFile"));

        protected static string PathOf(NativeFunctionBlockCall call, string fieldName) =>
            Convert.ToString(call.GetField(fieldName));
    }
}
