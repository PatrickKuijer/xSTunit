using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Parser;

namespace xStunit.Interpreter.Conformance
{
    // Diffs the byte layout a TwinCAT compiler declared in a .tmc against the
    // layout xStunit's own math computes for the same types.
    //
    // The .tmc is the measuring device: it states every member's offset and
    // size outright, so the declared types themselves supply the fixtures and
    // the comparison needs no TwinCAT installation, no ST source, and no
    // running PLC. Whatever types a project happens to contain is whatever the
    // run covers - see LayoutReport.ComparedMemberCount for how much that was.
    //
    // One rule this cannot reach: a .tmc records the layout a struct ended up
    // with, not the {attribute 'pack_mode'} pragma that produced it, so every
    // type is compared as if it were naturally aligned. A packed struct would
    // therefore be reported as a disagreement when xStunit is in fact right -
    // checking pack_mode needs the ST source alongside the .tmc.
    internal static class LayoutOracle
    {
        private const int BitsPerByte = 8;

        public static LayoutReport Compare(ModuleLayout module)
        {
            var useX64Widths = module.TargetPlatform != null &&
                module.TargetPlatform.IndexOf("x64", StringComparison.OrdinalIgnoreCase) >= 0;

            var registry = BuildRegistry(module);
            var layout = new TypeLayout(registry);

            var findings = new List<LayoutFinding>();
            var comparedTypes = 0;
            var comparedMembers = 0;

            foreach (var type in module.Types)
            {
                var skipReason = SkipReason(type);
                if (skipReason != null)
                {
                    findings.Add(LayoutFinding.NotCompared(type.Name, skipReason));
                    continue;
                }

                var compared = type.Members.Count == 0
                    ? CompareTypeSize(layout, type, findings)
                    : CompareStruct(layout, registry, type, useX64Widths, findings);
                if (compared.Reached)
                    comparedTypes++;
                comparedMembers += compared.Members;
            }

            return new LayoutReport(
                module.ModuleName, module.TargetPlatform, module.Types.Count, comparedTypes, comparedMembers, findings);
        }

        // Enums and aliases carry no members of their own, so all there is to
        // check is that resolving to the declared base type gives the compiler's
        // own size - which is the enum-base-width rule.
        private static Comparison CompareTypeSize(TypeLayout layout, DeclaredTypeLayout type, List<LayoutFinding> findings)
        {
            int computedBits;
            try
            {
                computedBits = layout.SizeOf(type.Name).Size * BitsPerByte;
            }
            catch (Exception ex) when (IsLayoutRefusal(ex))
            {
                findings.Add(LayoutFinding.Unsupported(type.Name, null, ex.Message));
                return new Comparison(false, 0);
            }

            if (type.BitSize != computedBits)
                findings.Add(LayoutFinding.TypeSize(type.Name, type.BitSize, computedBits));

            return new Comparison(true, 0);
        }

        private static Comparison CompareStruct(
            TypeLayout layout,
            TypeRegistry registry,
            DeclaredTypeLayout type,
            bool useX64Widths,
            List<LayoutFinding> findings)
        {
            var structAst = registry.GetStruct(type.Name);
            var (placements, failedIndex, failureDetail) = PlaceFields(layout, structAst);

            for (var i = 0; i < placements.Count; i++)
            {
                var member = type.Members[i];
                var placement = placements[i];

                var declaredOffset = member.BitOffset;
                var computedOffset = placement.Offset * BitsPerByte;
                if (declaredOffset != computedOffset)
                    findings.Add(LayoutFinding.MemberOffset(type.Name, member.Name, declaredOffset, computedOffset));

                var declaredSize = DeclaredBits(member, useX64Widths);
                var computedSize = placement.Size * BitsPerByte;
                if (declaredSize != computedSize)
                    findings.Add(LayoutFinding.MemberSize(type.Name, member.Name, declaredSize, computedSize));
            }

            if (failedIndex >= 0)
            {
                // Every member after the refused one would be placed at an
                // offset derived from a size xStunit could not compute, so the
                // type's remaining members and its overall size are not
                // reported at all rather than reported wrongly.
                findings.Add(LayoutFinding.Unsupported(type.Name, type.Members[failedIndex].Name, failureDetail));
                return new Comparison(false, placements.Count);
            }

            var computedTypeBits = layout.SizeOf(type.Name).Size * BitsPerByte;
            if (type.BitSize != computedTypeBits)
                findings.Add(LayoutFinding.TypeSize(type.Name, type.BitSize, computedTypeBits));

            return new Comparison(true, placements.Count);
        }

        // How much of a declared type the comparison actually got through:
        // whether the type came out fully compared, and how many of its members
        // were placed against the compiler's own offsets. Members are the
        // honest denominator - a module's type count is inflated by aliases and
        // enums, which are one size check each.
        private readonly struct Comparison
        {
            public Comparison(bool reached, int members)
            {
                Reached = reached;
                Members = members;
            }

            public bool Reached { get; }

            public int Members { get; }
        }

