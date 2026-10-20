using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Toolkit.Mcp;
using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Persistence;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Mcp;

/// <summary>
/// 验证门禁按"数据是否依赖 ViewWorkspace 同步状态"分类：
///   - 静态元数据方法（tools/list、resources/list、prompts/list 等）数据在构造期填充，
///     在 initializing/blocked/error 期间都应立即返回，不触发 _waitForReady 空转；
///   - 状态敏感方法（tools/call、resources/read）仍走门禁的等待/拒绝分支。
/// </summary>
[Collection("WorkspaceState")]
public sealed class McpRequestHandlerStaticMethodGateTests : IDisposable
{
    private readonly ProjectModel _model;
    private readonly BrowserRenderService _browser;
    private readonly ToolkitServices _services;

    public McpRequestHandlerStaticMethodGateTests()
    {
        _model = ProjectModel.CreateForTesting();
        _model.SetRootForTesting(TestEntityFactory.CreateRoot());
        ProjectLogicContext logic = new(_model);
        _browser = new BrowserRenderService(1, Array.Empty<int>());
        _services = new ToolkitServices(_model, logic, _browser);
    }

    /// <summary>
    /// 修复核心：initializing 状态下 tools/list 立即返回完整工具列表，且不调用 _waitForReady。
    /// registerHandlers:false 只跳过 handler 注册，工具定义（_tools）仍完整填充。
    /// </summary>
    [Fact]
    public async Task ToolsList_When_Initializing_Should_Return_Immediately_Without_Waiting()
    {
        bool waitInvoked = false;
        McpToolRegistry registry = new(_services, "test-root", null, registerHandlers: false);
        McpRequestHandler handler = new(
            registry,
            new McpResourceRegistry(),
            static () => new McpHostStateSnapshot("initializing", "尚未同步", Array.Empty<string>()),
            () => waitInvoked = true);

        JsonRpcRequest request = new()
        {
            Id = new RequestId(1L),
            Method = "tools/list",
            Params = new JsonObject()
        };

        JsonRpcMessage? response = await handler.HandleAsync(request, CancellationToken.None);

        JsonRpcResponse ok = Assert.IsType<JsonRpcResponse>(response);
        ListToolsResult list = JsonSerializer.Deserialize<ListToolsResult>(ok.Result, McpJsonOptions.Output)
            ?? throw new InvalidOperationException("tools/list result deserialized to null");
        Assert.True(list.Tools.Count > 0, "tools/list 应返回已注册的工具定义");
        Assert.False(waitInvoked, "tools/list 是静态元数据，不应触发 _waitForReady");
    }

    /// <summary>
    /// 其余静态列表方法在 initializing 下同样立即返回，不触发 _waitForReady。
    /// resources/list 依赖 skills 目录存在与否，只断言"返回响应"不断言非空。
    /// </summary>
    [Theory]
    [InlineData("resources/list")]
    [InlineData("resources/templates/list")]
    [InlineData("prompts/list")]
    public async Task StaticListMethods_When_Initializing_Should_Return_Without_Waiting(string method)
    {
        bool waitInvoked = false;
        McpToolRegistry registry = new(_services, "test-root", null, registerHandlers: false);
        McpRequestHandler handler = new(
            registry,
            new McpResourceRegistry(),
            static () => new McpHostStateSnapshot("initializing", "尚未同步", Array.Empty<string>()),
            () => waitInvoked = true);

        JsonRpcRequest request = new()
        {
            Id = new RequestId(1L),
            Method = method,
            Params = new JsonObject()
        };

        JsonRpcMessage? response = await handler.HandleAsync(request, CancellationToken.None);

        Assert.IsType<JsonRpcResponse>(response);
        Assert.False(waitInvoked, $"{method} 是静态元数据，不应触发 _waitForReady");
    }

    /// <summary>
    /// 对照组：initializing 状态下 tools/call 走门禁等待分支，触发 _waitForReady；
    /// 等待后状态仍未就绪（_getHostState 固定返回 initializing）则返回错误。
    /// 证明门禁仍对状态敏感方法生效，与上面的静态方法行为形成对比。
    /// </summary>
    [Fact]
    public async Task ToolsCall_When_Initializing_Should_Invoke_WaitForReady()
    {
        bool waitInvoked = false;
        McpToolRegistry registry = new(_services, "test-root", null, registerHandlers: false);
        McpRequestHandler handler = new(
            registry,
            new McpResourceRegistry(),
            static () => new McpHostStateSnapshot("initializing", "尚未同步", Array.Empty<string>()),
            () => waitInvoked = true);

        JsonRpcRequest request = new()
        {
            Id = new RequestId(1L),
            Method = "tools/call",
            Params = new JsonObject()
        };

        JsonRpcMessage? response = await handler.HandleAsync(request, CancellationToken.None);

        Assert.IsType<JsonRpcError>(response);
        Assert.True(waitInvoked, "tools/call 是状态敏感方法，应触发 _waitForReady");
    }

    /// <summary>
    /// blocked 状态下静态元数据方法也应立即返回：工具定义不依赖被外部进程占用的工程文件。
    /// </summary>
    [Fact]
    public async Task ToolsList_When_Blocked_Should_Return_Data()
    {
        McpToolRegistry registry = new(_services, "test-root", null, registerHandlers: false);
        McpRequestHandler handler = new(
            registry,
            new McpResourceRegistry(),
            static () => new McpHostStateSnapshot("blocked", "Host 已阻塞", ["表格/a.xlsx"]));

        JsonRpcRequest request = new()
        {
            Id = new RequestId(1L),
            Method = "tools/list",
            Params = new JsonObject()
        };

        JsonRpcMessage? response = await handler.HandleAsync(request, CancellationToken.None);

        JsonRpcResponse ok = Assert.IsType<JsonRpcResponse>(response);
        ListToolsResult list = JsonSerializer.Deserialize<ListToolsResult>(ok.Result, McpJsonOptions.Output)
            ?? throw new InvalidOperationException("tools/list result deserialized to null");
        Assert.True(list.Tools.Count > 0, "blocked 状态下 tools/list 仍应返回工具定义");
    }

    public void Dispose()
    {
        _browser.Dispose();
        _model.Dispose();
    }
}
