namespace xStunit.Interpreter.Conformance
{
    public enum LayoutFindingKind
    {
        // The whole type's size differs.
        TypeSize,

        // A member sits at a different offset than the compiler put it at.
        MemberOffset,

        // A member occupies a different number of bits.
        MemberSize,

        // xStunit's layout math refused the type outright - an unknown type
        // name, an unresolvable bound. A gap in the math, not a disagreement
        // about a rule.
        Unsupported,

        // The declared type was left out of the comparison, for a reason
        // xStunit knows in advance it cannot answer.
        NotCompared,
    }

    // One disagreement between a compiler-declared layout and xStunit's own, or
    // one declared type the comparison could not reach. Sizes and offsets are
    // in BITS, the unit the .tmc states them in.
    public sealed class LayoutFinding
    {
        public LayoutFinding(
            LayoutFindingKind kind,
            string typeName,
            string memberName,
            int? declaredBits,
            int? computedBits,
            string detail)
        {
            Kind = kind;
            TypeName = typeName;
            MemberName = memberName;
            DeclaredBits = declaredBits;
            ComputedBits = computedBits;
            Detail = detail;
        }

        public LayoutFindingKind Kind { get; }

        public string TypeName { get; }

        // Null for a finding about the type as a whole.
        public string MemberName { get; }

        // What the TwinCAT compiler put in the .tmc, and what xStunit's layout
        // math computes for the same thing. Both null on a finding that is not
        // a size/offset disagreement.
        public int? DeclaredBits { get; }

        public int? ComputedBits { get; }

        // Why the comparison could not be made, on an Unsupported or
        // NotCompared finding; null otherwise.
        public string Detail { get; }

        public string Subject => MemberName == null ? TypeName : $"{TypeName}.{MemberName}";
    }
}
