using System;
using xStunit.Parser;
using Xunit;

namespace xStunit.Parser.Tests
{
    public class TcPouParserTests
    {
        [Fact]
        public void Parse_MinimalFunctionBlock_ReadsNameDeclarationAndImplementation()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"" ProductVersion=""3.1.4026.18"">
  <POU Name=""FB_AddLrealInt"" Id=""{d98c697c-4a7c-44d3-a5f9-ca6e839b6f15}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_AddLrealInt
VAR_INPUT
	lrealValue : LREAL;
	intValue : INT;
END_VAR
VAR_OUTPUT
	result : LREAL;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[result := lrealValue + TO_LREAL(intValue);]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

            var ast = TcPouParser.Parse(xml);

            Assert.Equal("FB_AddLrealInt", ast.Name);
            Assert.Contains("FUNCTION_BLOCK FB_AddLrealInt", ast.DeclarationText);
            Assert.Equal("result := lrealValue + TO_LREAL(intValue);", ast.ImplementationText);
            Assert.Null(ast.BaseTypeName);
            Assert.Empty(ast.Methods);
        }

        [Fact]
        public void Parse_FunctionBlockWithMethods_ReadsEachMethodNameDeclarationAndImplementation()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_Counter"" Id=""{a1b2c3d4-0001-4a1a-8b1b-000000000001}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_Counter
VAR
	value : INT;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[]]></ST>
    </Implementation>
    <Method Name=""Increment"" Id=""{a1b2c3d4-0001-4a1a-8b1b-000000000003}"">
      <Declaration><![CDATA[METHOD PUBLIC Increment
VAR_INPUT
	delta : INT := 1;
END_VAR
]]></Declaration>
      <Implementation>
        <ST><![CDATA[value := value + delta;]]></ST>
      </Implementation>
    </Method>
    <Method Name=""GetValue"" Id=""{a1b2c3d4-0001-4a1a-8b1b-000000000005}"">
      <Declaration><![CDATA[METHOD PUBLIC GetValue : INT
]]></Declaration>
      <Implementation>
        <ST><![CDATA[GetValue := value;]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";

            var ast = TcPouParser.Parse(xml);

            Assert.Equal(2, ast.Methods.Count);

            var increment = ast.Methods[0];
            Assert.Equal("Increment", increment.Name);
            Assert.Contains("METHOD PUBLIC Increment", increment.DeclarationText);
            Assert.Equal("value := value + delta;", increment.ImplementationText);

            var getValue = ast.Methods[1];
            Assert.Equal("GetValue", getValue.Name);
            Assert.Equal("GetValue := value;", getValue.ImplementationText);
        }

        // A PROPERTY lives in <Property>/<Get>/<Set> elements the POU-and-method
        // walk never visits; miss them and the member disappears from the AST
        // silently, with no parse error to point at.
        [Fact]
        public void Parse_FunctionBlockWithGetSetProperty_ReadsPropertyNameAndBothAccessorBodies()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_Foo"" Id=""{a1b2c3d4-0006-4a1a-8b1b-000000000001}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_Foo
VAR
	snCounter : UINT;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[]]></ST>
    </Implementation>
    <Property Name=""nCounter"" Id=""{a1b2c3d4-0006-4a1a-8b1b-000000000002}"">
      <Declaration><![CDATA[PROPERTY PUBLIC nCounter : UINT]]></Declaration>
      <Get Name=""Get"" Id=""{a1b2c3d4-0006-4a1a-8b1b-000000000003}"">
        <Declaration><![CDATA[]]></Declaration>
        <Implementation>
          <ST><![CDATA[nCounter := snCounter;]]></ST>
        </Implementation>
      </Get>
      <Set Name=""Set"" Id=""{a1b2c3d4-0006-4a1a-8b1b-000000000004}"">
        <Declaration><![CDATA[]]></Declaration>
        <Implementation>
          <ST><![CDATA[snCounter := nCounter;]]></ST>
        </Implementation>
      </Set>
    </Property>
  </POU>
