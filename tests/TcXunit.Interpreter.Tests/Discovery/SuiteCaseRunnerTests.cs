using System;
using System.IO;
using System.Linq;
using TcXunit.Interpreter;
using Xunit;

namespace TcXunit.Interpreter.Tests
{
    public class SuiteCaseRunnerTests
    {
        private static readonly string FixturePouDir = TestFixtures.FbCounterFixtureDir();

        private const string RejectedPou = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <POU Name=""FB_Unsupported"" Id=""{a1b2c3d4-0001-4a1a-8b1b-0000000000ff}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_Unsupported
VAR
	value : INT;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[Tc2_System.SOME_CALL();]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        private const string MalformedXmlPou = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <POU Name=""FB_Broken"" Id=""{a1b2c3d4-0002-4a1a-8b1b-0000000000ff}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_Broken
VAR
	value : INT;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[value := 1;]]></ST>
    </Implementation>
</TcPlcObject>";

        private const string MissingStBodyPou = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <POU Name=""FB_NoBody"" Id=""{a1b2c3d4-0003-4a1a-8b1b-0000000000ff}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_NoBody
VAR
	value : INT;
END_VAR]]></Declaration>
  </POU>
</TcPlcObject>";

        private static string StructDutXml(string typeName, string fieldDecl) => $@"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <DUT Name=""{typeName}"" Id=""{{a1b2c3d4-0004-4a1a-8b1b-0000000000ff}}"">
    <Declaration><![CDATA[TYPE {typeName} :
STRUCT
	{fieldDecl} : INT;
END_STRUCT
END_TYPE]]></Declaration>
  </DUT>
