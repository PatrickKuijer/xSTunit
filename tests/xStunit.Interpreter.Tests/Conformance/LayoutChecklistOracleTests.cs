using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using xStunit.Parser;
using Xunit;

namespace xStunit.Interpreter.Tests.Conformance
{
    // The compiler's half of the layout checklist. LayoutChecklistFixtureTests
    // pins what xStunit's own math makes of each fixture; this file pins what
    // TwinCAT made of the same source, read out of the .tmc it emitted, and
    // diffs the two.
    //
    // Both .tmc files come from one build of the fixtures in
    // tests/Fixtures/LayoutChecklistFixture, once for each target platform.
    // Two targets is what makes an address width visible at all: identical
    // source, different bytes.
    public class LayoutChecklistOracleTests
    {
        private const int BitsPerByte = 8;
        private const string X86Module = "LayoutChecklist.x86";
        private const string X64Module = "LayoutChecklist.x64";

        // Each .tmc is a third of a megabyte and every test below reads one, so
        // the parse is done once per module rather than once per assertion.
        private static readonly ConcurrentDictionary<string, ModuleLayout> ParsedModules =
            new ConcurrentDictionary<string, ModuleLayout>();

        private static ModuleLayout ParsedModule(string moduleName) =>
            ParsedModules.GetOrAdd(moduleName, name => TmcLayoutReader.ReadFile(
                Path.Combine(TestFixtures.LayoutChecklistFixtureDir(), name + ".tmc")));

        // The .TcDUT files both .tmc files were built from, keyed by the name
        // the DUT file itself carries. Keyed that way on purpose: which source
        // belongs to which declared type must not depend on the declaration
        // parser whose UNION reading these runs are scoring.
        private static readonly IReadOnlyDictionary<string, string> ChecklistSource = ReadChecklistSource();

        private static IReadOnlyDictionary<string, string> ReadChecklistSource()
        {
            var source = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(TestFixtures.LayoutChecklistFixtureDir(), "*.TcDUT"))
            {
                var dut = TcDutParser.Parse(File.ReadAllText(file));
                source[dut.Name] = dut.DeclarationText;
            }

            return source;
        }

        // Every comparison below is handed the ST source, so the run scores
        // xStunit's own reading of a UNION declaration rather than assuming it
        // from the offsets the compiler produced.
        private static LayoutReport Compare(string moduleName) =>
            LayoutOracle.Compare(ParsedModule(moduleName), ChecklistSource);

        // The committed diff is the record of exactly how far xStunit's layout
        // math conforms over the checklist. A rule getting fixed and a rule
        // silently regressing look the same to the compiler; this is what tells
        // them apart.
        [Theory]
        [InlineData(X86Module)]
        [InlineData(X64Module)]
        public void Compare_ReproducesTheCommittedDiff(string module)
        {
            var expected = File.ReadAllText(
                Path.Combine(TestFixtures.LayoutChecklistFixtureDir(), module + ".diff.txt"));

            Assert.Equal(Normalize(expected), Normalize(Compare(module).ToText()));
        }

        // The conformance claim itself, now over source written to exercise the
        // rules rather than over whatever a borrowed project happened to
        // contain: on a 32-bit target xStunit agrees with the compiler about
        // every member of every type it can reach. If this goes red, some layout
        // rule is wrong rather than merely unimplemented.
        [Fact]
        public void Compare_X86Module_AgreesWithTheCompilerOnEveryComparedMember()
        {
            var report = Compare(X86Module);

            Assert.Empty(report.Mismatches);
            Assert.True(report.ComparedMemberCount > 100, $"only {report.ComparedMemberCount} members were compared");
        }

        // The same claim on the 64-bit target, where every address is twice as
        // wide and displaces everything behind it. The types carrying one are
        // named rather than counted: they are the whole difference between the
        // two modules, so a run that agreed everywhere except on them would
        // still be agreeing about nothing this module was built to measure.
        [Fact]
        public void Compare_X64Module_AgreesWithTheCompilerOnEveryComparedMember()
        {
            var report = Compare(X64Module);
            var typesDeclaringAnAddress = ParsedModule(X64Module).Types
                .Where(t => t.BaseTypeIsPointer || t.Members.Any(m => m.IsPointer || m.IsReference))
                .Select(t => t.Name)
                .ToList();

            Assert.Empty(report.Mismatches);
            Assert.True(report.ComparedMemberCount > 100, $"only {report.ComparedMemberCount} members were compared");
            Assert.All(
                new[] { "ST_PointerWidth", "ST_ReferenceWidth", "AnyType", "_Implicit_Task_Info", "RTS_IEC_HANDLE" },
                name => Assert.Contains(name, typesDeclaringAnAddress));
        }

