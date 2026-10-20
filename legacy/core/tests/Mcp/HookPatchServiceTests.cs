using System.Text.Json.Nodes;
using Toolkit.Mcp;
using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Mcp;

[Collection("WorkspaceState")]

public sealed class HookPatchServiceTests : IDisposable
{
    private readonly ProjectModel _model;
    private readonly BrowserRenderService _browser;
    private readonly ToolkitServices _services;

    public HookPatchServiceTests()
    {
        _model = ProjectModel.CreateForTesting();
        _model.SetRootForTesting(TestEntityFactory.CreateRoot());
        ProjectLogicContext logic = new(_model);
        _browser = new BrowserRenderService(1, Array.Empty<int>());
        _services = new ToolkitServices(_model, logic, _browser);
    }

    [Fact]
    public async Task ClaudeEditHook_When_TargetIsView_Should_RouteToPatchAndDenyOriginalTool()
    {
        string projectRootFullPath = SystemIOWrapper.Combine(
            Path.GetTempPath(),
            "ToolkitHookPatchServiceTests");

        McpRequestHandler handler = new(
            new McpToolRegistry(_services, projectRootFullPath, null, registerHandlers: false),
            new McpResourceRegistry(),
            static () => new McpHostStateSnapshot("ready", "ready", Array.Empty<string>()));
        HookPatchService service = new(handler, new GuardService(), projectRootFullPath);
        string viewFullPath = SystemIOWrapper.Combine(projectRootFullPath, "temp", "toolkit", "views", "missing.ts");

        string raw = $$"""
        {
          "hook_event_name": "PreToolUse",
          "tool_name": "Edit",
          "tool_input": {
            "file_path": "{{viewFullPath.Replace('\\', '/')}}",
            "old_string": "a",
            "new_string": "b"
          }
        }
        """;

        JsonObject response = await service.HandleAsync("claude", raw, CancellationToken.None);

        JsonObject hookOutput = Assert.IsType<JsonObject>(response["hookSpecificOutput"]);
        Assert.Equal("PreToolUse", hookOutput["hookEventName"]?.GetValue<string>());
        Assert.Equal("deny", hookOutput["permissionDecision"]?.GetValue<string>());
        string reason = Assert.IsAssignableFrom<JsonValue>(hookOutput["permissionDecisionReason"]).GetValue<string>();
        Assert.Contains("patch_mech_view", reason);
        Assert.Contains("调用失败", reason);
    }

    [Fact]
    public async Task ClaudeWriteHook_When_TargetIsViewWithoutWorkspace_Should_DenyAndExplainNoWrite()
    {
        string projectRootFullPath = SystemIOWrapper.Combine(
            Path.GetTempPath(),
            "ToolkitHookPatchServiceTests");

        McpRequestHandler handler = new(
            new McpToolRegistry(_services, projectRootFullPath, null, registerHandlers: false),
            new McpResourceRegistry(),
            static () => new McpHostStateSnapshot("ready", "ready", Array.Empty<string>()));
        HookPatchService service = new(handler, new GuardService(), projectRootFullPath);
        string viewFullPath = SystemIOWrapper.Combine(projectRootFullPath, "temp", "toolkit", "views", "missing.ts");

        string raw = $$"""
        {
          "hook_event_name": "PreToolUse",
          "tool_name": "Write",
          "tool_input": {
            "file_path": "{{viewFullPath.Replace('\\', '/')}}",
            "content": "new content"
          }
        }
        """;

        JsonObject response = await service.HandleAsync("claude", raw, CancellationToken.None);

        JsonObject hookOutput = Assert.IsType<JsonObject>(response["hookSpecificOutput"]);
        Assert.Equal("deny", hookOutput["permissionDecision"]?.GetValue<string>());
        string reason = Assert.IsAssignableFrom<JsonValue>(hookOutput["permissionDecisionReason"]).GetValue<string>();
        Assert.Contains("原生 Write", reason);
        Assert.Contains("无法转发为 MCP 全量覆盖", reason);
    }

    public void Dispose()
    {
        _browser.Dispose();
        _model.Dispose();
    }
}
