using System.Text.Json;
using Toolkit.Mcp;
using Xunit;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests;

/// <summary>
/// 验证 ToolError 的 StackTrace 字段存在且可访问。
/// MCP 序列化排除 StackTrace 的测试在 MCP 项目中。
/// </summary>
public sealed class ToolErrorSerializationTests
{
    [Fact]
    public void ToolError_ShouldHaveStackTraceProperty()
    {
        ToolError error = new ToolError("test.ts", 10, 5, "测试错误");

        // StackTrace 应该存在且不为空
        Assert.NotNull(error.StackTrace);
        Assert.NotEmpty(error.StackTrace);
    }

    [Fact]
    public void StandardJsonSerialization_ShouldIncludeStackTrace()
    {
        ToolError error = new ToolError("test.ts", 10, 5, "测试错误");

        string json = JsonSerializer.Serialize(error);

        Assert.Contains("\"StackTrace\":", json);
        Assert.Contains("test.ts", json);
    }

    [Fact]
    public void McpOutputSerialization_UsesCamelCaseNaming()
    {
        ToolError error = new ToolError("test.ts", 10, 5, "测试错误");

        string json = JsonSerializer.Serialize(error, McpJsonOptions.Output);

        Assert.Contains("stackTrace", json);
        Assert.Contains("test.ts", json);
    }
}