        // The whole pointer-width rule as a single number, from identical
        // source: the compiler puts the trailer at byte 8 on a 32-bit target
        // and at byte 16 on a 64-bit one.
        [Theory]
        [InlineData(X86Module, 4, 12)]
        [InlineData(X64Module, 8, 24)]
        public void PointerWidth_IsTheTargetsAddressWidth(string module, int addressSize, int typeSize)
        {
            AssertDeclaredLayout(module, "ST_PointerWidth", typeSize,
                ("leadIn", 0, 1),
                ("target", addressSize, addressSize),
                ("trailer", addressSize * 2, 1));
        }

        // REFERENCE TO is laid out as an address in its own right and not as
        // the INT it refers to, which would have made it 2 bytes on both
        // targets. It tracks the pointer exactly, so the .tmc marks it with a
        // ReferenceTo attribute where a pointer gets PointerTo - a member read
        // without that attribute is sized as the referent and silently
        // "conforms" at the wrong width.
        [Theory]
        [InlineData(X86Module, 4, 12)]
        [InlineData(X64Module, 8, 24)]
        public void ReferenceWidth_IsAnAddressAndNotTheReferent(string module, int addressSize, int typeSize)
        {
            AssertDeclaredLayout(module, "ST_ReferenceWidth", typeSize,
                ("leadIn", 0, 1),
                ("target", addressSize, addressSize),
                ("trailer", addressSize * 2, 1));

            Assert.True(DeclaredMember(module, "ST_ReferenceWidth", "target").IsReference);
        }

        // The 8-byte scalars align to 8 rather than to the 4 a 32-bit target
        // could have settled for, and the type is padded out to 32 rather than
        // ending at 25 - the trailing-padding rule at an alignment no other
        // fixture reaches.
        //
        // Asserted on both targets because the 32-bit answer is the surprising
        // one: an LREAL aligned to 8 on a machine whose word is 4 bytes is the
        // reading a 32-bit target could plausibly have talked itself out of.
        [Theory]
        [InlineData(X86Module)]
        [InlineData(X64Module)]
        public void WideAlignment_AlignsEightByteScalarsToEightAndPadsTheTypeToMatch(string module)
        {
            AssertDeclaredLayout(module, "ST_WideAlignment", 32,
                ("leadIn", 0, 4),
                ("wideFloat", 8, 8),
                ("wideInt", 16, 8),
                ("trailer", 24, 1));
        }

        // Three elements carrying five bytes of content each occupy 24 bytes,
        // not 15: the stride is the element size rounded up to the element's own
        // alignment, so the sentinel lands at 24.
        [Fact]
        public void ArrayStride_RoundsEachElementUpRatherThanPackingThem()
        {
            AssertDeclaredLayout(X86Module, "ST_TrailingPadding", 8,
                ("wide", 0, 4),
                ("tail", 4, 1));

            AssertDeclaredLayout(X86Module, "ST_ArrayStride", 28,
                ("items", 0, 24),
                ("sentinel", 24, 1));
        }

        // A STRING(5) is 6 bytes, so the terminator is counted; a WSTRING(5) is
        // 12 and starts at 8 rather than 7, so it is WORD-aligned and pads the
        // type out behind it.
        [Fact]
        public void StringSizes_CountTheTerminatorAndAlignWideStringsToTwoBytes()
        {
            AssertDeclaredLayout(X86Module, "ST_StringSizes", 22,
                ("narrow", 0, 6),
                ("guard", 6, 1),
                ("wide", 8, 12),
                ("trailer", 20, 1));
        }

        // An enum is as wide as its declared base type, and one with no declared
        // base is an INT. Each guard byte is a discriminator: guardOne at 1 says
        // the BYTE base was honoured, guardTwo at 4 says the undeclared base
        // came out two bytes wide and not one or four.
        [Fact]
        public void EnumWidths_FollowTheDeclaredBaseTypeAndDefaultToInt()
        {
            AssertDeclaredLayout(X86Module, "ST_EnumWidths", 12,
                ("byteBased", 0, 1),
                ("guardOne", 1, 1),
                ("defaultBased", 2, 2),
                ("guardTwo", 4, 1),
                ("dintBased", 8, 4));
        }

        // A BOOL member is a byte: two of them plus a guard byte make 3, where
        // bit-packing would have made 2.
        [Fact]
        public void BoolWidth_GivesEveryBoolItsOwnByte()
        {
            AssertDeclaredLayout(X86Module, "ST_BoolWidth", 3,
                ("first", 0, 1),
                ("second", 1, 1),
                ("guard", 2, 1));
        }

