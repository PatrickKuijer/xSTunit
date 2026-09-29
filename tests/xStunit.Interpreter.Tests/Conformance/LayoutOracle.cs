using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using xStunit.Parser;

namespace xStunit.Interpreter.Tests.Conformance
{
    // Diffs the byte layout a TwinCAT compiler declared in a .tmc against the
    // layout xStunit's own math computes for the same types.
    //
    // The .tmc is the measuring device: it states every member's offset and
    // size outright, so the declared types themselves supply the fixtures and
    // the comparison needs no TwinCAT installation and no running PLC.
    // Whatever types a project happens to contain is whatever the run covers -
    // see LayoutReport.ComparedMemberCount for how much that was.
    //
    // The ST source those types were compiled from is optional, and supplying
    // it is what decides whether UNION recognition is scored - see IsUnion.
    //
    // Packed types are compared under the cap the compiler applied, which it
    // records in the file - see DeclaredTypeLayout.PackMode.
    internal static class LayoutOracle
    {
        private const int BitsPerByte = 8;

        // The alignment of the widest scalar TwinCAT aligns anything to, in
        // bits. Nothing is ever padded by this much - padding is what it takes
        // to reach the next multiple, always less than the alignment itself.
        private const int WidestAlignmentBits = 64;

        private static readonly IReadOnlyDictionary<string, string> NoSource =
            new Dictionary<string, string>();

        public static LayoutReport Compare(ModuleLayout module) => Compare(module, NoSource);

