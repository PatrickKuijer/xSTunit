using System;
using System.IO;
using System.Linq;
using System.Text;
using xStunit.Interpreter;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // An FB that declares IMPLEMENTS of a loaded interface must provide every
    // method and every property accessor that interface declares, or it is
    // reported as a skipped file and never enters the registry. TwinCAT
    // refuses to compile such an FB, so a green run against it would be a
    // verdict TwinCAT could never reproduce.
    public class ImplementsConformanceTests : IDisposable
    {
        private readonly string _tempDir;

        public ImplementsConformanceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "XstunitConformanceFixture_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        [Fact]
        public void Load_FbProvidingEveryMemberAndAccessor_LoadsClean()
        {
            WriteInterface("I_Sensor", methods: new[] { "Reset" }, properties: new[] { ("Active", "GS") });
            WriteFb("FB_Sensor", "IMPLEMENTS I_Sensor", methods: new[] { "Reset" }, properties: new[] { ("Active", "GS") });

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Null(workspace.Error);
            Assert.Empty(workspace.Skipped);
            Assert.NotNull(workspace.Registry.Get("FB_Sensor"));
        }

        [Fact]
        public void Load_FbMissingInterfaceMethod_SkipsFileNamingFbInterfaceAndMethod()
        {
            WriteInterface("I_Sensor", methods: new[] { "Reset", "Calibrate" });
            WriteFb("FB_Sensor", "IMPLEMENTS I_Sensor", methods: new[] { "Reset" });
            WriteFb("FB_Other", "");

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Null(workspace.Error);
            var skip = Assert.Single(workspace.Skipped);
            Assert.EndsWith("FB_Sensor.TcPOU", skip.FileKey);
            Assert.Contains("FB_Sensor", skip.Message);
            Assert.Contains("I_Sensor", skip.Message);
            Assert.Contains("Calibrate", skip.Message);
            Assert.DoesNotContain("Reset", skip.Message);
            Assert.Null(workspace.Registry.Get("FB_Sensor"));
            Assert.NotNull(workspace.Registry.Get("FB_Other"));
            Assert.DoesNotContain("FB_Sensor", workspace.PouTypes.Select(t => t.Name));
            Assert.False(workspace.FilePathsByTypeName.ContainsKey("FB_Sensor"));
        }

        [Fact]
        public void Load_FbMissingInterfaceProperty_SkipsFileNamingProperty()
        {
            WriteInterface("I_Sensor", properties: new[] { ("Active", "G"), ("DebounceTime", "G") });
            WriteFb("FB_Sensor", "IMPLEMENTS I_Sensor", properties: new[] { ("Active", "G") });

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            var skip = Assert.Single(workspace.Skipped);
            Assert.Contains("DebounceTime", skip.Message);
            Assert.DoesNotContain("Active", skip.Message);
        }

        // Accessor presence is part of the contract: a Get-only property does
        // not satisfy an interface property that also declares Set.
        [Fact]
        public void Load_FbPropertyMissingDeclaredAccessor_SkipsFileNamingPropertyAndAccessor()
        {
            WriteInterface("I_Sensor", properties: new[] { ("Active", "GS") });
            WriteFb("FB_Sensor", "IMPLEMENTS I_Sensor", properties: new[] { ("Active", "G") });

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            var skip = Assert.Single(workspace.Skipped);
            Assert.Contains("Active", skip.Message);
            Assert.Contains("Set", skip.Message);
            Assert.DoesNotContain("Get", skip.Message);
        }

        [Fact]
        public void Load_FbWithExtraAccessorBeyondInterface_LoadsClean()
        {
            WriteInterface("I_Sensor", properties: new[] { ("Active", "G") });
            WriteFb("FB_Sensor", "IMPLEMENTS I_Sensor", properties: new[] { ("Active", "GS") });

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Empty(workspace.Skipped);
        }

        [Fact]
        public void Load_MethodNameDiffersOnlyInCase_Conforms()
        {
            WriteInterface("I_Sensor", methods: new[] { "Reset" });
            WriteFb("FB_Sensor", "IMPLEMENTS I_Sensor", methods: new[] { "RESET" });

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Empty(workspace.Skipped);
        }

        // TwinCAT lets a derived FB satisfy IMPLEMENTS with members it
        // inherits, so checking only the FB's own members would reject valid
        // code.
        [Fact]
        public void Load_MembersInheritedThroughExtendsChain_Satisfy()
        {
            WriteInterface("I_Sensor", methods: new[] { "Reset" }, properties: new[] { ("Active", "GS") });
            WriteFb("FB_Base", "", methods: new[] { "Reset" });
            WriteFb("FB_Mid", "EXTENDS FB_Base", properties: new[] { ("Active", "GS") });
            WriteFb("FB_Leaf", "EXTENDS FB_Mid IMPLEMENTS I_Sensor");

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Empty(workspace.Skipped);
            Assert.NotNull(workspace.Registry.Get("FB_Leaf"));
        }

        [Fact]
        public void Load_MultipleInterfaces_ReportsMissingMembersOfEach()
        {
            WriteInterface("I_A", methods: new[] { "MA" });
            WriteInterface("I_B", methods: new[] { "MB" }, properties: new[] { ("PB", "S") });
            WriteFb("FB_Both", "IMPLEMENTS I_A, I_B");

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            var skip = Assert.Single(workspace.Skipped);
            Assert.Contains("I_A", skip.Message);
            Assert.Contains("MA", skip.Message);
            Assert.Contains("I_B", skip.Message);
            Assert.Contains("MB", skip.Message);
            Assert.Contains("PB", skip.Message);
        }

        [Fact]
        public void Load_MultipleInterfacesOneConformed_StillReportsTheOther()
        {
            WriteInterface("I_A", methods: new[] { "MA" });
            WriteInterface("I_B", methods: new[] { "MB" });
            WriteFb("FB_Both", "IMPLEMENTS I_A, I_B", methods: new[] { "MA" });

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            var skip = Assert.Single(workspace.Skipped);
            Assert.Contains("I_B", skip.Message);
            Assert.DoesNotContain("I_A", skip.Message);
        }

        // A library interface has no .TcIO here, so there is nothing to check
        // against; reporting it would fail every FB implementing one.
        [Fact]
        public void Load_InterfaceThatDoesNotResolve_IsNotChecked()
        {
            WriteFb("FB_Sensor", "IMPLEMENTS Tc2_Lib.I_Unknown");

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Empty(workspace.Skipped);
            Assert.NotNull(workspace.Registry.Get("FB_Sensor"));
        }

        [Fact]
        public void Load_LibraryQualifiedNameOfLoadedInterface_IsChecked()
        {
            WriteInterface("I_Sensor", methods: new[] { "Reset" });
            WriteFb("FB_Sensor", "IMPLEMENTS MyLib.I_Sensor");

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Contains("Reset", Assert.Single(workspace.Skipped).Message);
        }

        // Members might live in the unresolved ancestor, so reporting them
        // missing would be a guess that fails valid code.
        [Fact]
        public void Load_ExtendsChainWithUnresolvedBase_IsNotChecked()
        {
            WriteInterface("I_Sensor", methods: new[] { "Reset" });
            WriteFb("FB_Sensor", "EXTENDS Lib.FB_NotLoaded IMPLEMENTS I_Sensor");

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Empty(workspace.Skipped);
            Assert.NotNull(workspace.Registry.Get("FB_Sensor"));
        }

        // The check reports only the FB that fails to conform; whatever extends
        // it is left to the registry's ordinary unresolved-base handling.
        [Fact]
        public void Load_FbExtendingNonConformantFb_StaysLoadedWithBaseAbsent()
        {
            WriteInterface("I_Sensor", methods: new[] { "Reset" });
            WriteFb("FB_Sensor", "IMPLEMENTS I_Sensor");
            WriteFb("FB_Child", "EXTENDS FB_Sensor");

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Null(workspace.Error);
            Assert.Contains("FB_Sensor", Assert.Single(workspace.Skipped).Message);
            Assert.NotNull(workspace.Registry.Get("FB_Child"));
            Assert.Null(workspace.Registry.Get("FB_Sensor"));
        }

        // The suite base is a native boundary with no loaded definition, but
        // it is modelled with no members, so a suite implementing an
        // interface is still checkable against its own and inherited members.
        [Fact]
        public void Load_SuiteMissingInterfaceMethod_IsSkipped()
        {
            WriteInterface("I_Sensor", methods: new[] { "Reset" });
            WriteFb("FB_SuiteImpl", "EXTENDS TcUnit.FB_TestSuite IMPLEMENTS I_Sensor");

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Contains("Reset", Assert.Single(workspace.Skipped).Message);
            Assert.Null(workspace.Registry.Get("FB_SuiteImpl"));
        }

        [Theory]
        [InlineData("TcUnit.FB_TestSuite")]
        [InlineData("FB_TestSuite")]
        public void Load_SuiteThroughIntermediateBaseMissingMethod_IsSkippedWhicheverSpellingTheRootUses(string root)
        {
            WriteInterface("I_Sensor", methods: new[] { "Reset" });
            WriteFb("FB_Mid", $"EXTENDS {root}");
            WriteFb("FB_SuiteImpl", "EXTENDS FB_Mid IMPLEMENTS I_Sensor");

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            var skip = Assert.Single(workspace.Skipped);
            Assert.EndsWith("FB_SuiteImpl.TcPOU", skip.FileKey);
        }

        [Fact]
        public void Load_ConformantSuite_StaysLoadedAndDiscovered()
        {
            WriteInterface("I_Sensor", methods: new[] { "Reset" });
            WriteFb("FB_SuiteImpl", "EXTENDS TcUnit.FB_TestSuite IMPLEMENTS I_Sensor", methods: new[] { "Reset" });

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Empty(workspace.Skipped);
            Assert.Equal(
                new[] { "FB_SuiteImpl" },
                SuiteDiscovery.FindSuiteTypeNames(workspace.Registry, workspace.PouTypes.Select(t => t.Name)));
        }

        [Theory]
        [InlineData("TON")]
        [InlineData("Tc2_Standard.R_TRIG")]
        [InlineData("CTU")]
        public void Load_FbExtendingBuiltinNativeBlockMissingMethod_IsSkipped(string native)
        {
            WriteInterface("I_Sensor", methods: new[] { "Reset" });
            WriteFb("FB_Timed", $"EXTENDS {native} IMPLEMENTS I_Sensor");

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Contains("Reset", Assert.Single(workspace.Skipped).Message);
        }

        // A plugin-provided FB is invisible at load time, so its members are
        // unknown and reporting them missing would be a guess.
        [Fact]
        public void Load_FbExtendingUnknownLibraryBlock_IsNotChecked()
        {
            WriteInterface("I_Sensor", methods: new[] { "Reset" });
            WriteFb("FB_Plugged", "EXTENDS Tc2_System.FB_FileOpen IMPLEMENTS I_Sensor");

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Empty(workspace.Skipped);
        }

        // A skipped file's declaration warnings would report lost VAR lines of
        // a type that is no longer loaded.
        [Fact]
        public void Load_NonConformantFbWithUnreadableVarLine_IsReportedOnlyAsSkipped()
        {
            WriteInterface("I_Sensor", methods: new[] { "Reset" });
            WriteFb("FB_Sensor", "IMPLEMENTS I_Sensor\nVAR\n    ax, ay : INT;\nEND_VAR");

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Single(workspace.Skipped);
            Assert.Empty(workspace.Warnings);
        }

        [Fact]
        public void Load_ConformantFbWithUnreadableVarLine_StillWarns()
        {
            WriteInterface("I_Sensor", methods: new[] { "Reset" });
            WriteFb("FB_Sensor", "IMPLEMENTS I_Sensor\nVAR\n    ax, ay : INT;\nEND_VAR", methods: new[] { "Reset" });

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Empty(workspace.Skipped);
            Assert.Single(workspace.Warnings);
        }

        // A method and a property are different member kinds: a same-named
        // member of the other kind does not satisfy the contract.
        [Fact]
        public void Load_FbProvidesMethodWhereInterfaceDeclaresProperty_IsSkipped()
        {
            WriteInterface("I_Sensor", properties: new[] { ("Reset", "G") });
            WriteFb("FB_Sensor", "IMPLEMENTS I_Sensor", methods: new[] { "Reset" });

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Contains("property Reset", Assert.Single(workspace.Skipped).Message);
        }

        [Fact]
        public void Load_FbProvidesPropertyWhereInterfaceDeclaresMethod_IsSkipped()
        {
            WriteInterface("I_Sensor", methods: new[] { "Reset" });
            WriteFb("FB_Sensor", "IMPLEMENTS I_Sensor", properties: new[] { ("Reset", "GS") });

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Contains("method Reset", Assert.Single(workspace.Skipped).Message);
        }

        [Fact]
        public void Load_InterfaceNameDiffersOnlyInCase_IsStillChecked()
        {
            WriteInterface("I_Sensor", methods: new[] { "Reset" });
            WriteFb("FB_Sensor", "IMPLEMENTS i_sensor");

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Contains("Reset", Assert.Single(workspace.Skipped).Message);
        }

        private void WriteInterface(string name, string[] methods = null, (string Name, string Accessors)[] properties = null)
        {
            var xml = new StringBuilder();
            xml.Append($"<TcPlcObject Version=\"1.1.0.1\"><Itf Name=\"{name}\" Id=\"{{00000000-0000-0000-0000-000000000001}}\">");
            xml.Append($"<Declaration><![CDATA[INTERFACE {name}]]></Declaration>");
            foreach (var method in methods ?? new string[0])
                xml.Append($"<Method Name=\"{method}\" Id=\"{{00000000-0000-0000-0000-000000000002}}\"><Declaration><![CDATA[METHOD {method} : BOOL]]></Declaration><Implementation><ST><![CDATA[]]></ST></Implementation></Method>");
            foreach (var (property, accessors) in properties ?? new (string, string)[0])
            {
                xml.Append($"<Property Name=\"{property}\" Id=\"{{00000000-0000-0000-0000-000000000003}}\"><Declaration><![CDATA[PROPERTY {property} : BOOL]]></Declaration>");
                if (accessors.Contains('G'))
                    xml.Append("<Get Name=\"Get\" Id=\"{00000000-0000-0000-0000-000000000004}\"><Declaration><![CDATA[]]></Declaration><Implementation><ST><![CDATA[]]></ST></Implementation></Get>");
                if (accessors.Contains('S'))
                    xml.Append("<Set Name=\"Set\" Id=\"{00000000-0000-0000-0000-000000000005}\"><Declaration><![CDATA[]]></Declaration><Implementation><ST><![CDATA[]]></ST></Implementation></Set>");
                xml.Append("</Property>");
            }
            xml.Append("</Itf></TcPlcObject>");
            File.WriteAllText(Path.Combine(_tempDir, name + ".TcIO"), xml.ToString());
        }

        private void WriteFb(string name, string headerTail, string[] methods = null, (string Name, string Accessors)[] properties = null)
        {
            var xml = new StringBuilder();
            xml.Append($"<TcPlcObject Version=\"1.1.0.1\"><POU Name=\"{name}\" Id=\"{{00000000-0000-0000-0000-000000000006}}\" SpecialFunc=\"None\">");
            xml.Append($"<Declaration><![CDATA[FUNCTION_BLOCK {name} {headerTail}]]></Declaration>");
            xml.Append("<Implementation><ST><![CDATA[]]></ST></Implementation>");
            foreach (var method in methods ?? new string[0])
                xml.Append($"<Method Name=\"{method}\" Id=\"{{00000000-0000-0000-0000-000000000007}}\"><Declaration><![CDATA[METHOD {method} : BOOL]]></Declaration><Implementation><ST><![CDATA[{method} := TRUE;]]></ST></Implementation></Method>");
            foreach (var (property, accessors) in properties ?? new (string, string)[0])
            {
                xml.Append($"<Property Name=\"{property}\" Id=\"{{00000000-0000-0000-0000-000000000008}}\"><Declaration><![CDATA[PROPERTY {property} : BOOL]]></Declaration>");
                if (accessors.Contains('G'))
                    xml.Append($"<Get Name=\"Get\" Id=\"{{00000000-0000-0000-0000-000000000009}}\"><Declaration><![CDATA[]]></Declaration><Implementation><ST><![CDATA[{property} := TRUE;]]></ST></Implementation></Get>");
                if (accessors.Contains('S'))
                    xml.Append("<Set Name=\"Set\" Id=\"{00000000-0000-0000-0000-00000000000a}\"><Declaration><![CDATA[]]></Declaration><Implementation><ST><![CDATA[]]></ST></Implementation></Set>");
                xml.Append("</Property>");
            }
            xml.Append("</POU></TcPlcObject>");
            File.WriteAllText(Path.Combine(_tempDir, name + ".TcPOU"), xml.ToString());
        }
    }
}