        // Placements up to the first field the layout math refuses, plus which
        // field that was. Naming the exact construct xStunit cannot size is the
        // point of the run, so the walk is driven by hand rather than through a
        // ToList() that would attribute the failure to the whole type.
        private static (IReadOnlyList<FieldPlacement> Placements, int FailedIndex, string FailureDetail) PlaceFields(
            TypeLayout layout, StructAst structAst)
        {
            var placements = new List<FieldPlacement>();
            using (var fields = layout.Fields(structAst).GetEnumerator())
            {
                while (true)
                {
                    try
                    {
                        if (!fields.MoveNext())
                            return (placements, -1, null);
                    }
                    catch (Exception ex) when (IsLayoutRefusal(ex))
                    {
                        return (placements, placements.Count, ex.Message);
                    }

                    placements.Add(fields.Current);
                }
            }
        }

        private static TypeRegistry BuildRegistry(ModuleLayout module)
        {
            var structs = module.Types
                .Where(type => SkipReason(type) == null && type.Members.Count > 0)
                .Select(ToStructAst);

            // Enums and aliases go in as ALIAS entries, which is how the
            // interpreter's own loaders register them: every layout lookup
            // resolves the name to its base type without knowing enums exist.
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var type in module.Types.Where(t => t.Name != null && t.Members.Count == 0 && t.BaseTypeName != null))
                aliases[type.Name] = AliasTarget(type);

            return new TypeRegistry(Array.Empty<PouAst>(), structs, null, aliases);
        }

        // TwinCAT's handle types (RTS_IEC_HANDLE and friends) alias a pointer,
        // not the pointed-to type, so the pointer has to survive into the alias
        // text or the alias resolves to the width of whatever it points at.
        private static string AliasTarget(DeclaredTypeLayout type) =>
            IecTypeName(type.BaseTypeName, type.BaseTypeIsPointer, type.ArrayDimensions);

        private static StructAst ToStructAst(DeclaredTypeLayout type) =>
            new StructAst(
                type.Name,
                type.Members
                    .Select(m => new VarDecl(
                        m.Name, IecTypeName(m.TypeName, m.IsPointer, m.ArrayDimensions), null, VarSection.Local))
                    .ToList());

        // The .tmc splits a declared type across a type name, a PointerTo
        // attribute and any number of ArrayInfo blocks - the same three pieces
        // whether it is describing a member or an array/handle type alias;
        // xStunit's layout math takes one piece of ST type text.
        private static string IecTypeName(
            string baseTypeName, bool isPointer, IReadOnlyList<DeclaredArrayDimension> dimensions)
        {
            var typeName = isPointer ? $"POINTER TO {baseTypeName}" : baseTypeName;
            if (dimensions.Count == 0)
                return typeName;

            var bounds = string.Join(
                ",", dimensions.Select(d => $"{d.LowerBound}..{d.LowerBound + d.ElementCount - 1}"));
            return $"ARRAY[{bounds}] OF {typeName}";
        }

        // A member whose width differs between targets carries both; which one
        // is real depends on what the module was compiled for.
        private static int? DeclaredBits(DeclaredMemberLayout member, bool useX64Widths) =>
            useX64Widths && member.BitSizeX64.HasValue ? member.BitSizeX64 : member.BitSize;

        // Why a declared type is outside what this comparison can say anything
        // about; null when it is fair game.
        private static string SkipReason(DeclaredTypeLayout type)
        {
            if (type.Name == null)
                return "unnamed declared type";
            if (type.IsFunctionBlock)
                return "function block instance layout is not modeled";
            if (type.Members.Count == 0)
                return type.BaseTypeName == null ? "neither members nor a base type declared" : null;
            if (type.Members.Any(m => m.IsStatic))
                return "static members - a program or global variable list, not an instance layout";
            if (type.Members.Any(m => m.BitOffset == null))
                return "a member declares no offset";
            if (IsUnion(type))
                return "union layout is not modeled";
            return null;
        }

        // A .tmc marks a UNION no differently from a STRUCT; what gives it away
        // is every member starting at the same offset. xStunit has no union
        // model at all, so reading one as a struct would report a wall of
        // offset mismatches that all say the same thing.
        private static bool IsUnion(DeclaredTypeLayout type) =>
            type.Members.Count > 1 && type.Members.All(m => m.BitOffset == 0);

        // The layout math refuses a type it has no rule for by throwing, and an
        // unresolvable ARRAY bound or STRING size surfaces as a FormatException
        // from the bound parsers. Both are gaps in xStunit, not disagreements
        // about a rule, so they are reported rather than thrown on.
        private static bool IsLayoutRefusal(Exception ex) =>
            ex is NotSupportedException || ex is FormatException;
    }
}
