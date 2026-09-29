using System.Collections.Generic;
using System.IO;
using Xunit;

namespace xStunit.Interpreter.Tests.Conformance
{
    // The Grade A layout oracle. Both .tmc files are a real TwinCAT compiler's
    // own output, committed verbatim, so what they declare is not a restatement
    // of xStunit's beliefs about byte layout but an independent measurement of
    // the same types. The two modules come from one solution built for two
    // targets, which is what makes the pointer-width rule visible at all: the
    // same source lays out differently on x86 and x64.
    public class LayoutOracleTests
    {
        private const string X64Module = "PLC1";
        private const string X86Module = "PLC1Tests";

        private static LayoutReport Compare(string module) =>
            LayoutOracle.Compare(TmcLayoutReader.ReadFile(
                Path.Combine(TestFixtures.LayoutOracleFixtureDir(), module + ".tmc")));

        // The committed diff is the record of exactly how far xStunit's layout
        // math currently conforms. Any change to SizeOfType or the packing
        // rules moves a line here - a rule getting fixed and a rule silently
        // regressing look the same to the compiler, and this is what tells them
        // apart.
        [Theory]
        [InlineData(X64Module)]
        [InlineData(X86Module)]
        public void Compare_ReproducesTheCommittedDiff(string module)
        {
            var expected = File.ReadAllText(
                Path.Combine(TestFixtures.LayoutOracleFixtureDir(), module + ".diff.txt"));

            Assert.Equal(Normalize(expected), Normalize(Compare(module).ToText()));
        }

        // On a 32-bit target every rule xStunit models agrees with the
        // compiler, over every member of every type in the module it can reach.
        // This is the conformance claim itself; if it ever goes red, some layout
        // rule is wrong rather than merely unimplemented.
        //
        // Members, not types, are the denominator asserted here: two thirds of
        // the compared types are aliases and enums, which are one size check
        // each and exercise no offset or padding rule at all. What this module
        // does NOT reach is alignment above 4 bytes - the library structs
        // holding a LINT abort on a type name xStunit has no rule for, so the
        // LREAL/LINT half of the checklist waits on the authored fixtures.
        [Fact]
        public void Compare_X86Module_AgreesWithTheCompilerOnEveryComparedMember()
        {
            var report = Compare(X86Module);

            Assert.Empty(report.Mismatches);
            Assert.True(report.ComparedMemberCount > 50, $"only {report.ComparedMemberCount} members were compared");
        }

        // The same claim on the 64-bit build of the same solution, where every
        // address is 8 bytes and displaces every member behind it. This module
        // is where a 32-bit assumption left behind in the layout math surfaces:
        // TwinCAT's own _Implicit_Task_Info alone carries four addresses and
        // twenty members that slide if any one of them is sized at half width.
        [Fact]
        public void Compare_X64Module_AgreesWithTheCompilerOnEveryComparedMember()
        {
            var report = Compare(X64Module);

            Assert.Empty(report.Mismatches);
            Assert.True(report.ComparedMemberCount > 50, $"only {report.ComparedMemberCount} members were compared");
        }

        // A union's members all sit at offset 0, and a .tmc marks one no
        // differently from a struct - shared offsets are the only tell.
        //
        // This module is a borrowed project whose ST source is not committed
        // alongside it, so the oracle has nothing to ask about U_ExpectedOrActual
        // and reads it as a union by that shape. What this pins is therefore the
        // layout math over a union - its size, its offsets, its alignment - and
        // not that xStunit recognises UNION in source; that claim is scored in
        // LayoutChecklistOracleTests, whose fixtures ship their .TcDUT files.
        [Fact]
        public void Compare_Union_IsComparedWithEveryMemberOverlaid()
        {
            Assert.DoesNotContain(Compare(X86Module).Findings, f => f.TypeName == "U_ExpectedOrActual");
        }

        // A function block's members start past the hidden vtable pointer, and
        // past one more pointer per implemented interface. Scoring a block
        // therefore pins both header rules against the compiler's own offsets:
        // a model that dropped either would shift every member behind it.
        [Fact]
        public void Compare_FunctionBlock_IsScoredAgainstTheCompilersOffsets()
        {
            var report = Compare(X64Module);

            Assert.DoesNotContain(report.Findings, f => f.TypeName == "FB_AddLrealInt");
            Assert.Contains("FB_AddLrealInt", report.ComparedTypeNames);
        }

        // FB_TestResults implements I_TestResults, so its first member sits
        // behind two pointers: the compiler declares it at byte 8 on this
        // 32-bit module.
        [Fact]
        public void Compare_X86FunctionBlockImplementingAnInterface_AgreesWithTheCompiler()
        {
            var report = Compare(X86Module);

            Assert.DoesNotContain(report.Findings, f => f.TypeName == "FB_TestResults");
            Assert.Contains("FB_TestResults", report.ComparedTypeNames);
        }

