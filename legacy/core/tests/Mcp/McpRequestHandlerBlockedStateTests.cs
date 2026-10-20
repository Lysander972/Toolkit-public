using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Toolkit.Mcp;
using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Persistence;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Mcp;

[Collection("WorkspaceState")]
public sealed class McpRequestHandlerBlockedStateTests : IDisposable
{
    private readonly ProjectModel _model;
    private readonly BrowserRenderService _browser;
    private readonly ToolkitServices _services;

    public McpRequestHandlerBlockedStateTests()
    {
        _model = ProjectModel.CreateForTesting();
        _model.SetRootForTesting(TestEntityFactory.CreateRoot());
        ProjectLogicContext logic = new(_model);
        _browser = new BrowserRenderService(1, Array.Empty<int>());
        _services = new ToolkitServices(_model, logic, _browser);
    }

    // 门禁只拦截状态敏感方法（tools/call、resources/read）。
    // 静态元数据方法（tools/list 等）在 blocked/error 下也立即返回，见 McpRequestHandlerStaticMethodGateTests。
    // 这里用 tools/call 验证拒绝：门禁在 params 解析之前生效，空 Params 仍会被拦截。

    [Fact]
    public async Task HandleAsync_When_WorkspaceBlocked_Should_Reject_State_Sensitive_Requests()
    {
        McpToolRegistry registry = new(_services, "test-root", null, registerHandlers: false);
        McpRequestHandler handler = new(
            registry,
            new McpResourceRegistry(),
            static () => new McpHostStateSnapshot(
                "blocked",
                "存在至少一个文件正在被外部进程占用，Host 已阻塞。至少这些文件存在阻塞问题，列表可能不完整。",
                ["表格/~$碰撞层级设置.xlsx", "表格/调色板表.xlsx"]));

        JsonRpcRequest request = new()
        {
            Id = new RequestId(1L),
            Method = "tools/call",
            Params = new JsonObject()
        };

        JsonRpcMessage? response = await handler.HandleAsync(request, CancellationToken.None);

        string message = Assert.IsType<JsonRpcError>(response).Error.Message;
        Assert.Contains("Host 已阻塞", message);
        Assert.Contains("表格/~$碰撞层级设置.xlsx", message);
        Assert.Contains("表格/调色板表.xlsx", message);
    }

    [Fact]
    public async Task HandleAsync_When_HostError_Should_Reject_State_Sensitive_Requests()
    {
        McpToolRegistry registry = new(_services, "test-root", null, registerHandlers: false);
        McpRequestHandler handler = new(
            registry,
            new McpResourceRegistry(),
            static () => new McpHostStateSnapshot(
                "error",
                "工作区视图刷新失败，正在重试：InvalidOperationException: boom",
                Array.Empty<string>()));

        JsonRpcRequest request = new()
        {
            Id = new RequestId(1L),
            Method = "tools/call",
            Params = new JsonObject()
        };

        JsonRpcMessage? response = await handler.HandleAsync(request, CancellationToken.None);

        string message = Assert.IsType<JsonRpcError>(response).Error.Message;
        Assert.Contains("工作区视图刷新失败", message);
        Assert.Contains("boom", message);
    }

    public void Dispose()
    {
        _browser.Dispose();
        _model.Dispose();
    }
}