</TcPlcObject>";

            var ast = TcPouParser.Parse(xml);

            Assert.Single(ast.Properties);
            var property = ast.Properties[0];
            Assert.Equal("nCounter", property.Name);
            Assert.Contains("PROPERTY PUBLIC nCounter", property.DeclarationText);
            Assert.True(property.HasGet);
            Assert.True(property.HasSet);
            Assert.Equal("nCounter := snCounter;", property.GetImplementationText);
            Assert.Equal("snCounter := nCounter;", property.SetImplementationText);
        }

        [Fact]
        public void Parse_FunctionBlockWithGetOnlyProperty_SetAccessorIsAbsentNotEmpty()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_Foo"" Id=""{a1b2c3d4-0007-4a1a-8b1b-000000000001}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_Foo
VAR
	snCounter : UINT;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[]]></ST>
    </Implementation>
    <Property Name=""nCounter"" Id=""{a1b2c3d4-0007-4a1a-8b1b-000000000002}"">
      <Declaration><![CDATA[PROPERTY nCounter : UINT]]></Declaration>
      <Get Name=""Get"" Id=""{a1b2c3d4-0007-4a1a-8b1b-000000000003}"">
        <Declaration><![CDATA[]]></Declaration>
        <Implementation>
          <ST><![CDATA[nCounter := snCounter;]]></ST>
        </Implementation>
      </Get>
    </Property>
  </POU>
</TcPlcObject>";

            var ast = TcPouParser.Parse(xml);

            var property = Assert.Single(ast.Properties);
            Assert.True(property.HasGet);
            Assert.False(property.HasSet);
            Assert.Null(property.SetImplementationText);
        }

        [Fact]
        public void Parse_ExtendsAnotherFunctionBlock_ReadsBaseTypeName()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_ClampedCounter"" Id=""{a1b2c3d4-0002-4a1a-8b1b-000000000001}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_ClampedCounter EXTENDS FB_Counter
VAR
	ceiling : INT;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

            var ast = TcPouParser.Parse(xml);

            Assert.Equal("FB_ClampedCounter", ast.Name);
            Assert.Equal("FB_Counter", ast.BaseTypeName);
        }

        [Fact]
        public void Parse_ExtendsLibraryQualifiedFunctionBlock_ReadsQualifiedBaseTypeName()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_CounterTests"" Id=""{a1b2c3d4-0003-4a1a-8b1b-000000000001}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_CounterTests EXTENDS TcUnit.FB_TestSuite]]></Declaration>
    <Implementation>
      <ST><![CDATA[]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

            var ast = TcPouParser.Parse(xml);

            Assert.Equal("TcUnit.FB_TestSuite", ast.BaseTypeName);
        }

        [Fact]
        public void Parse_ExtendsWithAbstractQualifier_ReadsBaseTypeName()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_WidgetModuleBase"" Id=""{a1b2c3d4-0004-4a1a-8b1b-000000000001}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK ABSTRACT FB_WidgetModuleBase EXTENDS FB_ModuleBase]]></Declaration>
    <Implementation>
      <ST><![CDATA[]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

            var ast = TcPouParser.Parse(xml);

            Assert.Equal("FB_ModuleBase", ast.BaseTypeName);
        }

        [Fact]
        public void Parse_ExtendsWithFinalQualifier_ReadsBaseTypeName()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_SealedCounter"" Id=""{a1b2c3d4-0005-4a1a-8b1b-000000000001}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FINAL FB_SealedCounter EXTENDS FB_Counter]]></Declaration>
    <Implementation>
      <ST><![CDATA[]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

            var ast = TcPouParser.Parse(xml);

            Assert.Equal("FB_Counter", ast.BaseTypeName);
        }

        [Fact]
        public void Parse_MethodUsingDunderNew_ThrowsRejectedConstructWithDiagnostic()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_DynamicCreator"" Id=""{00000000-0000-0000-0000-000000000001}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_DynamicCreator
