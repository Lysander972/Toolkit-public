using System.Net.WebSockets;
using Xunit;

namespace Toolkit.Core.Tests.Common;

/// <summary>
/// BrowserRenderService 重启重试逻辑测试（注入 FakeWorker + StubWebSocket，不依赖真实浏览器）。
///
/// 验证 ValidateHtmlRenderCore 的重启 catch（F3 修复）：
/// - 首次抛 CdpConnectionClosedException → worker.Restart() 被调用 → 重试
/// - Restart 失败 → 抛 "重启失败" InvalidOperationException
/// - 业务级异常（非 CdpConnectionClosedException）不触发重启
/// </summary>
using Toolkit.Core.Canvas;

public sealed class BrowserRenderServiceTests
{
    private static WebSocketException ConnectionClosedException() =>
        new(WebSocketError.ConnectionClosedPrematurely);

    private static CdpSession CreateFailingSession() =>
        new(StubWebSocket.WithSend((_, _) => ValueTask.FromException(ConnectionClosedException())));

    [Fact]
    public void ValidateHtmlRenderCore_WhenCdpThrowsConnectionClosed_RestartsOnceThenPropagates()
    {
        // 每次 AcquireSession 返回的 session 都会抛 CdpConnectionClosedException
        FakeWorker worker = new(() => CreateFailingSession());
        BrowserRenderService service = new(1, Array.Empty<int>());

        // 第一次抛 → catch(CdpConnectionClosedException) when(!retried) → Restart → retried=true
        // 第二次抛 → catch when(!retried) 不命中 → CdpConnectionClosedException 冒泡
        Assert.Throws<CdpConnectionClosedException>(() =>
            service.ValidateHtmlRenderCore(worker, "<html></html>", 100, 100));
        // F3 核心：Restart 被调用恰好一次（重启重试生效）
        Assert.Equal(1, worker.RestartCalls);
    }

    [Fact]
    public void ValidateHtmlRenderCore_WhenRestartFails_ThrowsRestartFailureWithInner()
    {
        FakeWorker worker = new(() => CreateFailingSession(), restart: () => false);
        BrowserRenderService service = new(1, Array.Empty<int>());

        // Restart 返回 false → 抛 "重启失败"，InnerException 保留原始 CdpConnectionClosedException
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            service.ValidateHtmlRenderCore(worker, "<html></html>", 100, 100));
        Assert.Contains("重启失败", ex.Message);
        Assert.IsType<CdpConnectionClosedException>(ex.InnerException);
        Assert.Equal(1, worker.RestartCalls);
    }

    [Fact]
    public void ValidateHtmlRenderCore_WhenBusinessException_DoesNotRestart()
    {
        // 业务级异常（CDP JS 异常模拟）：不触发 Restart，直接冒泡
        StubWebSocket stubWs = StubWebSocket.WithReceive((_, _) =>
            new ValueTask<ValueWebSocketReceiveResult>(
                new ValueWebSocketReceiveResult(0, WebSocketMessageType.Text, true)));
        // Send 成功，Receive 返回非法 JSON → JsonException 冒泡（非 CdpConnectionClosedException）
        FakeWorker worker = new(() => new CdpSession(stubWs));
        BrowserRenderService service = new(1, Array.Empty<int>());

        // 业务异常直接冒泡，不进重启 catch
        Assert.ThrowsAny<Exception>(() =>
            service.ValidateHtmlRenderCore(worker, "<html></html>", 100, 100));
        Assert.Equal(0, worker.RestartCalls);
    }
}
