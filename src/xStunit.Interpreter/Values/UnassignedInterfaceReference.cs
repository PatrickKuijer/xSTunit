using System;

namespace xStunit.Interpreter
{
    // What a VAR of a declared interface type holds until something assigns an
    // implementing instance into it. On the target that variable is a null
    // pointer: harmless to hold and to test against 0, and a fault the moment
    // anything is read or called through it. This stands in for both halves -
    // it compares equal to literal 0, and every dereference site turns it into
    // a named fault instead of the silent integer 0 an unknown type name still
    // defaults to.
    public sealed class UnassignedInterfaceReference
    {
        public UnassignedInterfaceReference(string interfaceTypeName)
        {
            InterfaceTypeName = interfaceTypeName;
        }

        public string InterfaceTypeName { get; }

        // Names the interface and what was reached for, because the two
        // together are what tell the reader which injection they forgot: the
        // member name alone appears on the implementing FB too.
        public InvalidOperationException Fault(string memberName) =>
            new InvalidOperationException(
                $"'{memberName}' was accessed through an unassigned '{InterfaceTypeName}' reference. " +
                "Assign an instance implementing the interface before using it.");
    }
}
