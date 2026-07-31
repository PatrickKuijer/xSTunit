using xStunit.Interpreter.Extensibility;

namespace xStunit.StandardStringPlugins
{
    // One Tc2_Standard string-function body, evaluated under a given character
    // measure. See StringOperations for the bodies themselves.
    public delegate object StringOperation(NativeCallContext context, CharacterMeasure measure);

    // A Tc2_Standard string function: a name, a shared body, and the character
    // measure the body counts and slices with. Narrow and wide are two
    // registrations over one body, so a fix to a function is made once and the
    // only genuine difference between the halves stays in the measure.
    //
    // Abstract on purpose: the plugin loader only instantiates exported,
    // non-abstract types with a parameterless constructor
    // (Cli/Plugins/NativeFunctionPluginLoader.cs), so it picks up the concrete
    // registrations below without mistaking this base type for a function of
    // its own.
    public abstract class StringFunction : IXstunitNativeFunction
    {
        private readonly StringOperation _operation;
        private readonly CharacterMeasure _measure;

        protected StringFunction(string name, StringOperation operation, CharacterMeasure measure)
        {
            Name = name;
            _operation = operation;
            _measure = measure;
        }

        public string Name { get; }

        public object Invoke(NativeCallContext context) => _operation(context, _measure);
    }

    // Each pair below is the same body twice, once counted the narrow way and
    // once the wide way. WCONCAT is absent on purpose: narrow CONCAT is an
    // interpreter intrinsic rather than a plugin function, so WCONCAT has no
    // counterpart to pair with and keeps its own file (WConcatFunction.cs).

    public sealed class DeleteFunction : StringFunction
    {
        public DeleteFunction() : base("DELETE", StringOperations.Delete, CharacterMeasure.Narrow) { }
    }

    public sealed class WDeleteFunction : StringFunction
    {
        public WDeleteFunction() : base("WDELETE", StringOperations.Delete, CharacterMeasure.Wide) { }
    }

    public sealed class FindFunction : StringFunction
    {
        public FindFunction() : base("FIND", StringOperations.Find, CharacterMeasure.Narrow) { }
    }

    public sealed class WFindFunction : StringFunction
    {
        public WFindFunction() : base("WFIND", StringOperations.Find, CharacterMeasure.Wide) { }
    }

    public sealed class InsertFunction : StringFunction
    {
        public InsertFunction() : base("INSERT", StringOperations.Insert, CharacterMeasure.Narrow) { }
    }

    public sealed class WInsertFunction : StringFunction
    {
        public WInsertFunction() : base("WINSERT", StringOperations.Insert, CharacterMeasure.Wide) { }
    }

    public sealed class LeftFunction : StringFunction
    {
        public LeftFunction() : base("LEFT", StringOperations.Left, CharacterMeasure.Narrow) { }
    }

    public sealed class WLeftFunction : StringFunction
    {
        public WLeftFunction() : base("WLEFT", StringOperations.Left, CharacterMeasure.Wide) { }
    }

    public sealed class LenFunction : StringFunction
    {
        public LenFunction() : base("LEN", StringOperations.Len, CharacterMeasure.Narrow) { }
    }

    public sealed class WLenFunction : StringFunction
    {
        public WLenFunction() : base("WLEN", StringOperations.Len, CharacterMeasure.Wide) { }
    }

    public sealed class MidFunction : StringFunction
    {
        public MidFunction() : base("MID", StringOperations.Mid, CharacterMeasure.Narrow) { }
    }

    public sealed class WMidFunction : StringFunction
    {
        public WMidFunction() : base("WMID", StringOperations.Mid, CharacterMeasure.Wide) { }
    }

    public sealed class ReplaceFunction : StringFunction
    {
        public ReplaceFunction() : base("REPLACE", StringOperations.Replace, CharacterMeasure.Narrow) { }
    }

    public sealed class WReplaceFunction : StringFunction
    {
        public WReplaceFunction() : base("WREPLACE", StringOperations.Replace, CharacterMeasure.Wide) { }
    }

    public sealed class RightFunction : StringFunction
    {
        public RightFunction() : base("RIGHT", StringOperations.Right, CharacterMeasure.Narrow) { }
    }

    public sealed class WRightFunction : StringFunction
    {
        public WRightFunction() : base("WRIGHT", StringOperations.Right, CharacterMeasure.Wide) { }
    }
}
