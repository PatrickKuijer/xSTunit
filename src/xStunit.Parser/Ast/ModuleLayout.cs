using System.Collections.Generic;

namespace xStunit.Parser
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
            bool isStatic,
            IReadOnlyList<DeclaredArrayDimension> arrayDimensions,
            int? bitSize,
            int? bitSizeX64,
            int? bitOffset)
        {
            Name = name;
            TypeName = typeName;
            IsPointer = isPointer;
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
        /// pointer or array member this is the pointed-to/element type;
        /// <see cref="IsPointer"/> and <see cref="ArrayDimensions"/> carry the
        /// rest.
        /// </summary>
        public string TypeName { get; }

        public bool IsPointer { get; }

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
            bool isEnum,
            bool isFunctionBlock,
            IReadOnlyList<DeclaredMemberLayout> members)
        {
            Name = name;
            BitSize = bitSize;
            BaseTypeName = baseTypeName;
            BaseTypeIsPointer = baseTypeIsPointer;
            IsEnum = isEnum;
            IsFunctionBlock = isFunctionBlock;
            Members = members;
        }

        public string Name { get; }

        /// <summary>Size in bits, or null when the file states none.</summary>
        public int? BitSize { get; }

        /// <summary>
        /// The underlying type this one is built on: an enum's base integer
        /// type, or an alias's target. Null when the type stands alone.
        /// </summary>
        public string BaseTypeName { get; }

        /// <summary>
        /// True when the type aliases a POINTER TO <see cref="BaseTypeName"/>
        /// rather than the base type itself - which is what TwinCAT's handle
        /// types are.
        /// </summary>
        public bool BaseTypeIsPointer { get; }

        public bool IsEnum { get; }

        /// <summary>
        /// True when the type is a function block instance rather than a plain
        /// struct. Its members start past whatever instance header the compiler
        /// puts at offset 0, which is not modeled anywhere in xStunit.
        /// </summary>
        public bool IsFunctionBlock { get; }

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
