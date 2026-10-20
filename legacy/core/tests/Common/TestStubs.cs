using System.Net.WebSockets;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Common;

/// <summary>
/// CdpSession 测试用的可控 WebSocket 子类：send/receive 用委托注入，State 可配置。
/// 让 CdpSession 的包装逻辑能在不依赖真实网络的情况下被精确触发。
/// </summary>
internal sealed class StubWebSocket : WebSocket
{
    public static readonly Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> NoopSend =
        (_, _) => ValueTask.CompletedTask;
    public static readonly Func<Memory<byte>, CancellationToken, ValueTask<ValueWebSocketReceiveResult>> NoopReceive =
        (_, _) => new ValueTask<ValueWebSocketReceiveResult>(new ValueWebSocketReceiveResult(0, WebSocketMessageType.Text, true));

    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> _send;
    private readonly Func<Memory<byte>, CancellationToken, ValueTask<ValueWebSocketReceiveResult>> _receive;

    public override WebSocketState State { get; }
    public override WebSocketCloseStatus? CloseStatus => null;
    public override string CloseStatusDescription => string.Empty;
    public override string SubProtocol => string.Empty;

    private StubWebSocket(WebSocketState state,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> send,
        Func<Memory<byte>, CancellationToken, ValueTask<ValueWebSocketReceiveResult>> receive)
    {
        State = state;
        _send = send;
        _receive = receive;
    }

    public static StubWebSocket WithState(WebSocketState state) => new(state, NoopSend, NoopReceive);
    public static StubWebSocket WithSend(Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> send) => new(WebSocketState.Open, send, NoopReceive);
    public static StubWebSocket WithReceive(Func<Memory<byte>, CancellationToken, ValueTask<ValueWebSocketReceiveResult>> receive) => new(WebSocketState.Open, NoopSend, receive);

    public override ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        => _send(buffer, cancellationToken);

    public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        => throw new NotSupportedException("StubWebSocket 只支持 Memory<byte> 重载");

    public override ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        => _receive(buffer, cancellationToken);

    public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        => throw new NotSupportedException("StubWebSocket 只支持 Memory<byte> 重载");

    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public override void Abort() { }
    public override void Dispose() { }
}

/// <summary>
/// IBrowserWorker 测试桩：控制 AcquireSession 返回值 + 计数 Restart 调用。
/// 用于验证 BrowserRenderService.ValidateHtmlRenderCore 的重启重试逻辑（F3）。
/// </summary>
internal sealed class FakeWorker : IBrowserWorker
{
    private readonly Func<CdpSession?> _acquireSession;
    private readonly Func<bool> _restart;
    public int RestartCalls;

    public FakeWorker(Func<CdpSession?> acquireSession)
        : this(acquireSession, () => true) { }

    public FakeWorker(Func<CdpSession?> acquireSession, Func<bool> restart)
    {
        _acquireSession = acquireSession;
        _restart = restart;
    }

    public int? BrowserProcessId => null;
    public CdpSession? AcquireSession() => _acquireSession();
    public bool Restart() { RestartCalls++; return _restart(); }
    public void Dispose() { }
}
