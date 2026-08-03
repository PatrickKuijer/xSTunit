using System.Collections.Generic;
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

        // PlcTaskSystemInfo is described with a hole. Its declared members stop
        // at byte 32 and TaskName is declared at byte 64, the 32 bytes between
        // them being a reserved array TwinCAT keeps out of the .tmc, so every
        // member past the hole is displaced by definition and the type's size
        // with it. Those rows measure how completely the compiler described the
        // type, not whether xStunit places fields correctly, and the oracle has
        // no rule for spotting one yet. Nothing else is excluded, so a real
        // disagreement anywhere else still reaches the assertions below.
        private static IEnumerable<LayoutFinding> MismatchesAboutALayoutRule(LayoutReport report) =>
            report.Mismatches.Where(f => f.TypeName != "PlcTaskSystemInfo");

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

            Assert.Empty(MismatchesAboutALayoutRule(report));
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

            Assert.Empty(MismatchesAboutALayoutRule(report));
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
        // conformance or non-conformance. DT, the abbreviated spelling of
        // DATE_AND_TIME, is one such name even though the spelled-out form is
        // sized.
        [Fact]
        public void Compare_UnknownTypeName_IsReportedAsUnsupported()
        {
            var finding = Assert.Single(
                Compare(X64Module).Findings, f => f.Subject == "PlcAppSystemInfo.AppTimestamp");

            Assert.Equal(LayoutFindingKind.Unsupported, finding.Kind);
            Assert.Contains("DT", finding.Detail);
        }

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
                packMode: 0,
                members: new[] { member });

            var report = LayoutOracle.Compare(new ModuleLayout("Synthetic", targetPlatform, new[] { type }));

            Assert.Empty(report.Mismatches);
        }

        private static string Normalize(string text) => text.Replace("\r\n", "\n");
    }
}