</TcPlcObject>";

        [Fact]
        public void DiscoverCases_FixtureProject_ListsAllFourCasesUnderFbCounterTests()
        {
            var cases = SuiteCaseRunner.DiscoverCases(FixturePouDir);

            Assert.Equal(
                new[]
                {
                    ("FB_CounterTests", "CounterStartsAtZero"),
                    ("FB_CounterTests", "IncrementAddsDelta"),
                    ("FB_CounterTests", "DecrementClampsAtZero"),
                    ("FB_CounterTests", "ClampedCounterIncrementRespectsCeiling"),
                },
                cases.Select(c => (c.SuiteName, c.CaseName)));
        }

        [Fact]
        public void RunCase_SpecificCase_ReturnsOnlyThatCasesResult()
        {
            var result = SuiteCaseRunner.RunCase(FixturePouDir, "FB_CounterTests", "IncrementAddsDelta");

            Assert.Equal("IncrementAddsDelta", result.Name);
            Assert.True(result.Passed, result.ToString());
        }

        [Fact]
        public void DiscoverCases_SuiteAndFbInSeparateDirectories_DiscoversAcrossBoth()
        {
            var sourceDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-source-" + Guid.NewGuid()));
            var testsDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-tests-" + Guid.NewGuid()));
            try
            {
                CopyFixtureFile(sourceDir.FullName, "FB_Counter.TcPOU");
                CopyFixtureFile(sourceDir.FullName, "FB_ClampedCounter.TcPOU");
                CopyFixtureFile(testsDir.FullName, "FB_CounterTests.TcPOU");

                var cases = SuiteCaseRunner.DiscoverCases(new[] { sourceDir.FullName, testsDir.FullName });

                Assert.Equal(
                    new[]
                    {
                        ("FB_CounterTests", "CounterStartsAtZero"),
                        ("FB_CounterTests", "IncrementAddsDelta"),
                        ("FB_CounterTests", "DecrementClampsAtZero"),
                        ("FB_CounterTests", "ClampedCounterIncrementRespectsCeiling"),
                    },
                    cases.Select(c => (c.SuiteName, c.CaseName)));

                var result = SuiteCaseRunner.RunCase(
                    new[] { sourceDir.FullName, testsDir.FullName }, "FB_CounterTests", "IncrementAddsDelta");
                Assert.True(result.Passed, result.ToString());
            }
            finally
            {
                Directory.Delete(sourceDir.FullName, recursive: true);
                Directory.Delete(testsDir.FullName, recursive: true);
            }
        }

        [Fact]
        public void DiscoverCases_DuplicateTypeNameAcrossDirectories_DoesNotThrowAndSurfacesAsFailingCase()
        {
            // TcXunit-qxp.1: a duplicate POU type name across merged
            // directories must not crash discovery of every other suite in
            // the scan (unlike CliRunner.Run, which can afford to abort the
            // whole process) - it must be reported the same way other
            // structural discovery failures in this file are, as a synthetic
            // failing case.
            var dirA = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-dupA-" + Guid.NewGuid()));
            var dirB = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-dupB-" + Guid.NewGuid()));
            try
            {
                CopyFixtureFile(dirA.FullName, "FB_Counter.TcPOU");
                CopyFixtureFile(dirB.FullName, "FB_Counter.TcPOU");

                var cases = SuiteCaseRunner.DiscoverCases(new[] { dirA.FullName, dirB.FullName });

                var skip = Assert.Single(cases, c => c.SuiteName == "FB_Counter" && c.CaseName == "(parse error)");

                var result = SuiteCaseRunner.RunCase(
                    new[] { dirA.FullName, dirB.FullName }, skip.SuiteName, skip.CaseName);
                Assert.False(result.Passed);
                Assert.Contains("duplicate POU type", result.ToString());
                Assert.Contains("FB_Counter", result.ToString());
            }
            finally
            {
                Directory.Delete(dirA.FullName, recursive: true);
                Directory.Delete(dirB.FullName, recursive: true);
            }
        }

        [Fact]
        public void DiscoverCases_DuplicateStructTypeNameAcrossDirectories_DoesNotThrowAndSurfacesAsFailingCase()
        {
            // TcXunit-qxp.1: same rationale as the POU duplicate case above -
            // a duplicate STRUCT name must not crash discovery of every other
            // suite; it must surface as its own synthetic failing case
            // instead.
            var dirA = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-dutdupA-" + Guid.NewGuid()));
            var dirB = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-dutdupB-" + Guid.NewGuid()));
            try
            {
                var pathA = Path.Combine(dirA.FullName, "ST_Shared.TcDUT");
                var pathB = Path.Combine(dirB.FullName, "ST_Shared.TcDUT");
                File.WriteAllText(pathA, StructDutXml("ST_Shared", "fieldA"));
                File.WriteAllText(pathB, StructDutXml("ST_Shared", "fieldB"));
                foreach (var file in Directory.GetFiles(FixturePouDir, "*.TcPOU"))
                    File.Copy(file, Path.Combine(dirA.FullName, Path.GetFileName(file)));

                var cases = SuiteCaseRunner.DiscoverCases(new[] { dirA.FullName, dirB.FullName });

                var skip = Assert.Single(cases, c => c.SuiteName == "ST_Shared" && c.CaseName == "(parse error)");

                var result = SuiteCaseRunner.RunCase(
                    new[] { dirA.FullName, dirB.FullName }, skip.SuiteName, skip.CaseName);
                Assert.False(result.Passed);
                Assert.Contains("duplicate STRUCT type", result.ToString());
                Assert.Contains("ST_Shared", result.ToString());

                // Unrelated suites in the merged set must still be
                // discoverable/runnable despite the STRUCT-name conflict.
                Assert.Contains(
                    cases,
                    c => c.SuiteName == "FB_CounterTests" && c.CaseName == "CounterStartsAtZero");
            }
            finally
            {
                Directory.Delete(dirA.FullName, recursive: true);
                Directory.Delete(dirB.FullName, recursive: true);
            }
        }

        private static string GvlXml(string gvlName, string varName) => $@"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <GVL Name=""{gvlName}"" Id=""{{a1b2c3d4-0006-4a1a-8b1b-0000000000ff}}"">
    <Declaration><![CDATA[VAR_GLOBAL
	{varName} : INT;
END_VAR]]></Declaration>
  </GVL>