VAR
	pCounter : POINTER TO FB_Counter;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[]]></ST>
    </Implementation>
    <Method Name=""CreateCounter"" Id=""{00000000-0000-0000-0000-000000000002}"">
      <Declaration><![CDATA[METHOD PUBLIC CreateCounter
]]></Declaration>
      <Implementation>
        <ST><![CDATA[pCounter := __NEW(FB_Counter);]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";

            var ex = Assert.Throws<TcPouRejectedException>(() => TcPouParser.Parse(xml));

            Assert.Contains("__NEW", ex.Message);
            Assert.Contains("CreateCounter", ex.Message);
        }

        [Fact]
        public void Parse_MethodCallingTc2System_ThrowsRejectedConstructWithDiagnostic()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_SystemCaller"" Id=""{00000000-0000-0000-0000-000000000003}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_SystemCaller]]></Declaration>
    <Implementation>
      <ST><![CDATA[Tc2_System.F_GetCurTaskIndex();]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

            var ex = Assert.Throws<TcPouRejectedException>(() => TcPouParser.Parse(xml));

            Assert.Contains("Tc2_System", ex.Message);
        }

        [Fact]
        public void Parse_RecordsBodyStartLinePerScope_SoInBodyLinesMapBackToTheFile()
        {
            // Editing this literal moves the assertions below: the ST bodies
            // sit on lines 9 (POU), 18 (Increment) and 26 (GetValue), counting
            // the <?xml ...?> line as 1.
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_Counter"" Id=""{a1b2c3d4-0001-4a1a-8b1b-000000000001}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_Counter
VAR
	value : INT;
END_VAR]]></Declaration>
    <Implementation>
      <ST><![CDATA[value := 0;]]></ST>
    </Implementation>
    <Method Name=""Increment"" Id=""{a1b2c3d4-0001-4a1a-8b1b-000000000003}"">
      <Declaration><![CDATA[METHOD PUBLIC Increment
VAR_INPUT
	delta : INT := 1;
END_VAR
]]></Declaration>
      <Implementation>
        <ST><![CDATA[value := value + delta;
value := value + 0;]]></ST>
      </Implementation>
    </Method>
    <Method Name=""GetValue"" Id=""{a1b2c3d4-0001-4a1a-8b1b-000000000005}"">
      <Declaration><![CDATA[METHOD PUBLIC GetValue : INT
]]></Declaration>
      <Implementation>
        <ST><![CDATA[GetValue := value;]]></ST>
      </Implementation>
    </Method>
  </POU>
</TcPlcObject>";

            var ast = TcPouParser.Parse(xml);

            Assert.Equal(9, ast.BodyStartLine);
            Assert.Equal(18, ast.Methods[0].BodyStartLine);
            Assert.Equal(26, ast.Methods[1].BodyStartLine);

            // The contract callers rely on: BodyStartLine plus the zero-based
            // line within the body is the real file line. Increment's second
            // statement is body line 1, on file line 19.
            var increment = ast.Methods[0];
            Assert.Equal(
                19,
                increment.BodyStartLine + increment.ImplementationText.Split('\n').Length - 1);
        }

        [Fact]
        public void Parse_CdataOpeningWithNewline_CountsThatNewlineAsTheBodysFirstLine()
        {
            // TwinCAT sometimes opens `<ST><![CDATA[` with a trailing newline.
            // That newline belongs to the body string, so body line 0 is the
            // empty tail of the <ST> line and the offset still holds.
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TcPlcObject Version=""1.1.0.1"">
  <POU Name=""FB_Blank"" Id=""{00000000-0000-0000-0000-000000000009}"" SpecialFunc=""None"">
    <Declaration><![CDATA[FUNCTION_BLOCK FB_Blank]]></Declaration>
    <Implementation>
      <ST><![CDATA[
first := 1;]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>";

            var ast = TcPouParser.Parse(xml);

            Assert.Equal(6, ast.BodyStartLine);

            var bodyLines = ast.ImplementationText.Split('\n');
            Assert.Equal("", bodyLines[0]);
            Assert.Equal("first := 1;", bodyLines[1]);
            Assert.Equal(7, ast.BodyStartLine + 1);
        }
    }
}
