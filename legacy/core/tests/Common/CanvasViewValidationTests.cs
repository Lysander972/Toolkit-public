using Xunit;
using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Common;

/// <summary>
/// CanvasViewValidation 异常落日志测试（F4：catch 块调 _log.Exception，不再静默吞噬）。
///
/// 通过重定向 Console.Error（Log 的输出通道）捕获日志，验证：
/// - 异常被 catch 后仍按既有契约返回错误字符串（不破坏 patch 流程）
/// - 同时写入完整堆栈日志（F4 修复，让用户能看到错误根因）
/// </summary>
public sealed class CanvasViewValidationTests
{
    [Fact]
    public void CollectHtmlLintErrors_WhenHtmlLacksCanvas_ReturnsErrorAndLogsException()
    {
        // ExtractCanvasRootFragment 对无 Canvas 根的 HTML 抛 InvalidOperationException → catch 触发
        StringWriter captured = new();
        TextWriter originalError = Console.Error;
        Console.SetError(captured);
        try
        {
            List<string> errors = CanvasViewValidation.CollectHtmlLintErrors("<div>not canvas</div>", "test.html");

            // 既有契约：返回错误字符串（下游 BuildPatchFromPatchedText 依赖此格式）
            Assert.NotEmpty(errors);
            Assert.Contains("HTML 视图转 Canvas 失败", errors[0]);

            // 日志经 AsyncConsoleErrorWriter 后台线程异步落 stderr（真实异步边界），轮询等待写入
            string logOutput = captured.ToString();
            for (int i = 0; i < 100 && !logOutput.Contains("Canvas HTML Lint 异常", StringComparison.Ordinal); i++)
            {
                Thread.Sleep(20);
                logOutput = captured.ToString();
            }

            // F4 修复：catch 块必须写日志（含上下文 + 完整堆栈），不再静默吞噬
            Assert.Contains("Canvas HTML Lint 异常", logOutput);
            Assert.Contains("HTML缺少Canvas", logOutput);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}