        // pack_mode does not reach into a nested struct type: the packed outer
        // struct places the nested value at a byte boundary, but the nested type
        // keeps its own internal padding and stays 8 bytes wide, so the trailer
        // sits at 9. Had the pragma inherited, the nested value would be 5 bytes
        // and the trailer would sit at 6.
        [Fact]
        public void PackedOuter_PacksItsOwnFieldsWithoutRepackingTheNestedType()
        {
            AssertDeclaredLayout(X86Module, "ST_NestedNatural", 8,
                ("small", 0, 1),
                ("wide", 4, 4));

            AssertDeclaredLayout(X86Module, "ST_PackedOuter", 10,
                ("leadIn", 0, 1),
                ("nested", 1, 8),
                ("trailer", 9, 1));
        }

        // pack_mode caps a field's alignment rather than flattening it: under
        // pack_mode 2 a DINT aligns to 2 and sits at offset 2 - not at 4 as it
        // would unpacked, and not at 1 as it would if any pack_mode at all were
        // read as byte-packing.
        [Fact]
        public void PackedToTwo_CapsAlignmentAtThePragmaRatherThanFlatteningIt()
        {
            AssertDeclaredLayout(X86Module, "ST_PackedToTwo", 8,
                ("leadIn", 0, 1),
                ("wide", 2, 4),
                ("tail", 6, 1));
        }

        // Without the pragma surviving into the .tmc, a packed type is read back
        // as naturally aligned and reported as a disagreement where xStunit is
        // right - so the two packed fixtures above measure nothing at all
        // unless this holds.
        [Fact]
        public void PackMode_SurvivesIntoTheTmcAlongsideTheLayoutItProduced()
        {
            Assert.Equal(1, DeclaredType(X86Module, "ST_PackedOuter").PackMode);
            Assert.Equal(2, DeclaredType(X86Module, "ST_PackedToTwo").PackMode);
            Assert.Null(DeclaredType(X86Module, "ST_NestedNatural").PackMode);
        }

        // BIT members carry sub-byte offsets - the one place in the checklist
        // where an offset is not a whole number of bytes - and two of them share
        // the byte the guard then follows. xStunit refuses to size BIT at all,
        // so this is the compiler stating an answer for a model not yet written
        // rather than a comparison.
        [Fact]
        public void BitPacking_PacksBitsIntoSharedBytesAtSubByteOffsets()
        {
            var type = DeclaredType(X86Module, "ST_BitPacking");

            Assert.Equal(16, type.BitSize);
            Assert.Equal(
                new[] { ("firstBit", 0, 1), ("secondBit", 1, 1), ("guard", 8, 8) },
                type.Members.Select(m => (m.Name, m.BitOffset.Value, m.BitSize.Value)).ToArray());

            Assert.Contains(
                Compare(X86Module).Findings,
                f => f.Subject == "ST_BitPacking.firstBit" && f.Kind == LayoutFindingKind.Unsupported);
        }

        // A union's members all start at offset 0 and its size is its widest
        // member - 8 bytes here, from the LWORD, not the 2 of the first member
        // or the 4 of the last-but-one. It also imposes that 8-byte alignment on
        // whatever holds it, which is what the holder's own offsets report and
        // what no reading of the union's declaration alone would give.
        [Fact]
        public void UnionLayout_OverlaysEveryMemberAndImposesItsWidestAlignment()
        {
            var union = DeclaredType(X86Module, "U_OverlaidScalars");

            Assert.Equal(64, union.BitSize);
            Assert.All(union.Members, m => Assert.Equal(0, m.BitOffset));

            AssertDeclaredLayout(X86Module, "ST_UnionHolder", 24,
                ("leadIn", 0, 1),
                ("overlay", 8, 8),
                ("trailer", 16, 1));

            Assert.DoesNotContain(Compare(X86Module).Findings,
                f => f.TypeName == "U_OverlaidScalars" || f.TypeName == "ST_UnionHolder");
        }

        // What the union rows above are worth. Shared offsets are the outcome
        // the layout math is supposed to predict, so a run that read union-ness
        // out of the .tmc could never catch xStunit and the compiler disagreeing
        // about whether a type is a union at all. Hand the same module a source
        // xStunit no longer reads as a UNION and the rows turn red, which is
        // what makes UNION recognition part of the score rather than an
        // assumption the answer key supplied.
        [Fact]
        public void UnionRecognition_ComesFromTheParsedSourceAndNotFromTheSharedOffsets()
        {
            var readAsAStruct = ChecklistSource.ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);
            readAsAStruct["U_OverlaidScalars"] = readAsAStruct["U_OverlaidScalars"].Replace("UNION", "STRUCT");