        // A method's VAR_INST cell is stored in the instance behind the
        // declared members. Sizing only the declared members would agree with
        // the compiler about the offsets and disagree about the size, or agree
        // with both by padding coincidence, so a block carrying one is not
        // scored at all.
        [Fact]
        public void Compare_FunctionBlockWithMethodInstanceCells_IsReportedAsNotCompared()
        {
            var report = Compare(X86Module);

            var finding = Assert.Single(report.Findings, f => f.TypeName == "FB_TcUnitRunner");
            Assert.Equal(LayoutFindingKind.NotCompared, finding.Kind);
            Assert.Contains("method VAR_INST", finding.Detail);
            Assert.DoesNotContain("FB_TcUnitRunner", report.ComparedTypeNames);
        }

        [Theory]
        [InlineData("TwinCAT RT (x64)", 16, 192)]
        [InlineData("TwinCAT RT (x86)", 8, 96)]
        public void Compare_FunctionBlockImplementingAnInterface_ExpectsTheFirstMemberBehindTwoPointers(
            string targetPlatform, int firstMemberByte, int typeBits)
        {
            var report = CompareFunctionBlock(
                targetPlatform, firstMemberByte * 8, typeBits, new[] { "I_Logger" }, memberTypeName: "DINT");

            Assert.Empty(report.Mismatches);
            Assert.Equal(1, report.ComparedMemberCount);
        }

        [Fact]
        public void Compare_FunctionBlockWithTheWrongHeader_ReportsTheShiftedMember()
        {
            var report = CompareFunctionBlock(
                "TwinCAT RT (x64)", 8 * 8, 192, new[] { "I_Logger" }, memberTypeName: "DINT");

            Assert.Contains(report.Mismatches, f => f.Subject == "FB_Synthetic.n");
        }

        [Fact]
        public void Compare_FunctionBlockImplementingSeveralInterfaces_IsReportedAsNotCompared()
        {
            var report = CompareFunctionBlock(
                "TwinCAT RT (x64)", 24 * 8, 224, new[] { "I_A", "I_B" }, memberTypeName: "DINT");

            var finding = Assert.Single(report.Findings, f => f.TypeName == "FB_Synthetic");
            Assert.Equal(LayoutFindingKind.NotCompared, finding.Kind);
            Assert.Contains("interfaces", finding.Detail);
        }

        [Fact]
        public void Compare_FunctionBlockWithAnInterfaceMember_IsReportedAsNotCompared()
        {
            var report = CompareFunctionBlock(
                "TwinCAT RT (x64)", 8 * 8, 192, new string[0], memberTypeName: "I_Logger", declareInterface: true);

            var finding = Assert.Single(report.Findings, f => f.TypeName == "FB_Synthetic");
            Assert.Equal(LayoutFindingKind.NotCompared, finding.Kind);
            Assert.Contains("interface-typed member", finding.Detail);
        }

        private static LayoutReport CompareFunctionBlock(
            string targetPlatform,
            int declaredMemberBitOffset,
            int typeBits,
            string[] implementedInterfaces,
            string memberTypeName,
            bool declareInterface = false)
        {
            var member = new DeclaredMemberLayout(
                "n",
                memberTypeName,
                isPointer: false,
                isReference: false,
                isStatic: false,
                arrayDimensions: new DeclaredArrayDimension[0],
                bitSize: 32,
                bitSizeX64: null,
                bitOffset: declaredMemberBitOffset);
            var functionBlock = new DeclaredTypeLayout(
                "FB_Synthetic",
                bitSize: typeBits,
                baseTypeName: null,
                baseTypeIsPointer: false,
                arrayDimensions: new DeclaredArrayDimension[0],
                isFunctionBlock: true,
                packMode: null,
                members: new[] { member },
                implementedInterfaces: implementedInterfaces);
            var types = new List<DeclaredTypeLayout> { functionBlock };
            if (declareInterface)
            {
                types.Add(new DeclaredTypeLayout(
                    memberTypeName,
                    bitSize: 32,
                    baseTypeName: "PVOID",
                    baseTypeIsPointer: false,
                    arrayDimensions: new DeclaredArrayDimension[0],
                    isFunctionBlock: false,
                    packMode: null,
                    members: new DeclaredMemberLayout[0],
                    isInterface: true));
            }

            return LayoutOracle.Compare(new ModuleLayout("Synthetic", targetPlatform, types));
        }

        // A type name xStunit has no size rule for is a gap in the interpreter,
        // not a disagreement about a rule, and must not be counted as either
        // conformance or non-conformance. The COM server pointer TwinCAT hangs
        // off PlcAppSystemInfo is one such name.
        [Fact]
        public void Compare_UnknownTypeName_IsReportedAsUnsupported()
        {
            var finding = Assert.Single(
                Compare(X64Module).Findings, f => f.Subject == "PlcAppSystemInfo.TComSrvPtr");

            Assert.Equal(LayoutFindingKind.Unsupported, finding.Kind);
            Assert.Contains("ITComObjectServer", finding.Detail);
        }

