using System;
using System.IO;
using System.Linq;
using xStunit.Interpreter;
using Xunit;

namespace xStunit.Interpreter.Tests
{
    // The load half of a run, exercised without the CLI: a directory set in, a
    // TypeRegistry and a skip list out. The rule these pin is that a file the
    // loaders cannot read costs only itself - it is reported as a skip and the
    // load still succeeds, which is what keeps a run that skipped files exiting
    // by test outcome rather than as a discovery error.
    public class WorkspaceLoaderTests : IDisposable
    {
        private readonly string _tempDir;

        public WorkspaceLoaderTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "XstunitWorkspaceFixture_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        [Fact]
        public void Load_MalformedPouBesideGoodPou_KeepsGoodTypeAndReportsSkip()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_Counter.TcPOU"), CounterPouXml);
            File.WriteAllText(Path.Combine(_tempDir, "FB_Malformed.TcPOU"), "<TcPlcObject><POU Name=");

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            // A skip is never fatal: no error, so the caller has a registry to
            // run suites against and exits by test outcome, not by exit 2.
            Assert.Null(workspace.Error);
            Assert.NotNull(workspace.Registry.Get("FB_Counter"));
            Assert.Equal(new[] { "FB_Counter" }, workspace.PouTypes.Select(t => t.Name));
            var skip = Assert.Single(workspace.Skipped);
            Assert.EndsWith("FB_Malformed.TcPOU", skip.FileKey);
            Assert.Contains("FB_Malformed.TcPOU", skip.Message);
        }

        // The unsupported-construct rejection takes its own path out of the
        // parser (TcPouRejectedException, which the shared parse guard
        // deliberately does not catch), so it needs its own pin that it still
        // lands in the skip list rather than escaping the load.
        [Fact]
        public void Load_UnsupportedPouBesideGoodPou_KeepsGoodTypeAndReportsSkip()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_Counter.TcPOU"), CounterPouXml);
            File.WriteAllText(Path.Combine(_tempDir, "FB_UsesLibraryCall.TcPOU"), UnsupportedPouXml);

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Null(workspace.Error);
            Assert.NotNull(workspace.Registry.Get("FB_Counter"));
            var skip = Assert.Single(workspace.Skipped);
            Assert.EndsWith("FB_UsesLibraryCall.TcPOU", skip.FileKey);
        }

        [Fact]
        public void Load_CleanDirectory_MapsEachTypeNameToItsSourceFile()
        {
            var pouPath = Path.Combine(_tempDir, "FB_Counter.TcPOU");
            File.WriteAllText(pouPath, CounterPouXml);

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Empty(workspace.Skipped);
            Assert.Equal(pouPath, workspace.FilePathsByTypeName["FB_Counter"]);
        }

        // The DUT and GVL halves reach the registry through separate loaders,
        // each with its own skip list: if any of them stops being merged in, a
        // suite referencing a struct, an alias, an enum or a global still loads
        // but fails at run time with an unresolved type.
        [Fact]
        public void Load_DutsAndGvlsBesidePous_AllReachTheRegistry()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_Counter.TcPOU"), CounterPouXml);
            File.WriteAllText(Path.Combine(_tempDir, "ST_Point.TcDUT"), StructDutXml);
            File.WriteAllText(Path.Combine(_tempDir, "T_Counter.TcDUT"), AliasDutXml);
            File.WriteAllText(Path.Combine(_tempDir, "E_Mode.TcDUT"), EnumDutXml);
            File.WriteAllText(Path.Combine(_tempDir, "gGlobals.TcGVL"), GvlXml);

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Null(workspace.Error);
            Assert.NotNull(workspace.Registry.GetStruct("ST_Point"));
            Assert.Equal("INT", workspace.Registry.ResolveAlias("T_Counter"));
            Assert.True(workspace.Registry.TryGetEnumMembers("E_Mode", out var members));
            Assert.Equal(1, members["Running"]);
            Assert.Contains("gGlobals", workspace.Registry.GvlNames);
            // An enum resolves through the alias map as well, so SIZEOF() and
            // every other ResolveAlias call site needs no second lookup path.
            Assert.Equal("INT", workspace.Registry.ResolveAlias("E_Mode"));
        }

        // A .TcIO reaches the registry as an interface and NOT as a POU: an
        // interface in the POU map would be instantiable, offered to
        // SuiteDiscovery's ancestry walk, and would put its file in
        // FilePathsByTypeName as though it held runnable code.
        [Fact]
        public void Load_InterfaceBesidePous_ReachesTheRegistryAsAnInterfaceOnly()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_Counter.TcPOU"), CounterPouXml);
            File.WriteAllText(Path.Combine(_tempDir, "I_Countable.TcIO"), InterfaceXml);

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Null(workspace.Error);
            Assert.Empty(workspace.Skipped);
            Assert.Equal("I_Countable", workspace.Registry.GetInterface("I_Countable").Name);
            Assert.Null(workspace.Registry.Get("I_Countable"));
            Assert.Equal(new[] { "FB_Counter" }, workspace.PouTypes.Select(t => t.Name));
        }

        [Fact]
        public void Load_MalformedInterfaceBesideGoodPou_KeepsGoodTypeAndReportsSkip()
        {
            File.WriteAllText(Path.Combine(_tempDir, "FB_Counter.TcPOU"), CounterPouXml);
            File.WriteAllText(Path.Combine(_tempDir, "I_Malformed.TcIO"), "<TcPlcObject><Itf Name=");

            var workspace = WorkspaceLoader.Load(new[] { _tempDir });

            Assert.Null(workspace.Error);
            Assert.NotNull(workspace.Registry.Get("FB_Counter"));
            Assert.EndsWith("I_Malformed.TcIO", Assert.Single(workspace.Skipped).FileKey);
        }

        // Same rule as a duplicate POU type name: an ambiguous interface name
        // means the caller pointed the CLI at an inconsistent directory set,
        // and letting the last file win would pick a contract at random.
        [Fact]
        public void Load_SameInterfaceNameInTwoDirectories_ReportsErrorRatherThanSkipping()
        {
            var first = Path.Combine(_tempDir, "first");
            var second = Path.Combine(_tempDir, "second");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            File.WriteAllText(Path.Combine(first, "I_Countable.TcIO"), InterfaceXml);
            File.WriteAllText(Path.Combine(second, "I_Countable.TcIO"), InterfaceXml);

            var workspace = WorkspaceLoader.Load(new[] { first, second });

            Assert.Contains("I_Countable", workspace.Error);
        }

        [Fact]
        public void Load_MissingDirectory_ReportsErrorNamingThePath()
        {
            var missing = Path.Combine(_tempDir, "not-here");

            var workspace = WorkspaceLoader.Load(new[] { missing });

            Assert.Contains(missing, workspace.Error);
        }

        // A type name defined twice across the merged directory set is usage,
        // not an unsupported file: the caller pointed the CLI at an
        // inconsistent set of directories, and letting whichever file loaded
        // last win would silently run the wrong code.
        [Fact]
        public void Load_SameTypeNameInTwoDirectories_ReportsErrorRatherThanSkipping()
        {
            var first = Path.Combine(_tempDir, "first");
            var second = Path.Combine(_tempDir, "second");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            File.WriteAllText(Path.Combine(first, "FB_Counter.TcPOU"), CounterPouXml);
            File.WriteAllText(Path.Combine(second, "FB_Counter.TcPOU"), CounterPouXml);

            var workspace = WorkspaceLoader.Load(new[] { first, second });

            Assert.Contains("FB_Counter", workspace.Error);
        }

        // A load that fails hard still has to hand back the files it had
        // already skipped: the CLI reports them alongside the error, so a
        // caller sees both why the run stopped and what it had lost before
        // that.
        [Fact]
        public void Load_SkipsThenFatalError_StillReportsTheSkips()
        {
            var first = Path.Combine(_tempDir, "first");
            var second = Path.Combine(_tempDir, "second");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            File.WriteAllText(Path.Combine(first, "FB_Counter.TcPOU"), CounterPouXml);
            File.WriteAllText(Path.Combine(second, "FB_Counter.TcPOU"), CounterPouXml);
            File.WriteAllText(Path.Combine(first, "FB_Malformed.TcPOU"), "<TcPlcObject><POU Name=");

            var workspace = WorkspaceLoader.Load(new[] { first, second });

            Assert.NotNull(workspace.Error);
            Assert.EndsWith("FB_Malformed.TcPOU", Assert.Single(workspace.Skipped).FileKey);
        }

        private const string CounterPouXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_Counter"" Id=""{00000000-0000-0000-0000-0000000000c0}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_Counter
