using Xunit;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests;

/// <summary>
/// 验证 ToolError 的 StackTrace 捕获。
/// </summary>
public sealed class ToolErrorStackTraceTests
{
    [Fact]
    public void ToolError_ShouldCaptureStackTrace()
    {
        ToolError error = new ToolError("test.ts", 10, 5, "测试错误");

        Assert.NotNull(error.StackTrace);
        Assert.NotEmpty(error.StackTrace);
        Assert.Contains("ToolErrorStackTraceTests", error.StackTrace);
    }

    [Fact]
    public void TestErrorFormatter_ShouldIncludeStackTrace()
    {
        ToolError error = new ToolError("test.ts", 10, 5, "测试错误");
        string formatted = TestErrorFormatter.Format(error);

        Assert.Contains("test.ts#10 测试错误", formatted);
        Assert.Contains("StackTrace:", formatted);
        Assert.Contains("ToolErrorStackTraceTests", formatted);
    }
}
