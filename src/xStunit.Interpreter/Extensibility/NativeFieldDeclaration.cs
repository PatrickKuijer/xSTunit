using System;

namespace xStunit.Interpreter.Extensibility
{
    /// <summary>
    /// One VAR_INPUT/VAR_OUTPUT slot a native function block expects to find
    /// already allocated on its owning instance before its first invocation.
    /// </summary>
    /// <remarks>
    /// A native stub publishes its outputs by writing into the owning instance's
    /// fields, and ST reads them straight back by dot access. Nothing allocates those
    /// fields on demand: a stub writing to a name nothing seeded fails at its first
    /// invocation, far from the declaration that forgot it. Declaring the whole field
    /// set up front is what makes that impossible.
    /// </remarks>
    public sealed class NativeFieldDeclaration
    {
        /// <param name="name">The field name as ST spells it (e.g. "bExecute"); matched case-sensitively, like every other instance field.</param>
        /// <param name="defaultValue">
        /// The value the field holds before anything assigns to it, in the interpreter's
        /// CLR representation for the field's IEC type (see
        /// <see cref="IXstunitNativeFunction.Invoke"/> for the mapping). An IEC variable
        /// is never uninitialized, so null here means a genuinely reference-shaped value
        /// (a POINTER slot), not "unset".
        /// </param>
        /// <param name="declaredTypeName">
        /// The IEC type text this field is declared with (e.g. "STRING(255)"), or null
        /// to leave the field untyped as the in-tree stubs' IN/PT/Q/ET are. Supplying it
        /// makes the field behave like a declared variable: a STRING(n) truncates at n,
        /// and AssertEquals(ANY) can name the type.
        /// </param>
        /// <exception cref="ArgumentException"><paramref name="name"/> is null or blank.</exception>
        public NativeFieldDeclaration(string name, object defaultValue, string declaredTypeName = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("a native field needs a name", nameof(name));

            Name = name;
            DefaultValue = defaultValue;
            DeclaredTypeName = declaredTypeName;
        }

        public string Name { get; }

        public object DefaultValue { get; }

        /// <summary>
        /// The IEC type text behind this field, or null when the field is untyped.
        /// </summary>
        public string DeclaredTypeName { get; }
    }
}
