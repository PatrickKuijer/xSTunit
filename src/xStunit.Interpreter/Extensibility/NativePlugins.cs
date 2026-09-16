namespace xStunit.Interpreter.Extensibility
{
    /// <summary>
    /// Everything a host can supply on behalf of the compiled-only libraries a tree
    /// references: functions, stateful function blocks, and named constants.
    /// </summary>
    /// <remarks>
    /// One object rather than three Engine constructor parameters, because the set
    /// grows: each extension point added to fill another hole in a vendor library would
    /// otherwise cost another overload on a constructor that already has several, and
    /// every host would have to be edited to pass a null through.
    /// <para>
    /// Each registry starts empty and is filled by the host. An Engine given a
    /// <see cref="NativePlugins"/> with nothing in it behaves exactly as one given none:
    /// every registry is consulted last, so registering is purely additive.
    /// </para>
    /// </remarks>
    public sealed class NativePlugins
    {
        /// <summary>Builds an empty set for a host to fill.</summary>
        public NativePlugins()
            : this(null, null, null)
        {
        }

        /// <summary>
        /// Wraps registries the host already built, for one that fills them before it
        /// has an Engine to give them to.
        /// </summary>
        /// <param name="functions">Adopted as-is, not copied, so its registration sources survive; null means an empty one.</param>
        /// <param name="functionBlocks">Likewise; null means an empty one.</param>
        /// <param name="constants">Likewise; null means an empty one.</param>
        public NativePlugins(
            NativeFunctionRegistry functions,
            NativeFunctionBlockRegistry functionBlocks = null,
            NativeConstantRegistry constants = null)
        {
            Functions = functions ?? new NativeFunctionRegistry();
            FunctionBlocks = functionBlocks ?? new NativeFunctionBlockRegistry();
            Constants = constants ?? new NativeConstantRegistry();
        }

        /// <summary>Stand-ins for compiled-only library FUNCTIONs.</summary>
        public NativeFunctionRegistry Functions { get; }

        /// <summary>Stand-ins for compiled-only, stateful library FUNCTION_BLOCKs.</summary>
        public NativeFunctionBlockRegistry FunctionBlocks { get; }

        /// <summary>The named values those libraries publish from their GVLs and ENUMs.</summary>
        public NativeConstantRegistry Constants { get; }
    }
}
