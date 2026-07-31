using xStunit.Interpreter.Extensibility;

namespace xStunit.SamplePlugins
{
    // A native function over plain scalars rather than a byte buffer, and the
    // second registration in this assembly - one plugin may export many.
    //
    // F_DemoUpperCase is an invented name, deliberately: an example of the
    // non-pointer shape should not imply anything about how any particular
    // Beckhoff library function behaves. Compare CheckSum16Function, which
    // borrows a real name and so has to warn that its algorithm is a stand-in.
    public sealed class DemoUpperCaseFunction : IXstunitNativeFunction
    {
        public string Name => "F_DemoUpperCase";

        public object Invoke(NativeCallContext context)
        {
            // STRING is CLR string in the interpreter's value model, so
            // neither the argument nor the return value needs conversion.
            var text = context.RequireString("sIn", 0);
            return text.ToUpperInvariant();
        }
    }
}