            Assert.DoesNotContain(Compare(X86Module).Mismatches, f => f.TypeName == "U_OverlaidScalars");
            Assert.Contains(
                LayoutOracle.Compare(ParsedModule(X86Module), readAsAStruct).Mismatches,
                f => f.TypeName == "U_OverlaidScalars");
        }

        // The union gap was never confined to the fixtures: TcUnit's own
        // assertion record holds two U_ExpectedOrActual members, so every type
        // on that path was unsizable while unions were. Its BIT member is the
        // one place a BIT carries a byte of its own - no neighbour to share
        // one with - which is why this type is reachable while ST_BitPacking
        // stays unsupported.
        [Theory]
        [InlineData(X86Module)]
        [InlineData(X64Module)]
        public void AssertResultPath_IsSizedOnceUnionsAre(string module)
        {
            AssertDeclaredLayout(module, "ST_AssertResult", 1536,
                ("Expected", 0, 512),
                ("Actual", 512, 512),
                ("Message", 1024, 256),
                ("TestInstancePath", 1280, 256));

            Assert.DoesNotContain(Compare(module).Findings,
                f => f.TypeName == "U_ExpectedOrActual" || f.TypeName == "ST_AssertResult");
        }

        // TwinCAT's own argument record carries a void pointer, so refusing to
        // size PVOID left T_Arg - and any code reading one - outside the
        // comparison entirely. The compiler declares that member 32 bits on
        // x86 and 64 on x64, which is what makes it an address rather than an
        // alias of some fixed-width type, and what the whole record's size
        // follows.
        [Theory]
        [InlineData(X86Module, 4, 12)]
        [InlineData(X64Module, 8, 16)]
        public void VoidPointer_IsTheTargetsAddressWidthRatherThanRefused(
            string module, int addressSize, int typeSize)
        {
            AssertDeclaredLayout(module, "T_Arg", typeSize,
                ("eType", 0, 2),
                ("cbLen", 4, 4),
                ("pData", 8, addressSize));

            Assert.DoesNotContain(
                Compare(module).Findings, f => f.Detail != null && f.Detail.Contains("PVOID"));
        }

        // The system-info types are TwinCAT's own, so a member of one that
        // cannot be sized reaches any code reading PlcAppSystemInfo or
        // PlcTaskSystemInfo rather than only the fixtures. Their ObjId is the
        // one object-type-class id in either module, and the compiler declares
        // it 32 bits wide on both targets - an id, not an address - so nothing
        // about it may depend on which target the module was built for.
        [Theory]
        [InlineData(X86Module)]
        [InlineData(X64Module)]
        public void ObjectTypeClassId_IsSizedOnBothTargetsRatherThanRefused(string module)
        {
            Assert.DoesNotContain(
                Compare(module).Findings, f => f.Detail != null && f.Detail.Contains("OTCID"));
        }

        // TwinCAT declares PlcAppSystemInfo.AppTimestamp as DT, IEC's
        // abbreviated spelling of DATE_AND_TIME. Sizing only the spelled-out
        // name cut the type off eight members in, which is a hole in xStunit's
        // reading of a name rather than anything the compiler decided.
        [Theory]
        [InlineData(X86Module)]
        [InlineData(X64Module)]
        public void AbbreviatedDateAndTime_IsSizedOnBothTargetsRatherThanRefused(string module)
        {
            Assert.DoesNotContain(
                Compare(module).Findings, f => f.Detail != null && f.Detail.Contains("'DT'"));
        }

        // Offsets and sizes are asserted in bytes so that this file and
        // LayoutChecklistFixtureTests state the same rule in the same unit and
        // can be read side by side. The .tmc counts in bits; a checklist type
        // whose members do not divide evenly into bytes is BIT's business alone,
        // and has its own test.
        private static void AssertDeclaredLayout(
            string module, string typeName, int expectedSize, params (string Field, int Offset, int Size)[] expectedFields)
        {
            var type = DeclaredType(module, typeName);

            Assert.Equal(expectedSize, Bytes(type.BitSize));
            Assert.Equal(
                expectedFields,
                type.Members.Select(m => (m.Name, Bytes(m.BitOffset), Bytes(m.BitSize))).ToArray());
        }

        private static int Bytes(int? bits)
        {
            Assert.NotNull(bits);
            Assert.True(bits.Value % BitsPerByte == 0, $"{bits} bits is not a whole number of bytes");
            return bits.Value / BitsPerByte;
        }

        private static DeclaredTypeLayout DeclaredType(string module, string typeName) =>
            Assert.Single(ParsedModule(module).Types, t => t.Name == typeName);

        private static DeclaredMemberLayout DeclaredMember(string module, string typeName, string memberName) =>
            Assert.Single(DeclaredType(module, typeName).Members, m => m.Name == memberName);

        private static string Normalize(string text) => text.Replace("\r\n", "\n");
    }
}
