using System.Collections.Generic;

namespace xStunit.Interpreter.Extensibility
{
    /// <summary>
    /// Supplies the behavior of one stateful FUNCTION_BLOCK the interpreter can never
    /// have source for - the FB-shaped sibling of <see cref="IXstunitNativeFunction"/>.
    /// </summary>
    /// <remarks>
    /// A function is arguments in, value out. A vendor library FB is not: it carries
    /// state from one PLC cycle to the next, publishes VAR_OUTPUTs a later cycle reads
    /// back, and usually runs a bExecute/bBusy/bError handshake spanning several
    /// invocations. Neither the argument-shaped call context nor the pure-value return
    /// of <see cref="IXstunitNativeFunction"/> can express that.
    /// <para>
    /// One registered implementation plays two roles. The instance handed to
    /// <see cref="NativeFunctionBlockRegistry.Register"/> is the PROTOTYPE: only its
    /// metadata (<see cref="TypeName"/>, <see cref="Fields"/>,
    /// <see cref="PositionalInputNames"/>, <see cref="MethodNames"/>) is ever read, and
    /// that metadata must describe the type rather than any one instance's state.
    /// <see cref="CreateInstance"/> then produces the per-FB-field state object the
    /// interpreter actually drives, one per declared variable of this type, and only
    /// those receive <see cref="Invoke"/>.
    /// </para>
    /// <para>
    /// Consulted at the same point in dispatch as a native function - last. A plugin FB
    /// type name is only reached when the ancestry walk leaves the TypeRegistry without
    /// finding source, and the in-tree stubs (TON/R_TRIG/RS/CTU/...) are matched first,
    /// so a plugin can never shadow interpreted source or an in-tree stub.
    /// </para>
    /// </remarks>
    public interface IXstunitNativeFunctionBlock
    {
        /// <summary>
        /// The ST type name this stands in for, as written in PLC source (e.g.
        /// "FB_FileOpen"). Matched case-insensitively, since IEC 61131-3 identifiers
        /// are.
        /// </summary>
        string TypeName { get; }

        /// <summary>
        /// Every VAR_INPUT/VAR_OUTPUT slot this FB reads or writes, which the
        /// interpreter allocates on each instance before the first invocation.
        /// </summary>
        /// <remarks>
        /// The complete set, not just the outputs: an input the caller never passes
        /// still has to exist for the FB to read a default out of, and for ST to assign
        /// to by dot access. Declaration order is documentation only - positional
        /// binding follows <see cref="PositionalInputNames"/>.
        /// </remarks>
        IReadOnlyList<NativeFieldDeclaration> Fields { get; }

        /// <summary>
        /// The VAR_INPUT names a bare invocation's positional arguments bind to, in IEC
        /// declaration order.
        /// </summary>
        /// <remarks>
        /// <c>fb(TRUE, t#1s)</c> assigns the first two names here; <c>fb(bExecute := x)</c>
        /// binds by name and ignores this list. An input left out of both keeps whatever
        /// the instance already held, which is what lets a caller re-trigger with only
        /// bExecute. Empty for an FB that is only ever called through
        /// <see cref="MethodNames"/>.
        /// </remarks>
        IReadOnlyList<string> PositionalInputNames { get; }

        /// <summary>
        /// The METHOD names this FB answers, e.g. FB_IecCriticalSection's Enter/Leave.
        /// </summary>
        /// <remarks>
        /// Declared rather than discovered so the interpreter can route a call it knows
        /// belongs here and leave every other name to normal dispatch. Without that the
        /// FB_init the interpreter speculatively calls on every new instance would
        /// arrive at <see cref="Invoke"/> as if it were part of the contract. Matched
        /// case-insensitively. Empty for a purely bare-invoked FB.
        /// </remarks>
        IReadOnlyList<string> MethodNames { get; }

        /// <summary>
        /// Builds the per-variable state object the interpreter drives.
        /// </summary>
        /// <returns>
        /// A fresh instance sharing no mutable state with the prototype or with any
        /// earlier instance. Two variables of the same plugin FB type are two
        /// independent FBs, exactly as in a real PLC; returning <c>this</c>, or anything
        /// holding a reference to shared mutable state, silently couples them.
        /// </returns>
        IXstunitNativeFunctionBlock CreateInstance();

        /// <summary>
        /// Runs one invocation: a bare <c>fb(...)</c> call, or one of
        /// <see cref="MethodNames"/>.
        /// </summary>
        /// <param name="call">
        /// Which call this is, plus read/write access to this instance's own fields. For
        /// a bare invocation the interpreter has already bound the caller's arguments
        /// into those fields, so <see cref="NativeFunctionBlockCall.Arguments"/> is empty
        /// and the inputs are read with <see cref="NativeFunctionBlockCall.GetField"/>.
        /// </param>
        /// <returns>
        /// The method's return value in the interpreter's CLR representation (see
        /// <see cref="IXstunitNativeFunction.Invoke"/>), or null for a bare invocation
        /// and for a method declared without one.
        /// </returns>
        /// <remarks>
        /// Throwing is a legitimate outcome: the exception surfaces through the normal
        /// interpreter fault path, attributed to the PLC call site.
        /// </remarks>
        object Invoke(NativeFunctionBlockCall call);
    }
}
