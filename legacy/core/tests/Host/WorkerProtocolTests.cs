using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Infra;
using Toolkit.Core.Lint;
using Toolkit.Core.Sdk;
using Toolkit.Core.Worker;

namespace Toolkit.Core.Tests.Host;

/// <summary>
/// Tests for WorkerProtocol - VSCode Plugin UI communication protocol.
/// </summary>
public sealed class WorkerProtocolTests
{
    #region WorkerRequest

    [Fact]
    public void WorkerRequest_Default_Values()
    {
        // Arrange & Act
        WorkerRequest request = new WorkerRequest("test_cmd");

        // Assert
        Assert.Equal("test_cmd", request.Cmd);
        Assert.Null(request.RequestId);
        Assert.Null(request.LintRootFullPath);
    }

    [Fact]
    public void WorkerRequest_With_All_Parameters()
    {
        // Arrange & Act
        WorkerRequest request = new WorkerRequest(
            "complex_cmd",
            123,
            "test_root",
            "canvas_path",
            "canvas_arg"
        );

        // Assert
        Assert.Equal("complex_cmd", request.Cmd);
        Assert.Equal(123, request.RequestId);
        Assert.Equal("test_root", request.LintRootFullPath);
        Assert.Equal("canvas_path", request.CanvasRelativePath);
    }

    [Fact]
    public void WorkerRequest_Symbol_Lookup_Params()
    {
        // Arrange & Act
        WorkerRequest request = new WorkerRequest(
            "symbol_lookup",
            ScriptRelativePath: "scripts/test.code",
            Line: 10,
            SymbolName: "myFunction"
        );

        // Assert
        Assert.Equal("scripts/test.code", request.ScriptRelativePath);
        Assert.Equal(10, request.Line);
        Assert.Equal("myFunction", request.SymbolName);
    }

    [Fact]
    public void WorkerRequest_Refactoring_Params()
    {
        // Arrange & Act
        WorkerRequest request = new WorkerRequest(
            "refactor_rename",
            DeviceRelativePath: "mech1/device1",
            OldName: "oldName",
            NewName: "newName"
        );

        // Assert
        Assert.Equal("mech1/device1", request.DeviceRelativePath);
        Assert.Equal("oldName", request.OldName);
        Assert.Equal("newName", request.NewName);
    }

    #endregion

    #region WorkerResponse

    [Fact]
    public void WorkerResponse_Error_Response()
    {
        // Arrange & Act
        WorkerResponse response = new WorkerResponse(
            Ok: false,
            RequestId: 1,
            Message: "Test error message"
        );

        // Assert
        Assert.False(response.Ok);
        Assert.Equal(1, response.RequestId);
        Assert.Equal("Test error message", response.Message);
    }

    [Fact]
    public void WorkerResponse_Success_Response()
    {
        // Arrange & Act
        WorkerResponse response = new WorkerResponse(
            Ok: true,
            RequestId: 2
        );

        // Assert
        Assert.True(response.Ok);
        Assert.Equal(2, response.RequestId);
        Assert.Null(response.Message);
    }

    [Fact]
    public void WorkerResponse_With_Diagnostics()
    {
        // Arrange
        LintDiagnostic[] diagnostics = new[]
        {
            new LintDiagnostic(
                "ERR001",
                LintSeverity.Error,
                "Error 1",
                new LintLocation("file1.code", 10)
            ),
            new LintDiagnostic(
                "WARN001",
                LintSeverity.Warning,
                "Warning 1",
                new LintLocation("file2.code", 20)
            )
        };

        // Act
        WorkerResponse response = new WorkerResponse(
            Ok: true,
            Diagnostics: diagnostics
        );

        // Assert
        Assert.NotNull(response.Diagnostics);
        Assert.Equal(2, response.Diagnostics.Count);
        Assert.Equal(LintSeverity.Error, response.Diagnostics[0].Severity);
    }

    #endregion

    #region SymbolLocation

    [Fact]
    public void SymbolLocation_Full_Properties()
    {
        // Arrange & Act
        SymbolLocation location = new SymbolLocation(
            "scripts/test.code",
            10,
            5,
            "myFunction",
            "function code"
        );

        // Assert
        Assert.Equal("scripts/test.code", location.RelativePath);
        Assert.Equal(10, location.Line);
        Assert.Equal(5, location.Column);
        Assert.Equal("myFunction", location.Name);
        Assert.Equal("function code", location.Text);
    }

    [Fact]
    public void SymbolLocation_Minimal_Properties()
    {
        // Arrange & Act
        SymbolLocation location = new SymbolLocation(
            "test.code",
            1,
            1
        );

        // Assert
        Assert.Equal("test.code", location.RelativePath);
        Assert.Equal(1, location.Line);
        Assert.Equal(1, location.Column);
        Assert.Null(location.Name);
        Assert.Null(location.Text);
    }

    #endregion

    #region ActCallLink

