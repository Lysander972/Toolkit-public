using System.Net.Sockets;
using System.Net.WebSockets;
using Xunit;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Common;

/// <summary>
/// CdpSession 连接级故障路径测试（注入 stub WebSocket，真实触发包装逻辑，不依赖网络服务器）。
///
/// CdpSession 依赖 WebSocket 抽象基类而非 ClientWebSocket（sealed 不可 mock），
/// 测试通过注入 StubWebSocket 子类精确控制 SendAsync/ReceiveAsync 行为，验证：
/// - SendAsync/ReceiveAsync 抛 WebSocketException 时被包装为 CdpConnectionClosedException（R1 修复，让重启重试能命中）
/// - Receive 收到 Close 帧时抛 CdpConnectionClosedException（R1 的优雅关闭路径）
/// - IsAlive 反映 WebSocket.State（F2 修复，让 AcquireSession 不复用死 session）
/// - IsConnectionClosed 判定逻辑正确区分连接级 vs 业务级异常
/// </summary>
public sealed class CdpSessionConnectionTests
{
    private static WebSocketException ConnectionClosedException() =>
        new(WebSocketError.ConnectionClosedPrematurely);

    [Fact]
    public void IsConnectionClosed_IdentifiesConnectionFaults()
    {
        // 连接级异常 → true（这些是触发 worker 重启的信号）
        Assert.True(CdpSession.IsConnectionClosed(ConnectionClosedException()));
        Assert.True(CdpSession.IsConnectionClosed(new IOException("io")));
        Assert.True(CdpSession.IsConnectionClosed(new SocketException()));
        Assert.True(CdpSession.IsConnectionClosed(new OperationCanceledException("cancel", new IOException("inner io"))));

        // 业务级异常 → false（CDP 命令失败、JS 异常等不应触发重启）
        Assert.False(CdpSession.IsConnectionClosed(new InvalidOperationException("业务错误")));
        Assert.False(CdpSession.IsConnectionClosed(new OperationCanceledException("纯取消")));
    }

    [Fact]
    public async Task SendCommandAsync_WhenWebSocketSendThrows_WrapsAsCdpConnectionClosedException()
    {
        // R1 核心逃逸点：底层 SendAsync 抛 WebSocketException 时必须被包装，否则 BrowserRenderService 重启重试字符串匹配失效
        using CdpSession session = new(StubWebSocket.WithSend((_, _) => ValueTask.FromException(ConnectionClosedException())));

        CdpConnectionClosedException ex = await Assert.ThrowsAsync<CdpConnectionClosedException>(
            () => session.SendCommandAsync("Page.enable", null, CancellationToken.None));
        Assert.IsType<WebSocketException>(ex.InnerException);
    }

    [Fact]
    public async Task SendCommandAsync_WhenReceiveGetsCloseFrame_ThrowsCdpConnectionClosedException()
    {
        // 对端发送 Close 帧（优雅关闭）：ReceiveMessageAsync 检测到 MessageType.Close 必须抛
        using CdpSession session = new(StubWebSocket.WithReceive((_, _) =>
            new ValueTask<ValueWebSocketReceiveResult>(new ValueWebSocketReceiveResult(0, WebSocketMessageType.Close, true))));

        CdpConnectionClosedException ex = await Assert.ThrowsAsync<CdpConnectionClosedException>(
            () => session.SendCommandAsync("Page.enable", null, CancellationToken.None));
        // Close 帧路径无底层异常，InnerException 必须为 null
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task SendCommandAsync_WhenWebSocketReceiveThrows_WrapsAsCdpConnectionClosedException()
    {
        // 强制断开（abort，不发 Close 帧）：ReceiveAsync 抛 WebSocketException 必须被包装
        using CdpSession session = new(StubWebSocket.WithReceive((_, _) =>
            ValueTask.FromException<ValueWebSocketReceiveResult>(ConnectionClosedException())));

        CdpConnectionClosedException ex = await Assert.ThrowsAsync<CdpConnectionClosedException>(
            () => session.SendCommandAsync("Page.enable", null, CancellationToken.None));
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void IsAlive_ReflectsWebSocketState()
    {
        // F2：AcquireSession 用 IsAlive 判定是否复用 session，必须真实反映底层连接状态
        using CdpSession open = new(StubWebSocket.WithState(WebSocketState.Open));
        Assert.True(open.IsAlive);

        using CdpSession closed = new(StubWebSocket.WithState(WebSocketState.Closed));
        Assert.False(closed.IsAlive);

        using CdpSession aborted = new(StubWebSocket.WithState(WebSocketState.Aborted));
        Assert.False(aborted.IsAlive);
    }
}