</TcPlcObject>";

        [Fact]
        public void DiscoverCases_DuplicateGvlNameAcrossDirectories_DoesNotThrowAndSurfacesAsFailingCase()
        {
            // TcXunit-qxp.1: same rationale as the POU/STRUCT duplicate cases
            // above - a duplicate GVL name must not crash discovery of every
            // other suite; it must surface as its own synthetic failing case
            // instead.
            var dirA = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-gvldupA-" + Guid.NewGuid()));
            var dirB = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-gvldupB-" + Guid.NewGuid()));
            try
            {
                File.WriteAllText(Path.Combine(dirA.FullName, "gShared.TcGVL"), GvlXml("gShared", "fieldA"));
                File.WriteAllText(Path.Combine(dirB.FullName, "gShared.TcGVL"), GvlXml("gShared", "fieldB"));
                foreach (var file in Directory.GetFiles(FixturePouDir, "*.TcPOU"))
                    File.Copy(file, Path.Combine(dirA.FullName, Path.GetFileName(file)));

                var cases = SuiteCaseRunner.DiscoverCases(new[] { dirA.FullName, dirB.FullName });

                var skip = Assert.Single(cases, c => c.SuiteName == "gShared" && c.CaseName == "(parse error)");

                var result = SuiteCaseRunner.RunCase(
                    new[] { dirA.FullName, dirB.FullName }, skip.SuiteName, skip.CaseName);
                Assert.False(result.Passed);
                Assert.Contains("duplicate GVL", result.ToString());
                Assert.Contains("gShared", result.ToString());

                // Unrelated suites in the merged set must still be
                // discoverable/runnable despite the GVL-name conflict.
                Assert.Contains(
                    cases,
                    c => c.SuiteName == "FB_CounterTests" && c.CaseName == "CounterStartsAtZero");
            }
            finally
            {
                Directory.Delete(dirA.FullName, recursive: true);
                Directory.Delete(dirB.FullName, recursive: true);
            }
        }

        [Fact]
        public void DiscoverCases_SingleStructTypeNameAcrossDirectories_DoesNotThrow()
        {
            var dirA = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-dutokA-" + Guid.NewGuid()));
            var dirB = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-dutokB-" + Guid.NewGuid()));
            try
            {
                File.WriteAllText(Path.Combine(dirA.FullName, "ST_Shared.TcDUT"), StructDutXml("ST_Shared", "fieldA"));

                var cases = SuiteCaseRunner.DiscoverCases(new[] { dirA.FullName, dirB.FullName });

                // No suites declared, but the DUT must have parsed without
                // triggering the duplicate-struct check.
                Assert.Empty(cases);
            }
            finally
            {
                Directory.Delete(dirA.FullName, recursive: true);
                Directory.Delete(dirB.FullName, recursive: true);
            }
        }

        private const string PointDutXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <DUT Name=""ST_Point"" Id=""{a1b2c3d4-0007-4a1a-8b1b-0000000000ff}"">
    <Declaration><![CDATA[TYPE ST_Point :
STRUCT
	x : INT;
	y : INT;
END_STRUCT
END_TYPE]]></Declaration>
  </DUT>
</TcPlcObject>";

        private const string LineDutXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <DUT Name=""ST_Line"" Id=""{a1b2c3d4-0008-4a1a-8b1b-0000000000ff}"">
    <Declaration><![CDATA[TYPE ST_Line :
STRUCT
	start : ST_Point;
	stop : ST_Point;
END_STRUCT
END_TYPE]]></Declaration>
  </DUT>
