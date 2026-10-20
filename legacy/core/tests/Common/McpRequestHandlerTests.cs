using System.Net.WebSockets;
using Toolkit.Mcp;
using Xunit;

namespace Toolkit.Core.Tests.Common;

/// <summary>
/// McpRequestHandler 异常格式化测试（F6：FormatExceptionChain 展开 InnerException 链）。
/// </summary>
public sealed class McpRequestHandlerTests
{
    [Fact]
    public void FormatExceptionChain_WithInnerException_IncludesInnerTypeAndMessage()
    {
        // CDP 场景：CdpConnectionClosedException 包装 WebSocketException，InnerException 必须被展开不丢
        Exception inner = new WebSocketException("底层 WebSocket 故障");
        Exception outer = new InvalidOperationException("CDP 连接已关闭", inner);

        string result = McpRequestHandler.FormatExceptionChain(outer);

        Assert.Equal("[InvalidOperationException] CDP 连接已关闭 -> [WebSocketException] 底层 WebSocket 故障", result);
    }

    [Fact]
    public void FormatExceptionChain_WithoutInnerException_ReturnsSingleEntry()
    {
        Exception ex = new InvalidOperationException("单独错误");

        string result = McpRequestHandler.FormatExceptionChain(ex);

        Assert.Equal("[InvalidOperationException] 单独错误", result);
    }
}
