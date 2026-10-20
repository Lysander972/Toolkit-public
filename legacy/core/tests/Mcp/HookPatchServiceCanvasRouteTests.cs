using System.Text.Json.Nodes;
using Toolkit.Mcp;
using Xunit;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests.Mcp;

/// <summary>
/// HookPatchService 在 Claude 原生 Write 目标视图不存在时的路由测试。
/// 用 McpBrowserTestFixture 的已同步 Workspace（测试方法内新建 McpViewWorkspace 会因后台同步未就绪而死锁）。
/// 覆盖 CreateWritePatchCall 的路径不存在分流：
/// - .html 不存在 → 转发 patch_mech_view 新建（registerHandlers:true，patch 入口因父目录不存在 FailPatch，不落盘）
/// - .ts 不存在 → 拒绝并引导用户去编辑器创建零件
/// </summary>
[Collection("McpBrowserTests")]
public sealed class HookPatchServiceCanvasRouteTests
{
    private readonly McpBrowserTestFixture _fixture;

    public HookPatchServiceCanvasRouteTests(McpBrowserTestFixture fixture)
    {
        _fixture = fixture;
    }

    private HookPatchService CreateService()
    {
        McpRequestHandler handler = new(
            _fixture.Registry,
            new McpResourceRegistry(),
            static () => new McpHostStateSnapshot("ready", "ready", Array.Empty<string>()));
        return new HookPatchService(handler, new GuardService(), McpBrowserTestFixture.TestProjectRoot, _fixture.Workspace);
    }

    [Fact]
    public async Task ClaudeWriteHook_When_HtmlViewNotExists_Should_RouteToPatchCreate()
    {
        if (!_fixture.IsAvailable) return;
        HookPatchService service = CreateService();
        // 父目录不存在 → 转发 patch 新建后入口拒绝（不落盘）；reason 含 patch 反馈证明走了新建转发分支。
        string viewFullPath = SystemIOWrapper.Combine(
            McpBrowserTestFixture.TestProjectRoot, "temp", "toolkit", "views", "patch_hook_no_such_dir/new.html");

        string raw = $$"""
        {
          "hook_event_name": "PreToolUse",
          "tool_name": "Write",
          "tool_input": {
            "file_path": "{{viewFullPath.Replace('\\', '/')}}",
            "content": "<BeCanvas name=\"x\"></BeCanvas>"
          }
        }
        """;

        JsonObject response = await service.HandleAsync("claude", raw, CancellationToken.None);

        JsonObject hookOutput = Assert.IsType<JsonObject>(response["hookSpecificOutput"]);
        Assert.Equal("deny", hookOutput["permissionDecision"]?.GetValue<string>());
        string reason = Assert.IsAssignableFrom<JsonValue>(hookOutput["permissionDecisionReason"]).GetValue<string>();
        // 已转发到 patch_mech_view（非"无法读取当前视图文本"的读取拒绝），证明 CreateWriteCall 对 .html 路径不存在走了新建转发分支。
        // patch_mech_view 内部校验（old_string 空等入口检查）由 PatchMechViewCanvasCreateTests 覆盖。
        Assert.Contains("patch_mech_view", reason);
        Assert.DoesNotContain("无法读取当前视图文本", reason);
    }

    [Fact]
    public async Task ClaudeWriteHook_When_TsViewNotExists_Should_DenyWithNotSupportedNewTs()
    {
        if (!_fixture.IsAvailable) return;
        HookPatchService service = CreateService();
        string viewFullPath = SystemIOWrapper.Combine(
            McpBrowserTestFixture.TestProjectRoot, "temp", "toolkit", "views", "patch_hook_no_such_device.ts");

        string raw = $$"""
        {
          "hook_event_name": "PreToolUse",
          "tool_name": "Write",
          "tool_input": {
            "file_path": "{{viewFullPath.Replace('\\', '/')}}",
            "content": "new script"
          }
        }
        """;

        JsonObject response = await service.HandleAsync("claude", raw, CancellationToken.None);

        JsonObject hookOutput = Assert.IsType<JsonObject>(response["hookSpecificOutput"]);
        Assert.Equal("deny", hookOutput["permissionDecision"]?.GetValue<string>());
        string reason = Assert.IsAssignableFrom<JsonValue>(hookOutput["permissionDecisionReason"]).GetValue<string>();
        Assert.Contains("不支持新建 TS 文件", reason);
        Assert.Contains("编辑器中创建零件", reason);
    }
}
