using System;
using System.Collections.Generic;
using xStunit.Interpreter.Extensibility;

namespace xStunit.SamplePlugins
{
    // The worked example of the STATEFUL half of the plugin surface, and the
    // shape nearly every vendor library FB has: a rising edge on bExecute
    // starts work that takes several PLC cycles, bBusy is true while it runs,
    // and exactly one of bDone/bError is raised when it finishes.
    //
    // FB_DemoHandshake is an invented name, deliberately - same reason as
    // F_DemoUpperCase. An example of the shape should not imply anything about
    // how any particular Beckhoff FB behaves.
    public sealed class DemoHandshakeBlock : IXstunitNativeFunctionBlock
    {
        // The whole reason this is not a native FUNCTION: the countdown has to
        // survive from one invocation to the next, and a suite observes the
        // block by stepping cycles rather than by reading a return value.
        private int _remaining;
        private bool _lastExecute;

        public string TypeName => "FB_DemoHandshake";

        // Every slot ST touches, inputs included. Defaults are the CLR shapes
        // the interpreter uses for BOOL and INT.
        public IReadOnlyList<NativeFieldDeclaration> Fields => new[]
        {
            new NativeFieldDeclaration("bExecute", false),
            new NativeFieldDeclaration("nCycles", 0),
            new NativeFieldDeclaration("bBusy", false),
            new NativeFieldDeclaration("bDone", false),
            new NativeFieldDeclaration("sResult", string.Empty, "STRING(32)"),
        };

        public IReadOnlyList<string> PositionalInputNames => new[] { "bExecute", "nCycles" };

        public IReadOnlyList<string> MethodNames => new[] { "Abort" };

        // A fresh countdown per declared variable. Returning `this` would make
        // every FB_DemoHandshake in a tree the same block.
        public IXstunitNativeFunctionBlock CreateInstance() => new DemoHandshakeBlock();

        public object Invoke(NativeFunctionBlockCall call)
        {
            if (!call.IsBareInvocation)
                return Abort(call);

            var execute = Convert.ToBoolean(call.GetField("bExecute"));

            // Edge-triggered, not level-triggered: holding bExecute true must
            // not restart the command every cycle, which is what the vendor
            // handshake means by "a rising edge activates the block".
            if (execute && !_lastExecute)
            {
                _remaining = Math.Max(1, Convert.ToInt32(call.GetField("nCycles")));
                call.SetField("bDone", false);
                call.SetField("sResult", string.Empty);
            }

            _lastExecute = execute;

            if (_remaining > 0)
            {
                _remaining--;
                if (_remaining == 0)
                {
                    call.SetField("bDone", true);
                    call.SetField("sResult", "done");
                }
            }

            call.SetField("bBusy", _remaining > 0);
            return null;
        }

        // Returns whether there was anything to abort, so a suite can tell a
        // cancelled command from one that had already finished.
        private object Abort(NativeFunctionBlockCall call)
        {
            var wasRunning = _remaining > 0;

            _remaining = 0;
            call.SetField("bBusy", false);
            call.SetField("bDone", false);
            call.SetField("sResult", "aborted");

            return wasRunning;
        }
    }
}