VAR
    nCount : INT;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[nCount := nCount + 1;]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        // Rejected outright by the parser on account of the library call in its
        // body, the way a real tree's Tc2_System-touching POUs are.
        private const string UnsupportedPouXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_UsesLibraryCall"" Id=""{00000000-0000-0000-0000-0000000000c1}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_UsesLibraryCall
VAR
    fbTimer : Tc2_System.GETCURTASKINDEX;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[Tc2_System.GETCURTASKINDEX();]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

        private const string StructDutXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <DUT Name=""ST_Point"" Id=""{00000000-0000-0000-0000-0000000000c2}"">
    <Declaration><![CDATA[TYPE ST_Point :
STRUCT
    x : INT;
    y : INT;
END_STRUCT
END_TYPE]]></Declaration>
  </DUT>
</TcPlcObject>";

        private const string AliasDutXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <DUT Name=""T_Counter"" Id=""{00000000-0000-0000-0000-0000000000c3}"">
    <Declaration><![CDATA[TYPE T_Counter : INT;
END_TYPE]]></Declaration>
  </DUT>
</TcPlcObject>";

        private const string EnumDutXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <DUT Name=""E_Mode"" Id=""{00000000-0000-0000-0000-0000000000c4}"">
    <Declaration><![CDATA[TYPE E_Mode :
(
    Idle := 0,
    Running := 1
) INT;
END_TYPE]]></Declaration>
  </DUT>
</TcPlcObject>";

        private const string InterfaceXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <Itf Name=""I_Countable"" Id=""{00000000-0000-0000-0000-0000000000c6}"">
    <Declaration><![CDATA[INTERFACE I_Countable]]></Declaration>
    <Method Name=""Reset"" Id=""{00000000-0000-0000-0000-0000000000c7}"">
      <Declaration><![CDATA[METHOD Reset : BOOL]]></Declaration>
      <Implementation>
        <ST><![CDATA[]]></ST>
      </Implementation>
    </Method>
  </Itf>
</TcPlcObject>";

        private const string GvlXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <GVL Name=""gGlobals"" Id=""{00000000-0000-0000-0000-0000000000c5}"">
    <Declaration><![CDATA[VAR_GLOBAL
    nLimit : INT := 16;
END_VAR]]></Declaration>
  </GVL>
</TcPlcObject>";
    }
}
