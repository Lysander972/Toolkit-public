using Toolkit.Mcp;
using Xunit;
using System.Diagnostics;
using Toolkit.Core.Analysis;
using Toolkit.Core.Persistence;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Mcp;

[Collection("McpTests")]
public class TscDiagnosticsTests
{
    private readonly McpTestFixture _fixture;

    public TscDiagnosticsTests(McpTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GetTscDiagnostics_ShouldCompleteWithinTimeout()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        // 使用 5 秒超时测试
        const int timeoutMs = 5000;
        Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

        object? result = _fixture.Registry.GetTscDiagnostics(timeoutMs);

        sw.Stop();

        // 验证在超时时间内完成（加 1 秒容差）
        Assert.True(sw.ElapsedMilliseconds < timeoutMs + 1000,
            $"GetTscDiagnostics 耗时 {sw.ElapsedMilliseconds}ms，超过预期 {timeoutMs + 1000}ms");

        // 验证返回结果不为 null
        Assert.NotNull(result);
    }

    [Fact]
    public void GetTscDiagnostics_WithShortTimeout_ShouldNotHang()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        // 极短超时，验证不会卡死
        const int timeoutMs = 100;
        Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

        object? result = _fixture.Registry.GetTscDiagnostics(timeoutMs);

        sw.Stop();

        // 验证在合理时间内返回（即使超时）
        Assert.True(sw.ElapsedMilliseconds < 5000,
            $"GetTscDiagnostics 卡死，耗时 {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void GetTscDiagnostics_NoTsconfig_ShouldReturnSkipped()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        ProjectModel model = ProjectModel.CreateForTesting();
        ProjectLogicContext logic = new ProjectLogicContext(model);
        using BrowserRenderService browser = new(1, Array.Empty<int>());
        ToolkitServices services = new ToolkitServices(model, logic, browser);
        McpToolRegistry registry = new McpToolRegistry(services, McpTestFixture.TestProjectRoot, null, registerHandlers: false);

        Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        object? result = registry.GetTscDiagnostics(1000);
        sw.Stop();

        model.Dispose();

        // 没有 tsconfig 应该立即返回
        Assert.True(sw.ElapsedMilliseconds < 100,
            $"无 tsconfig 时应立即返回，实际耗时 {sw.ElapsedMilliseconds}ms");
    }
}