        // TwinCAT keeps PlcTaskSystemInfo's reserved bytes out of the .tmc, so
        // TaskName is declared 32 bytes further along than the members in front
        // of it account for. Comparing it would state the width of the hole as
        // an offset disagreement, and again as the type's size - two rows that
        // measure how completely the compiler described the type rather than
        // any rule xStunit implements. Asserted as the single finding the type
        // produces, so a run that reported them anyway goes red.
        [Theory]
        [InlineData(X86Module)]
        [InlineData(X64Module)]
        public void Compare_TypeDescribedWithAHole_StopsAtTheDisplacedMember(string module)
        {
            var finding = Assert.Single(Compare(module).Findings, f => f.TypeName == "PlcTaskSystemInfo");

            Assert.Equal(LayoutFindingKind.NotCompared, finding.Kind);
            Assert.Equal("PlcTaskSystemInfo.TaskName", finding.Subject);
        }

        // A hole costs exactly the rows it displaces. Every member declared in
        // front of one is still scored, so a width xStunit and the compiler
        // disagree about there still reaches the conformance claim - dropping
        // the whole type instead would carry every rule its earlier members
        // exercise out of the claim along with the hole.
        [Fact]
        public void Compare_TypeDescribedWithAHole_StillScoresTheMembersInFrontOfIt()
        {
            var report = CompareHoledType(declaredLeadInBits: 16);

            var mismatch = Assert.Single(report.Mismatches);
            Assert.Equal(LayoutFindingKind.MemberSize, mismatch.Kind);
            Assert.Equal("ST_Holed.leadIn", mismatch.Subject);
        }

        // Nothing behind the hole is comparable: the compiler placed those
        // members past bytes it never described, and the type's size follows
        // from where they landed.
        [Fact]
        public void Compare_TypeDescribedWithAHole_ReportsNoOffsetOrSizeBehindIt()
        {
            var report = CompareHoledType(declaredLeadInBits: 32);

            var finding = Assert.Single(report.Findings);
            Assert.Equal(LayoutFindingKind.NotCompared, finding.Kind);
            Assert.Equal("ST_Holed.displaced", finding.Subject);
        }

        // A hand-built type with PlcTaskSystemInfo's shape: two DINTs with 60
        // bytes between them that no member accounts for. leadIn's declared
        // width is the dial - state it wrong and a disagreement sits in front
        // of the hole.
        private static LayoutReport CompareHoledType(int declaredLeadInBits)
        {
            var type = new DeclaredTypeLayout(
                "ST_Holed",
                bitSize: 544,
                baseTypeName: null,
                baseTypeIsPointer: false,
                arrayDimensions: new DeclaredArrayDimension[0],
                isFunctionBlock: false,
                packMode: null,
                members: new[]
                {
                    DeclaredDint("leadIn", declaredLeadInBits, bitOffset: 0),
                    DeclaredDint("displaced", 32, bitOffset: 512),
                });

            return LayoutOracle.Compare(new ModuleLayout("Synthetic", "TwinCAT RT (x86)", new[] { type }));
        }

        private static DeclaredMemberLayout DeclaredDint(string name, int bitSize, int bitOffset) =>
            new DeclaredMemberLayout(
                name,
                "DINT",
                isPointer: false,
                isReference: false,
                isStatic: false,
                arrayDimensions: new DeclaredArrayDimension[0],
                bitSize: bitSize,
                bitSizeX64: null,
                bitOffset: bitOffset);

        // Neither committed .tmc reaches a member carrying both widths - the one
        // that does sits behind a type name xStunit cannot size - so the choice
        // between them is pinned against a hand-built module instead. The
        // module states a pointer at its target's own width, and so does the
        // layout math, so reading the wrong one of the two declared widths
        // reports a disagreement in either direction: read 32 on the 64-bit
        // target and every pointer "conforms" at half its real width, which is
        // the one mistake this whole harness exists to make impossible.
        [Theory]
        [InlineData("TwinCAT RT (x64)", 64)]
        [InlineData("TwinCAT RT (x86)", 32)]
        public void Compare_MemberWithTwoWidths_ComparesTheOneTheTargetUses(string targetPlatform, int declaredBits)
        {
            var member = new DeclaredMemberLayout(
                "TComSrvPtr",
                "ITComObjectServer",
                isPointer: true,
                isReference: false,
                isStatic: false,
                arrayDimensions: new DeclaredArrayDimension[0],
                bitSize: 32,
                bitSizeX64: 64,
                bitOffset: 0);
            var type = new DeclaredTypeLayout(
                "ST_Holder",
                bitSize: declaredBits,
                baseTypeName: null,
                baseTypeIsPointer: false,
                arrayDimensions: new DeclaredArrayDimension[0],
                isFunctionBlock: false,
                packMode: null,
                members: new[] { member });

            var report = LayoutOracle.Compare(new ModuleLayout("Synthetic", targetPlatform, new[] { type }));

            Assert.Empty(report.Mismatches);
        }

        private static string Normalize(string text) => text.Replace("\r\n", "\n");
    }
}