        // sourceDeclarations holds the .TcDUT declaration text of as many of
        // the module's types as the caller has, keyed by type name.
        public static LayoutReport Compare(
            ModuleLayout module, IReadOnlyDictionary<string, string> sourceDeclarations)
        {
            // The module names the machine it was compiled for, so a
            // conformance run needs nothing from the user to compare like with
            // like: the same target picks the declared width to read and the
            // width xStunit computes.
            var target = TargetPlatform.FromModuleTarget(module.TargetPlatform);

            var registry = BuildRegistry(module, sourceDeclarations);
            var layout = new TypeLayout(registry, target);

            var interfaces = InterfaceNames(module);
            var findings = new List<LayoutFinding>();
            var comparedTypes = 0;
            var comparedTypeNames = new List<string>();
            var comparedMembers = 0;

            foreach (var type in module.Types)
            {
                var skipReason = SkipReason(type, interfaces);
                if (skipReason != null)
                {
                    findings.Add(LayoutFinding.NotCompared(type.Name, null, skipReason));
                    continue;
                }

                var compared = type.Members.Count == 0
                    ? CompareTypeSize(layout, type, findings)
                    : CompareStruct(layout, registry, type, target, findings);
                if (compared.Reached)
                {
                    comparedTypes++;
                    comparedTypeNames.Add(type.Name);
                }
                comparedMembers += compared.Members;
            }

            return new LayoutReport(
                module.ModuleName, module.TargetPlatform, module.Types.Count, comparedTypes, comparedMembers, findings,
                comparedTypeNames);
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
            TargetPlatform target,
            List<LayoutFinding> findings)
        {
            var declared = type.IsFunctionBlock
                ? layout.FunctionBlockFields(registry.Get(type.Name))
                : layout.Fields(registry.GetStruct(type.Name));
            var (placements, failedIndex, failureDetail) = PlaceFields(declared);

            var gapIndex = FirstMemberBehindAnUndescribedGap(type, target);
            var stoppedByAGap = gapIndex >= 0 && (failedIndex < 0 || gapIndex < failedIndex);
            var comparedCount = stoppedByAGap ? gapIndex : placements.Count;

            for (var i = 0; i < comparedCount; i++)
            {
                var member = type.Members[i];
                var placement = placements[i];

                var declaredOffset = member.BitOffset;
                var computedOffset = placement.Offset * BitsPerByte;
                if (declaredOffset != computedOffset)
                    findings.Add(LayoutFinding.MemberOffset(type.Name, member.Name, declaredOffset, computedOffset));

                var declaredSize = DeclaredBits(member, target);
                var computedSize = placement.Size * BitsPerByte;
                if (declaredSize != computedSize)
                    findings.Add(LayoutFinding.MemberSize(type.Name, member.Name, declaredSize, computedSize));
            }

            if (stoppedByAGap)
            {
                findings.Add(LayoutFinding.NotCompared(
                    type.Name, type.Members[gapIndex].Name, "displaced by a gap the .tmc does not describe"));
                return new Comparison(false, comparedCount);
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

        // The first member the compiler placed further along than the members
        // in front of it account for, or -1 when the declared members describe
        // the type end to end.
        //
        // TwinCAT keeps a reserved member out of the .tmc entirely, leaving a
        // hole no SubItem describes. Padding can never open one: alignment tops
        // out at 8 bytes, so it always inserts less than that. Everything
        // behind such a hole is displaced by definition, and the comparison
        // stops there - reporting those members would state the hole's width as
        // an offset disagreement and say nothing about a layout rule. The
        // members in front of it are still scored, so the hole costs exactly
        // the rows it displaces.
        private static int FirstMemberBehindAnUndescribedGap(DeclaredTypeLayout type, TargetPlatform target)
        {
            for (var i = 1; i < type.Members.Count; i++)
            {
                var previous = type.Members[i - 1];
                var describedThrough = previous.BitOffset + DeclaredBits(previous, target);

                // Null widths and overlaid members (a union's, all at offset 0)
                // both fall out as "no gap": the comparison only stops on a hole
                // it can measure.
                if (type.Members[i].BitOffset - describedThrough >= WidestAlignmentBits)
                    return i;
            }

            return -1;
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
            IEnumerable<FieldPlacement> declared)
        {
            var placements = new List<FieldPlacement>();
            using (var fields = declared.GetEnumerator())
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

        private static TypeRegistry BuildRegistry(
            ModuleLayout module, IReadOnlyDictionary<string, string> sourceDeclarations)
        {
            var interfaces = InterfaceNames(module);
            var comparable = module.Types
                .Where(type => SkipReason(type, interfaces) == null && (type.Members.Count > 0 || type.IsFunctionBlock))
                .ToList();
            var structs = comparable
                .Where(type => !type.IsFunctionBlock)
                .Select(type => ToStructAst(type, sourceDeclarations));

            // Enums and aliases go in as ALIAS entries, which is how the
            // interpreter's own loaders register them: every layout lookup
            // resolves the name to its base type without knowing enums exist.
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var type in module.Types.Where(t => t.Name != null && t.Members.Count == 0 && t.BaseTypeName != null))
                aliases[type.Name] = AliasTarget(type);

            var functionBlocks = comparable
                .Where(type => type.IsFunctionBlock)
                .Select(type => ToPouAst(type, aliases))
                .ToList();

            return new TypeRegistry(functionBlocks, structs, null, aliases);
        }

        private static IReadOnlySet<string> InterfaceNames(ModuleLayout module) =>
            new HashSet<string>(
                module.Types.Where(t => t.IsInterface && t.Name != null).Select(t => t.Name),
                StringComparer.OrdinalIgnoreCase);

        // A block's declaration text is what the layout math reads its members
        // from, so the .tmc's members are written back out as ST lines. A type
        // the declaration grammar cannot spell - a subrange, an array of
        // pointers - goes in under a synthetic alias instead, which resolves
        // to exactly the type text the .tmc gave.
        private static PouAst ToPouAst(DeclaredTypeLayout type, Dictionary<string, string> aliases)
        {
            var lines = new List<string> { "VAR" };
            foreach (var member in type.Members)
            {
                var typeText = IecTypeName(member.TypeName, member.IsPointer, member.IsReference, member.ArrayDimensions);
                if (!DeclarableTypeText.IsMatch(typeText))
                {
                    var alias = $"__member_type_{aliases.Count}";
                    aliases[alias] = typeText;
                    typeText = alias;
                }

                lines.Add($"{member.Name} : {typeText};");
            }

            lines.Add("END_VAR");
            return new PouAst(
                type.Name, null, string.Join("\n", lines), string.Empty, new List<MethodAst>(),
                implementedInterfaces: type.ImplementedInterfaces);
        }

        private static readonly Regex DeclarableTypeText = new Regex(
            @"^(?:\w+|POINTER TO \w+|REFERENCE TO \w+|W?STRING\(\d+\))$", RegexOptions.Compiled);

        // TwinCAT's handle types (RTS_IEC_HANDLE and friends) alias a pointer,
        // not the pointed-to type, so the pointer has to survive into the alias
        // text or the alias resolves to the width of whatever it points at.
        // A base type is only ever marked PointerTo, never ReferenceTo: the
        // reference spelling shows up on members and not on the alias itself.
        private static string AliasTarget(DeclaredTypeLayout type) =>
            IecTypeName(type.BaseTypeName, type.BaseTypeIsPointer, isReference: false, type.ArrayDimensions);

        private static StructAst ToStructAst(
            DeclaredTypeLayout type, IReadOnlyDictionary<string, string> sourceDeclarations) =>
            new StructAst(
                type.Name,
                type.Members
                    .Select(m => new VarDecl(
                        m.Name,
                        IecTypeName(m.TypeName, m.IsPointer, m.IsReference, m.ArrayDimensions),
                        null,
                        VarSection.Local))
                    .ToList(),
                type.PackMode,
                IsUnion(type, sourceDeclarations));

        // The .tmc splits a declared type across a type name, a PointerTo or
        // ReferenceTo attribute and any number of ArrayInfo blocks - the same
        // pieces whether it is describing a member or an array/handle type
        // alias; xStunit's layout math takes one piece of ST type text.
        private static string IecTypeName(
            string baseTypeName,
            bool isPointer,
            bool isReference,
            IReadOnlyList<DeclaredArrayDimension> dimensions)
        {
            var typeName = baseTypeName;
            if (isPointer)
                typeName = $"POINTER TO {baseTypeName}";
            else if (isReference)
                typeName = $"REFERENCE TO {baseTypeName}";

            if (dimensions.Count == 0)
                return typeName;

            var bounds = string.Join(
                ",", dimensions.Select(d => $"{d.LowerBound}..{d.LowerBound + d.ElementCount - 1}"));
            return $"ARRAY[{bounds}] OF {typeName}";
        }

        // A member whose width differs between targets carries both; which one
        // is real depends on what the module was compiled for.
        private static int? DeclaredBits(DeclaredMemberLayout member, TargetPlatform target) =>
            target == TargetPlatform.X64 && member.BitSizeX64.HasValue ? member.BitSizeX64 : member.BitSize;

        // Why a declared type is outside what this comparison can say anything
        // about; null when it is fair game.
        private static string SkipReason(DeclaredTypeLayout type, IReadOnlySet<string> interfaces)
        {
            if (type.Name == null)
                return "unnamed declared type";
            if (type.Members.Count == 0 && !type.IsFunctionBlock)
                return type.BaseTypeName == null ? "neither members nor a base type declared" : null;
            if (type.ImplementedInterfaces.Count > 1)
                return $"implements {type.ImplementedInterfaces.Count} interfaces - no compiler output to verify their pointer layout";
            if (type.Members.Any(m => m.IsMethodInstance))
                return "method VAR_INST cells sit in the instance and are not modeled";
            if (type.IsFunctionBlock && type.Members.Any(m => m.TypeName != null && interfaces.Contains(m.TypeName)))
                return "interface-typed member - interface references are not modeled";
            if (type.Members.Any(m => m.IsStatic))
                return "static members - a program or global variable list, not an instance layout";
            if (type.Members.Any(m => m.BitOffset == null))
                return "a member declares no offset";
            return null;
        }

        // Union-ness comes from xStunit reading the type's own ST declaration,
        // never from the .tmc. Member names and types are imported from the
        // .tmc freely - those are declarations, and the comparison is exactly
        // "given this declared shape, does the layout math agree". Every member
        // sharing offset 0 is not a declaration but the outcome that math is
        // meant to predict, so importing it back as an input would leave union
        // the one shape xStunit and the compiler can never be caught
        // disagreeing about.
        //
        // A type the caller has no source for - a library type the module was
        // built against - falls back to that outcome. Its size, offsets and
        // alignment are still scored against the compiler; its UNION
        // recognition is not, because nothing here asked the parser.
        private static bool IsUnion(
            DeclaredTypeLayout type, IReadOnlyDictionary<string, string> sourceDeclarations) =>
            sourceDeclarations.TryGetValue(type.Name, out var declarationText)
                ? StructDeclParser.DeclaredBody(declarationText) == StructDeclParser.UnionBody
                : ShapedLikeAUnion(type);

        // Read a union as a struct and it reports a wall of offset mismatches
        // that all say the same thing.
        private static bool ShapedLikeAUnion(DeclaredTypeLayout type) =>
            type.Members.Count > 1 && type.Members.All(m => m.BitOffset == 0);

        // The layout math refuses a type it has no rule for by throwing, and an
        // unresolvable ARRAY bound or STRING size surfaces as a FormatException
        // from the bound parsers. Both are gaps in xStunit, not disagreements
        // about a rule, so they are reported rather than thrown on.
        private static bool IsLayoutRefusal(Exception ex) =>
            ex is NotSupportedException || ex is FormatException;
    }
}
