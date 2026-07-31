using xStunit.Interpreter.Extensibility;

namespace xStunit.SamplePlugins
{
    // Second sample (TcXunit-6k2), showing the shape of a native function that
    // takes and returns plain scalars rather than walking a byte buffer - and
    // proving the registry dispatches more than one function from one plugin
    // assembly.
    //
    // F_DemoUpperCase is an invented name, not a real vendor function. That is
    // deliberate: an example of the non-pointer shape shouldn't imply anything
    // about how any particular Beckhoff library function behaves. Compare
    // CheckSum16Function, which borrows a real name and therefore carries a
    // prominent warning that its algorithm is a stand-in.
    public sealed class DemoUpperCaseFunction : IXstunitNativeFunction
    {
        public string Name => "F_DemoUpperCase";

        public object Invoke(NativeCallContext context)
        {
            // STRING maps to CLR string in the interpreter's value model, so
            // this needs no conversion on the way in or out.
            var text = context.RequireString("sIn", 0);
            return text.ToUpperInvariant();
        }
    }
}
