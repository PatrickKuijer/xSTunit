using System.IO;
using System.Linq;
using xStunit.Interpreter.Conformance;
using xStunit.Parser;
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

        // The same math on a 64-bit target does not: SizeOfType hardcodes a
        // 4-byte pointer, so every pointer member is half the declared width
        // and everything after it slides. Pinned as a known non-conformance -
        // when a target-platform concept lands, this test is the one that
        // should change.
        [Fact]
        public void Compare_X64Module_ReportsPointerMembersAsHalfWidth()
        {
            var report = Compare(X64Module);

            var pointerSizes = report.Mismatches
                .Where(f => f.Kind == LayoutFindingKind.MemberSize)
                .ToList();

            Assert.NotEmpty(pointerSizes);
            Assert.All(pointerSizes, f =>
            {
                Assert.Equal(64, f.DeclaredBits);
                Assert.Equal(32, f.ComputedBits);
            });
        }

        // A union's members all sit at offset 0, which xStunit has no model
        // for. Reported as out of scope rather than as a wall of offset
        // mismatches that would drown the real findings.
        [Fact]
        public void Compare_Union_IsReportedAsNotCompared()
        {
            var finding = Assert.Single(
                Compare(X86Module).Findings, f => f.TypeName == "U_ExpectedOrActual");

            Assert.Equal(LayoutFindingKind.NotCompared, finding.Kind);
            Assert.Contains("union", finding.Detail);
        }

        // A function block's members start past an instance header the compiler
        // adds and xStunit does not model, so comparing one would report a
        // constant offset shift that says nothing about any layout rule.
        [Fact]
        public void Compare_FunctionBlock_IsReportedAsNotCompared()
        {
            var finding = Assert.Single(
                Compare(X64Module).Findings, f => f.TypeName == "FB_AddLrealInt");

            Assert.Equal(LayoutFindingKind.NotCompared, finding.Kind);
            Assert.Contains("function block", finding.Detail);
        }

        // A type name xStunit has no size rule for is a gap in the interpreter,
        // not a disagreement about a rule, and must not be counted as either
        // conformance or non-conformance.
        [Fact]
        public void Compare_UnknownTypeName_IsReportedAsUnsupported()
        {
            var finding = Assert.Single(
                Compare(X64Module).Findings, f => f.Subject == "PlcAppSystemInfo.ObjId");

            Assert.Equal(LayoutFindingKind.Unsupported, finding.Kind);
            Assert.Contains("OTCID", finding.Detail);
        }

        // Neither committed .tmc reaches a member carrying both widths - the one
        // that does sits behind a type name xStunit cannot size - so the choice
        // between them is pinned against a hand-built module instead. Read the
        // 32-bit value on a 64-bit target and every pointer silently "conforms"
        // at half its real width, which is the one mistake this whole harness
        // exists to make impossible.
        [Theory]
        [InlineData("TwinCAT RT (x64)", 64)]
        [InlineData("TwinCAT RT (x86)", 32)]
        public void Compare_MemberWithTwoWidths_ComparesTheOneTheTargetUses(string targetPlatform, int expectedDeclaredBits)
        {
            var member = new DeclaredMemberLayout(
                "TComSrvPtr",
                "ITComObjectServer",
                isPointer: true,
                isStatic: false,
                arrayDimensions: new DeclaredArrayDimension[0],
                bitSize: 32,
                bitSizeX64: 64,
                bitOffset: 0);
            var type = new DeclaredTypeLayout(
                "ST_Holder",
                bitSize: 32,
                baseTypeName: null,
                baseTypeIsPointer: false,
                arrayDimensions: new DeclaredArrayDimension[0],
                isFunctionBlock: false,
                members: new[] { member });

            var report = LayoutOracle.Compare(new ModuleLayout("Synthetic", targetPlatform, new[] { type }));

            var sizes = report.Mismatches.Where(f => f.Kind == LayoutFindingKind.MemberSize).ToList();
            if (expectedDeclaredBits == 32)
            {
                Assert.Empty(sizes);
                return;
            }

            var finding = Assert.Single(sizes);
            Assert.Equal(expectedDeclaredBits, finding.DeclaredBits);
            Assert.Equal(32, finding.ComputedBits);
        }

        private static string Normalize(string text) => text.Replace("\r\n", "\n");
    }
}
