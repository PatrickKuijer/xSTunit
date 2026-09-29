using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests.Conformance
{
    // The ST source the Grade A layout oracle measures: one construct per rule
    // on the byte-layout checklist, each shaped so that getting the rule wrong
    // moves a member's offset or a type's size rather than landing on the same
    // bytes by luck.
    //
    // Each test pins what xStunit's own math makes of a fixture and names the
    // number a rival reading of that rule would have produced instead. A
    // fixture whose two readings agree measures nothing, and these numbers are
    // what keeps that from going unnoticed.
    public class LayoutChecklistFixtureTests
    {
        private static readonly string[] FixtureDirectories = { TestFixtures.LayoutChecklistFixtureDir() };

        // Built through the interpreter's own .TcDUT loaders rather than by
        // hand, so a fixture line these loaders silently drop shows up as a
        // wrong offset here instead of as a fixture that measures nothing.
        private static readonly TypeRegistry Registry = BuildRegistry();
        private static readonly TypeLayout Layout = new TypeLayout(Registry, TargetPlatform.X86);

        private static TypeRegistry BuildRegistry()
        {
            var structs = DutStructLoader.Load(FixtureDirectories, out _);
            var enums = DutEnumLoader.Load(FixtureDirectories, out _, out _);
            var aliases = DutAliasLoader.Load(FixtureDirectories, out _);

            var resolvable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var alias in aliases.Concat(enums))
                resolvable[alias.Key] = alias.Value;

            return new TypeRegistry(Array.Empty<PouAst>(), structs, null, resolvable);
        }

        // A 32-bit address leaves the trailer at byte 8; the 64-bit target the
        // same source also builds for puts it at 16. That gap is the whole
        // pointer-width rule, and this type exists to make it a single number -
        // one that follows the target rather than being fixed at either answer.
        [Theory]
        [InlineData("x86", 4, 12)]
        [InlineData("x64", 8, 24)]
        public void PointerWidth_PlacesTheTrailerBehindTheTargetsAddress(
            string targetName, int addressSize, int typeSize)
        {
            AssertLayout(Target(targetName), "ST_PointerWidth", typeSize,
                ("leadIn", 0, 1),
                ("target", addressSize, addressSize),
                ("trailer", addressSize * 2, 1));
        }

        // REFERENCE TO is sized as an address in its own right rather than
        // inheriting the referent's width - INT here, which would put the
        // trailer at 4 on both targets and make the type immune to the one
        // rule it exists to measure.
        [Theory]
        [InlineData("x86", 4, 12)]
        [InlineData("x64", 8, 24)]
        public void ReferenceWidth_IsSizedAsAnAddressNotAsTheReferent(
            string targetName, int addressSize, int typeSize)
        {
            AssertLayout(Target(targetName), "ST_ReferenceWidth", typeSize,
                ("leadIn", 0, 1),
                ("target", addressSize, addressSize),
                ("trailer", addressSize * 2, 1));
        }

        // A void pointer names no referent at all, which is exactly why it is
        // an address and not an alias of some fixed-width type: it is as wide
        // as the target's other addresses, and refusing to size it would leave
        // TwinCAT's own T_Arg unmeasurable.
        [Theory]
        [InlineData("x86", 4)]
        [InlineData("x64", 8)]
        public void VoidPointer_IsSizedAsAnAddressOnEitherTarget(string targetName, int addressSize)
        {
            Assert.Equal(addressSize, new TypeLayout(Registry, Target(targetName)).SizeOf("PVOID").Size);
        }

        // The 8-byte scalars align to 8, not to the 4 bytes that would suffice
        // on a 32-bit target: an LREAL after a DINT sits at 8, not at 4. The
        // trailing byte then pads the whole type out to 32 rather than ending
        // it at 25, which is the "does SIZEOF include trailing padding"
        // question asked at an alignment no other fixture reaches.
        [Fact]
        public void WideAlignment_AlignsEightByteScalarsToEightAndPadsTheTypeToMatch()
        {
            AssertLayout("ST_WideAlignment", 32,
                ("leadIn", 0, 4),
                ("wideFloat", 8, 8),
                ("wideInt", 16, 8),
                ("trailer", 24, 1));
        }

        // CODESYS does not propagate pack_mode into a nested struct type: the
        // packed outer struct places the nested value at a byte boundary, but
        // the nested type keeps its own internal padding and stays 8 bytes
        // wide. Had the pragma inherited, the nested value would be 5 bytes and
        // the trailer would sit at 6 rather than 9.
        [Fact]
        public void PackedOuter_PacksItsOwnFieldsWithoutRepackingTheNestedType()
        {
            AssertLayout("ST_NestedNatural", 8,
                ("small", 0, 1),
                ("wide", 4, 4));

            AssertLayout("ST_PackedOuter", 10,
                ("leadIn", 0, 1),
                ("nested", 1, 8),
                ("trailer", 9, 1));
        }

        // pack_mode caps each field's alignment rather than flattening it:
        // under pack_mode 2 a DINT aligns to 2, so it sits at offset 2 - not at
        // 4 as it would unpacked, and not at 1 as it would if any pack_mode at
        // all were read as byte-packing.
        [Fact]
        public void PackedToTwo_CapsAlignmentAtThePragmaRatherThanFlatteningIt()
        {
            AssertLayout("ST_PackedToTwo", 8,
                ("leadIn", 0, 1),
                ("wide", 2, 4),
                ("tail", 6, 1));
        }

        // An explicit pack_mode '0' means "aligned without gaps", the same as
        // '1': the DINT sits at 1 and the type is 6 bytes. Read as an absent
        // pragma, the DINT would sit at 4 and the type would be 12.
        [Fact]
        public void PackedToZero_PacksWithoutGapsRatherThanAligningNaturally()
        {
            AssertLayout("ST_PackedToZero", 6,
                ("leadIn", 0, 1),
                ("wide", 1, 4),
                ("tail", 5, 1));
        }

        // The pragma has to survive from the ST source into the AST, because
        // a .tmc records the layout a struct ended up with and never the
        // pragma that produced it - the source is the only place the oracle
        // can learn a struct was packed at all.
        [Fact]
        public void PackedFixtures_CarryTheirPackModeOutOfTheSource()
        {
            Assert.Equal(1, Registry.GetStruct("ST_PackedOuter").PackMode);
            Assert.Equal(0, Registry.GetStruct("ST_PackedToZero").PackMode);
            Assert.Null(Registry.GetStruct("ST_NestedNatural").PackMode);
        }

        // Array stride is the element size rounded up to the element's own
        // alignment, so three 5-byte-of-content elements occupy 24 bytes and
        // not 15: the sentinel behind them lands at 24. A stride that skipped
        // each element's trailing padding would put it at 15 and would have
        // every element after the first straddling its alignment.
        [Fact]
        public void ArrayStride_RoundsEachElementUpRatherThanPackingThem()
        {
            AssertLayout("ST_TrailingPadding", 8,
                ("wide", 0, 4),
                ("tail", 4, 1));

            AssertLayout("ST_ArrayStride", 28,
                ("items", 0, 24),
                ("sentinel", 24, 1));
        }

        // A STRING(5) is 6 bytes - five characters plus the terminator - and a
        // WSTRING(5) is 12, two bytes per character including the terminator.
        // The WSTRING is also WORD-aligned, so it starts at 8 rather than 7
        // and pads the type out to an even size. Every one of those four
        // claims moves a number in this table if it is wrong.
        [Fact]
        public void StringSizes_CountTheTerminatorAndAlignWideStringsToTwoBytes()
        {
            AssertLayout("ST_StringSizes", 22,
                ("narrow", 0, 6),
                ("guard", 6, 1),
                ("wide", 8, 12),
                ("trailer", 20, 1));
        }

        // An enum is as wide as its declared base type, and an enum with no
        // declared base is an INT. Each guard byte is one rule's discriminator:
        // guardOne at 1 rather than 2 says the BYTE base was honoured, and
        // guardTwo at 4 rather than 3 or 8 says the enum with no declared base
        // was two bytes and not one or four.
        [Fact]
        public void EnumWidths_FollowTheDeclaredBaseTypeAndDefaultToInt()
        {
            AssertLayout("ST_EnumWidths", 12,
                ("byteBased", 0, 1),
                ("guardOne", 1, 1),
                ("defaultBased", 2, 2),
                ("guardTwo", 4, 1),
                ("dintBased", 8, 4));
        }

        // A BOOL member occupies a whole byte rather than being packed into a
        // bit: two of them plus a guard byte make 3, where bit-packing would
        // make 2.
        [Fact]
        public void BoolWidth_GivesEveryBoolItsOwnByte()
        {
            AssertLayout("ST_BoolWidth", 3,
                ("first", 0, 1),
                ("second", 1, 1),
                ("guard", 2, 1));
        }

        // BIT is a struct-only type with sub-byte offsets, which xStunit has no
        // model for. Pinned as a refusal rather than left out of the fixture
        // set: the compiler's own .tmc will state the bit offsets, and a run
        // that reported this type as conforming would be reporting on a rule it
        // never applied.
        [Fact]
        public void BitPacking_IsRefusedRatherThanSizedAsBytes()
        {
            var ex = Assert.Throws<NotSupportedException>(() => Placements("ST_BitPacking"));

            Assert.Contains("BIT", ex.Message);
        }

        // A union's members all start at offset 0 and its size is its widest
        // member: 8 bytes from the LWORD, not the 2 of the first member, the 4
        // of the middle one, or the 14 that laying them end to end would give.
        // The holder is what reports the alignment the union imposes - a union
        // aligned to 1 would put the trailer at 9 rather than 16.
        [Fact]
        public void UnionLayout_OverlaysEveryMemberAndImposesItsWidestAlignment()
        {
            AssertLayout("U_OverlaidScalars", 8,
                ("asWord", 0, 2),
                ("asBytes", 0, 4),
                ("asLong", 0, 8));

            AssertLayout("ST_UnionHolder", 24,
                ("leadIn", 0, 1),
                ("overlay", 8, 8),
                ("trailer", 16, 1));
        }

        // VarBlockParser skips a declaration line it cannot spell instead of
        // failing, so a fixture written in a shape it does not accept would
        // still load - as a type silently missing a member, quietly measuring
        // a layout no one authored. Every line inside a STRUCT or UNION body
        // must therefore survive as a field.
        [Fact]
        public void EveryDeclaredStructField_SurvivesParsing()
        {
            foreach (var (file, declarationText) in DutDeclarations()
                         .Where(d => StructDeclParser.DeclaredBody(d.DeclarationText) != null))
            {
                var declared = CountDeclaredFields(declarationText);
                var parsed = StructDeclParser.Parse(declarationText).Fields.Count;

                Assert.True(
                    declared == parsed,
                    $"{Path.GetFileName(file)} declares {declared} fields but parses as {parsed}");
            }
        }

        // A DUT no code references may never reach the compiler's .tmc at all,
        // which would drop a checklist rule from the oracle without any test
        // going red. The global variable list is what forces every fixture type
        // to be instantiated, and this is what keeps it complete as fixtures
        // are added.
        [Fact]
        public void EveryFixtureType_IsInstantiatedByTheGlobalVariableList()
        {
            var declaredTypes = MultiDirectoryPouLoader.FindDutFiles(FixtureDirectories)
                .Select(Path.GetFileNameWithoutExtension);

            var gvlFile = Assert.Single(MultiDirectoryPouLoader.FindGvlFiles(FixtureDirectories));
            var instantiated = new HashSet<string>(
                VarBlockParser.Parse(TcGvlParser.Parse(File.ReadAllText(gvlFile)).DeclarationText)
                    .Select(field => field.TypeName),
                StringComparer.OrdinalIgnoreCase);

            Assert.All(declaredTypes, type =>
                Assert.True(instantiated.Contains(type), $"{type} is declared but never instantiated"));
        }

        // The bare overload measures the 32-bit target, which is what every
        // fixture whose numbers do not contain an address is measuring anyway.
        private static void AssertLayout(
            string typeName, int expectedSize, params (string Field, int Offset, int Size)[] expectedFields) =>
            AssertLayout(TargetPlatform.X86, typeName, expectedSize, expectedFields);

        private static void AssertLayout(
            TargetPlatform target,
            string typeName,
            int expectedSize,
            params (string Field, int Offset, int Size)[] expectedFields)
        {
            var layout = new TypeLayout(Registry, target);
            var actual = Placements(typeName, layout)
                .Select(p => (p.Field.Name, p.Offset, p.Size))
                .ToArray();

            Assert.Equal(expectedFields, actual);
            Assert.Equal(expectedSize, layout.SizeOf(typeName).Size);
        }

        private static IReadOnlyList<FieldPlacement> Placements(string typeName, TypeLayout layout = null)
        {
            var structAst = Registry.GetStruct(typeName);
            Assert.NotNull(structAst);
            return (layout ?? Layout).Fields(structAst).ToList();
        }

        private static TargetPlatform Target(string name)
        {
            Assert.True(TargetPlatform.TryParse(name, out var target), $"'{name}' is not a target platform");
            return target;
        }

        private static IEnumerable<(string File, string DeclarationText)> DutDeclarations() =>
            MultiDirectoryPouLoader.FindDutFiles(FixtureDirectories)
                .Select(file => (file, TcDutParser.Parse(File.ReadAllText(file)).DeclarationText));

        // Deliberately looser than the pattern VarBlockParser matches with: it
        // accepts any "name : something;" line, so a fixture written in a type
        // syntax VarBlockParser rejects still counts as a declared field here
        // and the two counts diverge. A pattern copied from the parser would
        // agree with it about every line, including the ones it drops.
        private static readonly Regex FieldLinePattern = new Regex(@"^\w+\s*:.*;$");

        private static int CountDeclaredFields(string declarationText)
        {
            var inBody = false;
            var count = 0;
            foreach (var rawLine in declarationText.Replace("\r\n", "\n").Split('\n'))
            {
                var line = rawLine.Trim();
                if (line == "STRUCT" || line == "UNION")
                    inBody = true;
                else if (line == "END_STRUCT" || line == "END_UNION")
                    inBody = false;
                else if (inBody && FieldLinePattern.IsMatch(line))
                    count++;
            }

            return count;
        }
    }
}
