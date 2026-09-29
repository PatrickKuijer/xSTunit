using System.Collections.Generic;

namespace xStunit.Interpreter.Tests.Conformance
{
    /// <summary>
    /// One dimension of a declared array member: its lower bound and how many
    /// elements it holds.
    /// </summary>
    public readonly struct DeclaredArrayDimension
    {
        public DeclaredArrayDimension(int lowerBound, int elementCount)
        {
            LowerBound = lowerBound;
            ElementCount = elementCount;
        }

        public int LowerBound { get; }

        public int ElementCount { get; }
    }

    /// <summary>
    /// One member of a declared type, with the bit offset and bit size the
    /// TwinCAT compiler gave it.
    /// </summary>
    /// <remarks>
    /// Sizes and offsets stay in BITS, as the file states them: a BOOL occupies
    /// 8 of them and a bit-packed member need not start on a byte boundary, so
    /// converting to bytes here would lose the cases a layout oracle most wants
    /// to see.
    /// </remarks>
    public sealed class DeclaredMemberLayout
    {
        public DeclaredMemberLayout(
            string name,
            string typeName,
            bool isPointer,
            bool isReference,
            bool isStatic,
            IReadOnlyList<DeclaredArrayDimension> arrayDimensions,
            int? bitSize,
            int? bitSizeX64,
            int? bitOffset)
        {
            Name = name;
            TypeName = typeName;
            IsPointer = isPointer;
            IsReference = isReference;
            IsStatic = isStatic;
            ArrayDimensions = arrayDimensions;
            BitSize = bitSize;
            BitSizeX64 = bitSizeX64;
            BitOffset = bitOffset;
        }

        public string Name { get; }

        /// <summary>
        /// The member's type as the file spells it - an elementary type, a
        /// sized <c>STRING(n)</c>, or another declared type's name. For a
        /// pointer, reference or array member this is the
        /// pointed-to/referred-to/element type; <see cref="IsPointer"/>,
        /// <see cref="IsReference"/> and <see cref="ArrayDimensions"/> carry
        /// the rest.
        /// </summary>
        public string TypeName { get; }

        public bool IsPointer { get; }

        /// <summary>
        /// True for a <c>REFERENCE TO</c> member. Laid out as an address, the
        /// same as <see cref="IsPointer"/>, but kept apart from it because the
        /// two are different declarations and only one of them is assignable
        /// through in ST.
        /// </summary>
        public bool IsReference { get; }

        /// <summary>
        /// True for a member the compiler marked static - what a PROGRAM's or
        /// GVL's entries are, as opposed to a struct's or function block's
        /// instance fields.
        /// </summary>
        public bool IsStatic { get; }

        /// <summary>Empty for a non-array member; one entry per dimension otherwise.</summary>
        public IReadOnlyList<DeclaredArrayDimension> ArrayDimensions { get; }

        /// <summary>Size in bits, or null when the file states none.</summary>
        public int? BitSize { get; }

        /// <summary>
        /// Size in bits on a 64-bit target, from the <c>X64</c> attribute the
        /// file puts on a member whose width differs between targets - a
        /// pointer or an interface reference. Null when the member is the same
        /// width everywhere.
        /// </summary>
        public int? BitSizeX64 { get; }

        /// <summary>
        /// Offset in bits from the start of the containing type, or null when
        /// the file states none - which is what a PROGRAM's or GVL's entries
        /// look like, having no containing instance to be offset within.
        /// </summary>
        public int? BitOffset { get; }
    }

    /// <summary>
    /// One data type the TwinCAT compiler emitted, with its overall size and
    /// its members' placements.
    /// </summary>
    public sealed class DeclaredTypeLayout
    {
        public DeclaredTypeLayout(
            string name,
            int? bitSize,
            string baseTypeName,
            bool baseTypeIsPointer,
            IReadOnlyList<DeclaredArrayDimension> arrayDimensions,
            bool isFunctionBlock,
            int? packMode,
            IReadOnlyList<DeclaredMemberLayout> members)
        {
            Name = name;
            BitSize = bitSize;
            BaseTypeName = baseTypeName;
            BaseTypeIsPointer = baseTypeIsPointer;
            ArrayDimensions = arrayDimensions;
            IsFunctionBlock = isFunctionBlock;
            PackMode = packMode;
            Members = members;
        }

        public string Name { get; }

        /// <summary>Size in bits, or null when the file states none.</summary>
        public int? BitSize { get; }

        /// <summary>
        /// The underlying type this one is built on: an enum's base integer
        /// type, or an alias's target. Null when the type stands alone. A type
        /// declaring this and no members is one or the other - which of the two
        /// makes no difference to its layout.
        /// </summary>
        public string BaseTypeName { get; }

        /// <summary>
        /// True when the type aliases a POINTER TO <see cref="BaseTypeName"/>
        /// rather than the base type itself - which is what TwinCAT's handle
        /// types are.
        /// </summary>
        public bool BaseTypeIsPointer { get; }

        /// <summary>
        /// Empty unless the type is an array OF <see cref="BaseTypeName"/> -
        /// what an array type alias looks like; one entry per dimension.
        /// </summary>
        public IReadOnlyList<DeclaredArrayDimension> ArrayDimensions { get; }

        /// <summary>
        /// True when the type is a function block instance rather than a plain
        /// struct. Its members start past whatever instance header the compiler
        /// puts at offset 0, which is not modeled anywhere in xStunit.
        /// </summary>
        public bool IsFunctionBlock { get; }

        /// <summary>
        /// The <c>{attribute 'pack_mode' := 'N'}</c> cap the type was compiled
        /// with, in bytes; null when the type carries no pragma and every member
        /// keeps its natural alignment. An explicit 0 is a pragma in its own
        /// right, packing without gaps like 1.
        /// </summary>
        /// <remarks>
        /// The compiler writes the pragma into the file alongside the layout it
        /// produced, so a reader need not consult the ST source to know a type
        /// was packed. Without it, a packed type read back as naturally aligned
        /// disagrees with its own declared offsets.
        /// </remarks>
        public int? PackMode { get; }

        public IReadOnlyList<DeclaredMemberLayout> Members { get; }
    }

    /// <summary>
    /// The byte layout a TwinCAT compiler declared for one PLC module: every
    /// data type it emitted, plus the target platform those sizes are for.
    /// </summary>
    public sealed class ModuleLayout
    {
        public ModuleLayout(string moduleName, string targetPlatform, IReadOnlyList<DeclaredTypeLayout> types)
        {
            ModuleName = moduleName;
            TargetPlatform = targetPlatform;
            Types = types;
        }

        public string ModuleName { get; }

        /// <summary>
        /// The target the module was compiled for, verbatim - e.g.
        /// <c>TwinCAT RT (x64)</c>. Null when the file declares no module.
        /// </summary>
        /// <remarks>
        /// Decides which of a member's two widths applies, so a consumer that
        /// ignores it will read 32-bit pointer sizes out of a 64-bit build.
        /// </remarks>
        public string TargetPlatform { get; }

        public IReadOnlyList<DeclaredTypeLayout> Types { get; }
    }
}
