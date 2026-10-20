using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests;

/// <summary>
/// 测试中格式化错误信息的辅助类。
/// </summary>
internal static class TestErrorFormatter
{
    /// <summary>
    /// 格式化单个错误,包含 StackTrace 信息（仅栈顶一帧）。
    /// </summary>
    public static string Format(ToolError error)
    {
        string basic = $"{error.Path}#{error.Line} {error.Message}";
        string firstFrame = StackTraceFormatter.ExtractFirstFrame(error.StackTrace);
        return $"{basic}\nStackTrace:\n{firstFrame}";
    }

    /// <summary>
    /// 格式化多个错误。
    /// </summary>
    public static string FormatAll(IReadOnlyList<ToolError> errors)
    {
        if (errors.Count == 0)
        {
            return "(无错误)";
        }
        if (errors.Count == 1)
        {
            return Format(errors[0]);
        }
        return string.Join("\n---\n", errors.Select(Format));
    }
}
