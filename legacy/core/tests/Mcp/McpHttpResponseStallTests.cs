using Toolkit.Mcp;
using Xunit;

namespace Toolkit.Core.Tests.Mcp;

public sealed class McpHttpResponseStallTests
{
    [Fact]
    public async Task WatchHttpResponseStallAsync_ShouldLogAfterConfiguredDelayWithoutWaitingThirtySeconds()
    {
        List<string> logs = [];

        await McpHostRunner.WatchHttpResponseStallAsync(
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None,
            static () => "WriteAsync",
            "42",
            128,
            static () => false,
            logs.Add);

        string log = Assert.Single(logs);
        Assert.Contains("MCP HTTP 响应发送超过 0s，可能阻塞", log, StringComparison.Ordinal);
        Assert.Contains("id=42", log, StringComparison.Ordinal);
        Assert.Contains("stage=WriteAsync", log, StringComparison.Ordinal);
        Assert.Contains("payloadLength=128", log, StringComparison.Ordinal);
        Assert.Contains("requestAborted=False", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WatchHttpResponseStallAsync_ShouldNotLogWhenCancelled()
    {
        List<string> logs = [];
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await McpHostRunner.WatchHttpResponseStallAsync(
            TimeSpan.FromSeconds(30),
            cts.Token,
            static () => "StartAsync",
            "cancelled",
            1,
            static () => false,
            logs.Add);

        Assert.Empty(logs);
    }
}