</TcPlcObject>";

        private const string LineTestsPouXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <POU Name=""FB_LineTests"" Id=""{a1b2c3d4-0009-4a1a-8b1b-0000000000ff}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_LineTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[NestedFieldAccessOnDutStructWorks();]]></ST>
    </Implementation>
    <Method Name=""NestedFieldAccessOnDutStructWorks"" Id=""{a1b2c3d4-0009-4a1a-8b1b-000000000002}"">
      <Declaration><![CDATA[METHOD PRIVATE NestedFieldAccessOnDutStructWorks
VAR
	line : ST_Line;
END_VAR
]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('NestedFieldAccessOnDutStructWorks');

line.start.x := 3;
line.start.y := 4;
line.stop.x := 30;

AssertEquals_INT(Expected := 7,
                  Actual := line.start.x + line.start.y,
                  Message := 'DUT struct field access');

AssertEquals_INT(Expected := 30,
                  Actual := line.stop.x,
                  Message := 'nested struct-of-struct DUT field access');

TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";

        [Fact]
        public void DiscoverCases_TcDutStructType_ResolvesAndSupportsNestedFieldAccess()
        {
            // Regression test for the bug fixed alongside DutStructLoader's
            // STRUCT-DUT wiring: before that fix, a VAR of a struct type only
            // declared via a .TcDUT file was unresolved in TypeRegistry and
            // defaulted to Int32 0, so any field access on it (even
            // "line.start.x") threw "Cannot access fields on Int32" instead
            // of resolving through TypeRegistry.GetStruct.
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-dutfields-" + Guid.NewGuid()));
            try
            {
                File.WriteAllText(Path.Combine(tempDir.FullName, "ST_Point.TcDUT"), PointDutXml);
                File.WriteAllText(Path.Combine(tempDir.FullName, "ST_Line.TcDUT"), LineDutXml);
                File.WriteAllText(Path.Combine(tempDir.FullName, "FB_LineTests.TcPOU"), LineTestsPouXml);

                var cases = SuiteCaseRunner.DiscoverCases(tempDir.FullName);

                Assert.Equal(
                    new[] { ("FB_LineTests", "NestedFieldAccessOnDutStructWorks") },
                    cases.Select(c => (c.SuiteName, c.CaseName)));

                var result = SuiteCaseRunner.RunCase(
                    tempDir.FullName, "FB_LineTests", "NestedFieldAccessOnDutStructWorks");

                Assert.True(result.Passed, result.ToString());
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }

        private const string SampleValueStringAliasDutXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <DUT Name=""T_SampleValueString"" Id=""{a1b2c3d4-000b-4a1a-8b1b-0000000000ff}"">
    <Declaration><![CDATA[TYPE T_SampleValueString : STRING(80);
END_TYPE]]></Declaration>
  </DUT>
</TcPlcObject>";

        private const string ProcessGuardPouXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <POU Name=""FB_ProcessGuard"" Id=""{a1b2c3d4-000c-4a1a-8b1b-0000000000ff}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_ProcessGuard
VAR
	saLowBound  : ARRAY[1..4] OF T_SampleValueString;
	saHighBound : ARRAY[1..4] OF T_SampleValueString;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[]]></ST>
    </Implementation>
    <Method Name=""SomeMethod"" Id=""{a1b2c3d4-000c-4a1a-8b1b-000000000002}"">
      <Declaration><![CDATA[METHOD PUBLIC SomeMethod]]></Declaration>
      <Implementation>
        <ST><![CDATA[saLowBound[1] := 'x';]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";

        private const string ProcessGuardTestsPouXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <POU Name=""FB_ProcessGuardTests"" Id=""{a1b2c3d4-000d-4a1a-8b1b-0000000000ff}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_ProcessGuardTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[ArrayOfStringAliasDutFieldIsAssignable();]]></ST>
    </Implementation>
    <Method Name=""ArrayOfStringAliasDutFieldIsAssignable"" Id=""{a1b2c3d4-000d-4a1a-8b1b-000000000002}"">
      <Declaration><![CDATA[METHOD PRIVATE ArrayOfStringAliasDutFieldIsAssignable
VAR
	guard : FB_ProcessGuard;
END_VAR
]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('ArrayOfStringAliasDutFieldIsAssignable');

guard.SomeMethod();

AssertEquals_STRING(Expected := 'x',
                     Actual := guard.saLowBound[1],
                     Message := 'array element of custom STRING-alias DUT');

TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";

        [Fact]
        public void DiscoverCases_ArrayOfCustomStringAliasDutField_ResolvesAndSupportsReadWrite()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-arrayaliasdut-" + Guid.NewGuid()));
            try
            {
                File.WriteAllText(Path.Combine(tempDir.FullName, "T_SampleValueString.TcDUT"), SampleValueStringAliasDutXml);
                File.WriteAllText(Path.Combine(tempDir.FullName, "FB_ProcessGuard.TcPOU"), ProcessGuardPouXml);
                File.WriteAllText(Path.Combine(tempDir.FullName, "FB_ProcessGuardTests.TcPOU"), ProcessGuardTestsPouXml);

                var cases = SuiteCaseRunner.DiscoverCases(tempDir.FullName);
                Assert.Equal(
                    new[] { ("FB_ProcessGuardTests", "ArrayOfStringAliasDutFieldIsAssignable") },
                    cases.Select(c => (c.SuiteName, c.CaseName)));

                var result = SuiteCaseRunner.RunCase(
                    tempDir.FullName, "FB_ProcessGuardTests", "ArrayOfStringAliasDutFieldIsAssignable");

                Assert.True(result.Passed, result.ToString());
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }

        private const string WidgetValueKindDutXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <DUT Name=""eWidgetValueKind"" Id=""{a1b2c3d4-000a-4a1a-8b1b-0000000000ff}"">
    <Declaration><![CDATA[TYPE eWidgetValueKind :
(
	TypeBool,
	TypeByte,
	TypeInt,
	TypeDint,
	TypeLreal
);
END_TYPE]]></Declaration>
  </DUT>
</TcPlcObject>";

        private const string EnumTestsPouXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <POU Name=""FB_EnumTests"" Id=""{a1b2c3d4-000b-4a1a-8b1b-0000000000ff}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_EnumTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[QualifiedEnumLiteralResolvesToMemberValue();]]></ST>
    </Implementation>
    <Method Name=""QualifiedEnumLiteralResolvesToMemberValue"" Id=""{a1b2c3d4-000b-4a1a-8b1b-000000000002}"">
      <Declaration><![CDATA[METHOD PRIVATE QualifiedEnumLiteralResolvesToMemberValue
VAR
	actual : INT;
END_VAR
]]></Declaration>
      <Implementation>
        <ST><![CDATA[TEST('QualifiedEnumLiteralResolvesToMemberValue');

actual := eWidgetValueKind.TypeLreal;

AssertEquals_INT(Expected := 4,
                  Actual := actual,
                  Message := 'qualified enum DUT literal resolves to member ordinal');

TEST_FINISHED();]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";

        [Fact]
        public void DiscoverCases_QualifiedEnumDutLiteral_ResolvesAndAssertsMemberValue()
        {
            // TcXunit-rk3 end-to-end regression: a fully-qualified ENUM DUT
            // literal (eWidgetValueKind.TypeLreal) referenced from a
            // suite method body must resolve through DutEnumLoader's member
            // table registered on TypeRegistry, unblocking suites that
            // reference a qualified ENUM DUT literal from a nested FB
            // method body.
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-enumliteral-" + Guid.NewGuid()));
            try
            {
                File.WriteAllText(
                    Path.Combine(tempDir.FullName, "eWidgetValueKind.TcDUT"), WidgetValueKindDutXml);
                File.WriteAllText(Path.Combine(tempDir.FullName, "FB_EnumTests.TcPOU"), EnumTestsPouXml);

                var cases = SuiteCaseRunner.DiscoverCases(tempDir.FullName);

                Assert.Equal(
                    new[] { ("FB_EnumTests", "QualifiedEnumLiteralResolvesToMemberValue") },
                    cases.Select(c => (c.SuiteName, c.CaseName)));

                var result = SuiteCaseRunner.RunCase(
                    tempDir.FullName, "FB_EnumTests", "QualifiedEnumLiteralResolvesToMemberValue");

                Assert.True(result.Passed, result.ToString());
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }

        private const string MalformedXmlDut = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <DUT Name=""ST_Broken"" Id=""{a1b2c3d4-0005-4a1a-8b1b-0000000000ff}"">
    <Declaration><![CDATA[TYPE ST_Broken :
STRUCT
	value : INT;
END_STRUCT
END_TYPE]]></Declaration>
</TcPlcObject>";

        [Fact]
        public void DiscoverCases_MalformedTcDutFile_DoesNotAbortRegistryBuild()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-" + Guid.NewGuid()));
            try
            {
                foreach (var file in Directory.GetFiles(FixturePouDir, "*.TcPOU"))
                    File.Copy(file, Path.Combine(tempDir.FullName, Path.GetFileName(file)));
                File.WriteAllText(Path.Combine(tempDir.FullName, "ST_Broken.TcDUT"), MalformedXmlDut);

                var cases = SuiteCaseRunner.DiscoverCases(tempDir.FullName);

                // The malformed .TcDUT must not throw out of registry build; the
                // rest of the directory's suites still run normally.
                Assert.Contains(
                    cases,
                    c => c.SuiteName == "FB_CounterTests" && c.CaseName == "IncrementAddsDelta");
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }

        private static void CopyFixtureFile(string destDir, string fileName) =>
            File.Copy(Path.Combine(FixturePouDir, fileName), Path.Combine(destDir, fileName));

        [Fact]
        public void DiscoverCases_SameNamedRejectedPouInTwoDirectories_SurfacesBothWithoutCollision()
        {
            var dirA = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-rejA-" + Guid.NewGuid()));
            var dirB = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-rejB-" + Guid.NewGuid()));
            try
            {
                CopyFixtureFile(dirA.FullName, "FB_CounterTests.TcPOU");
                CopyFixtureFile(dirA.FullName, "FB_Counter.TcPOU");
                CopyFixtureFile(dirA.FullName, "FB_ClampedCounter.TcPOU");
                File.WriteAllText(Path.Combine(dirA.FullName, "FB_Unsupported.TcPOU"), RejectedPou);
                File.WriteAllText(Path.Combine(dirB.FullName, "FB_Unsupported.TcPOU"), RejectedPou);

                var cases = SuiteCaseRunner.DiscoverCases(new[] { dirA.FullName, dirB.FullName });

                var skippedCases = cases.Where(c => c.CaseName == "(parse error)").ToList();

                // Two same-named rejected POUs across the two directories must
                // surface as two distinct SuiteCase entries (unique full-path
                // keys), not collapse into one ambiguous duplicate.
                Assert.Equal(2, skippedCases.Count);
                Assert.Equal(skippedCases.Select(c => c.SuiteName).Distinct().Count(), skippedCases.Count);

                var expectedPathA = Path.Combine(dirA.FullName, "FB_Unsupported.TcPOU");
                var expectedPathB = Path.Combine(dirB.FullName, "FB_Unsupported.TcPOU");
                Assert.Contains(skippedCases, c => c.SuiteName == expectedPathA);
                Assert.Contains(skippedCases, c => c.SuiteName == expectedPathB);

                var resultA = SuiteCaseRunner.RunCase(
                    new[] { dirA.FullName, dirB.FullName }, expectedPathA, "(parse error)");
                var resultB = SuiteCaseRunner.RunCase(
                    new[] { dirA.FullName, dirB.FullName }, expectedPathB, "(parse error)");

                Assert.False(resultA.Passed);
                Assert.False(resultB.Passed);
                Assert.Contains("Tc2_System", resultA.ToString());
                Assert.Contains("Tc2_System", resultB.ToString());
            }
            finally
            {
                Directory.Delete(dirA.FullName, recursive: true);
                Directory.Delete(dirB.FullName, recursive: true);
            }
        }

        [Fact]
        public void DiscoverCases_UnparseablePou_SurfacesAsFailingCaseInsteadOfVanishing()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-" + Guid.NewGuid()));
            try
            {
                foreach (var file in Directory.GetFiles(FixturePouDir, "*.TcPOU"))
                    File.Copy(file, Path.Combine(tempDir.FullName, Path.GetFileName(file)));
                File.WriteAllText(Path.Combine(tempDir.FullName, "FB_Unsupported.TcPOU"), RejectedPou);

                var cases = SuiteCaseRunner.DiscoverCases(tempDir.FullName);

                var skippedCase = Assert.Single(
                    cases, c => c.CaseName == "(parse error)" && c.SuiteName.Contains("FB_Unsupported"));
                Assert.Equal(Path.Combine(tempDir.FullName, "FB_Unsupported.TcPOU"), skippedCase.SuiteName);

                var result = SuiteCaseRunner.RunCase(tempDir.FullName, skippedCase.SuiteName, "(parse error)");

                Assert.False(result.Passed);
                Assert.Contains("Tc2_System", result.ToString());
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }

        [Fact]
        public void RunCase_ParseErrorCaseWithNoMatchingSkipEntry_ThrowsInvalidOperationExceptionNotNullReference()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-" + Guid.NewGuid()));
            try
            {
                foreach (var file in Directory.GetFiles(FixturePouDir, "*.TcPOU"))
                    File.Copy(file, Path.Combine(tempDir.FullName, Path.GetFileName(file)));
                File.WriteAllText(Path.Combine(tempDir.FullName, "FB_Unsupported.TcPOU"), RejectedPou);

                // Stale/mismatched pair: caseName is the parse-error sentinel but
                // suiteName does not match any entry in the current skip set
                // (e.g. the skip set changed between DiscoverCases and RunCase).
                // suiteName was never added to the TypeRegistry, so falling
                // through to engine.RunSuite would previously throw a
                // NullReferenceException instead of a clear diagnostic.
                var ex = Assert.Throws<InvalidOperationException>(
                    () => SuiteCaseRunner.RunCase(tempDir.FullName, "FB_DoesNotExist", "(parse error)"));

                Assert.Contains("FB_DoesNotExist", ex.Message);
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }

        [Fact]
        public void DiscoverCases_MalformedXmlPou_SurfacesAsFailingCaseInsteadOfAbortingScan()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-" + Guid.NewGuid()));
            try
            {
                foreach (var file in Directory.GetFiles(FixturePouDir, "*.TcPOU"))
                    File.Copy(file, Path.Combine(tempDir.FullName, Path.GetFileName(file)));
                File.WriteAllText(Path.Combine(tempDir.FullName, "FB_Broken.TcPOU"), MalformedXmlPou);

                var cases = SuiteCaseRunner.DiscoverCases(tempDir.FullName);

                Assert.Contains(
                    cases, c => c.SuiteName.Contains("FB_Broken") && c.CaseName == "(parse error)");
                Assert.Contains(
                    cases,
                    c => c.SuiteName == "FB_CounterTests" && c.CaseName == "IncrementAddsDelta");
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }

        [Fact]
        public void DiscoverCases_PouMissingStBody_SurfacesAsFailingCaseInsteadOfAbortingScan()
        {
            var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tcxunit-" + Guid.NewGuid()));
            try
            {
                foreach (var file in Directory.GetFiles(FixturePouDir, "*.TcPOU"))
                    File.Copy(file, Path.Combine(tempDir.FullName, Path.GetFileName(file)));
                File.WriteAllText(Path.Combine(tempDir.FullName, "FB_NoBody.TcPOU"), MissingStBodyPou);

                var cases = SuiteCaseRunner.DiscoverCases(tempDir.FullName);

                Assert.Contains(
                    cases, c => c.SuiteName.Contains("FB_NoBody") && c.CaseName == "(parse error)");
                Assert.Contains(
                    cases,
                    c => c.SuiteName == "FB_CounterTests" && c.CaseName == "IncrementAddsDelta");
            }
            finally
            {
                Directory.Delete(tempDir.FullName, recursive: true);
            }
        }
    }
}
