namespace xStunit.Interpreter.Conformance
{
    internal enum LayoutFindingKind
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
    internal sealed class LayoutFinding
    {
        private LayoutFinding(
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

        // One factory per kind, because which of the six fields carry a value
        // is fixed by the kind: a size disagreement has no detail, and a skip
        // has nothing to compare.
        public static LayoutFinding TypeSize(string typeName, int? declaredBits, int computedBits) =>
            new LayoutFinding(LayoutFindingKind.TypeSize, typeName, null, declaredBits, computedBits, null);

        public static LayoutFinding MemberOffset(string typeName, string memberName, int? declaredBits, int computedBits) =>
            new LayoutFinding(LayoutFindingKind.MemberOffset, typeName, memberName, declaredBits, computedBits, null);

        public static LayoutFinding MemberSize(string typeName, string memberName, int? declaredBits, int computedBits) =>
            new LayoutFinding(LayoutFindingKind.MemberSize, typeName, memberName, declaredBits, computedBits, null);

        public static LayoutFinding Unsupported(string typeName, string memberName, string detail) =>
            new LayoutFinding(LayoutFindingKind.Unsupported, typeName, memberName, null, null, detail);

        public static LayoutFinding NotCompared(string typeName, string memberName, string reason) =>
            new LayoutFinding(LayoutFindingKind.NotCompared, typeName, memberName, null, null, reason);

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