    [Fact]
    public void ActCallLink_With_Definition()
    {
        // Arrange
        SymbolLocation definition = new SymbolLocation("def.code", 5, 1, "targetAct");

        // Act
        ActCallLink link = new ActCallLink(
            "caller.code",
            new BescriptTextSpan(10, 5, 15),
            definition,
            null
        );

        // Assert
        Assert.Equal("caller.code", link.ScriptName);
        Assert.Equal(10, link.Range.Line);
        Assert.Equal(5, link.Range.Column);
        Assert.Equal(15, link.Range.Length);
        Assert.NotNull(link.Definition);
    }

    [Fact]
    public void ActCallLink_Without_Definition()
    {
        // Arrange & Act
        ActCallLink link = new ActCallLink(
            "unknown.code",
            new BescriptTextSpan(1, 1, 10),
            null,
            null
        );

        // Assert
        Assert.Equal("unknown.code", link.ScriptName);
        Assert.Null(link.Definition);
    }

    #endregion

    #region BuiltinMethodDoc

    [Fact]
    public void BuiltinMethodDoc_All_Properties()
    {
        // Arrange & Act
        BuiltinMethodDoc doc = new BuiltinMethodDoc(
            "GetValue",
            "float GetValue(string key)",
            "Gets a value from storage",
            new DocRef("Human", "human_doc.html"),
            new DocRef("Agent", "agent_doc.html")
        );

        // Assert
        Assert.Equal("GetValue", doc.Name);
        Assert.Equal("float GetValue(string key)", doc.Signature);
        Assert.Equal("Gets a value from storage", doc.Documentation);
        Assert.NotNull(doc.HumanDoc);
        Assert.NotNull(doc.AgentDoc);
    }

    #endregion

    #region EventDoc

    [Fact]
    public void EventDoc_Full_Documentation()
    {
        // Arrange
        EventParameterDoc[] parameters = new[]
        {
            new EventParameterDoc("sender", "DeviceVar", "The device that triggered the event"),
            new EventParameterDoc("value", "FloatVar", "The value that changed")
        };

        // Act
        EventDoc eventDoc = new EventDoc(
            "event_valueChanged",
            "CounterDevice",
            "void event_valueChanged(DeviceVar sender, float value)",
            "Triggered when the counter value changes",
            "DeviceVar sender, float value",
            parameters
        );

        // Assert
        Assert.Equal("event_valueChanged", eventDoc.EventName);
        Assert.Equal("CounterDevice", eventDoc.DeviceType);
        Assert.Equal(2, eventDoc.Parameters.Count);
        Assert.Equal("sender", eventDoc.Parameters[0].Name);
        Assert.Equal("DeviceVar", eventDoc.Parameters[0].TypeName);
        Assert.Equal("The device that triggered the event", eventDoc.Parameters[0].Description);
    }

    #endregion

    #region Real Data Scenarios

    [Fact]
    public void Real_Data_Script_Lookup_Request()
    {
        // Arrange - protocol carries an absolute lint root; this test does not read it.
        string scriptRelativePath = "map.map_/255/scripts/使用帮助.code";
        WorkerRequest request = new WorkerRequest(
            "symbol_lookup",
            LintRootFullPath: Path.Combine(Path.GetTempPath(), "toolkit-worker-protocol"),
            ScriptRelativePath: scriptRelativePath,
            Line: 15,
            SymbolName: "event_clientUpdate"
        );

        // Assert
        Assert.Equal("使用帮助.code", SystemIOWrapper.GetLastSegment(scriptRelativePath));
        Assert.Equal("event_clientUpdate", request.SymbolName);
    }

    [Fact]
    public void Real_Data_Refactor_Global_Var_Request()
    {
        // Arrange - protocol construction does not access the lint root.
        WorkerRequest request = new WorkerRequest(
            "globalvar_rename",
            LintRootFullPath: Path.Combine(Path.GetTempPath(), "toolkit-worker-protocol"),
            DeviceRelativePath: "map.map_/255",
            OldName: "参数调节列表",
            NewName: "新参数列表"
        );

        // Assert
        Assert.Equal("参数调节列表", request.OldName);
        Assert.Equal("新参数列表", request.NewName);
    }

    [Fact]
    public void Real_Data_Diagnostic_Response()
    {
        // Arrange - Real diagnostic scenario from test project
        LintDiagnostic diagnostic = new LintDiagnostic(
            "NAMING001",
            LintSeverity.Warning,
            "Script name '使用帮助' should use ASCII characters only",
            new LintLocation("map.map_/255/scripts/使用帮助.code", 1)
        );

        WorkerResponse response = new WorkerResponse(
            Ok: true,
            Diagnostics: new[] { diagnostic }
        );

        // Assert
        Assert.NotNull(response.Diagnostics);
        Assert.Single(response.Diagnostics);
        Assert.Contains("使用帮助", response.Diagnostics[0].Message);
    }

    #endregion
}
