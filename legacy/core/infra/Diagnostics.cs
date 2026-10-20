using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Toolkit.Core.Infra;

// =============================================================================
// 通用诊断模型 / COMMON DIAGNOSTICS
// =============================================================================
//
// 本文件包含全栈统一的诊断/异常模型定义。
//
// 设计原则：
// 1. 通用性 - 只包含跨层通用的基础诊断类型
// 2. 无机制耦合 - 不包含特定机制（如实体持久化、Lint、Analysis）的错误收集器
// 3. 扩展方式 - 特定机制的错误收集器应定义在各自模块，复用基础类型
//
// 禁止：
// 1. 添加特定机制的错误收集器（如 EntityLoadErrorCollector）
// 2. 添加特定模块的异常类型
//
// =============================================================================

/// <summary>
/// 堆栈追踪格式化工具。
/// 保留异常完整调用栈，简化文件路径。
/// </summary>
public static class StackTraceFormatter
{
    private static readonly Regex FilePathPattern = new(@" in (.+):line (\d+)", RegexOptions.Compiled);
    /// <summary>
    /// 格式化异常堆栈：保留完整异常链，简化路径。
    /// </summary>
    public static string FormatException(Exception ex)
    {
        string[] lines = TextLineEndings.ToLf(ex.ToString()).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = SimplifyFilePath(lines[i]);
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// 从完整堆栈中提取栈顶一帧并简化路径。
    /// </summary>
    public static string ExtractFirstFrame(string stackTrace)
    {
        string frames = ExtractFirstFiveFrames(stackTrace);
        int lineBreak = frames.IndexOf('\n', StringComparison.Ordinal);
        return lineBreak < 0 ? frames : frames[..lineBreak];
    }

    /// <summary>
    /// 从完整堆栈中提取栈顶最多五帧并简化路径。
    /// </summary>
    public static string ExtractFirstFiveFrames(string? stackTrace)
    {
        if (stackTrace == null) return string.Empty;

        List<string> frames = [];
        string[] lines = TextLineEndings.ToLf(stackTrace).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
#pragma warning disable TK010 // 栈追踪行来自外部异常，可能有前导空白
            string trimmed = line.TrimStart();
#pragma warning restore TK010
#pragma warning disable TK008 // 栈追踪空行跳过
            if (trimmed.Length == 0 || trimmed.StartsWith("---", StringComparison.Ordinal))
#pragma warning restore TK008
            {
                continue;
            }

            frames.Add(SimplifyFilePath(trimmed));
            if (frames.Count == 5)
                break;
        }
        return string.Join("\n", frames);
    }

    /// <summary>
    /// 简化堆栈帧中的文件路径，只保留文件名。
    /// </summary>
    public static string SimplifyFilePath(string line)
    {
        Match match = FilePathPattern.Match(line);
        if (match.Success)
        {
            string fullPath = match.Groups[1].Value;
            string lineNum = match.Groups[2].Value;
            string fileName = SystemIOWrapper.GetLastSegment(fullPath);
            return line.Substring(0, match.Index) + $" in {fileName}:line {lineNum}";
        }
        return line;
    }
}

/// <summary>
/// 诊断严重程度。
/// Warning/Error/Fatal 用于代码诊断；Info 用于操作状态提示（如草稿保存通知）。
/// </summary>
public enum ToolDiagnosticSeverity
{
    /// <summary>操作状态提示，不阻断流程，无风险含义。</summary>
    Info,

    /// <summary>提示性警告，不阻断流程。</summary>
    Warning,

    /// <summary>普通错误，参与增量对比（新错阻断，旧错留存）。</summary>
    Error,

    /// <summary>致命错误，表示结构已损坏，立即本层阻断，停止后续分析。</summary>
    Fatal
}

/// <summary>
/// 定位信息。
/// </summary>
public sealed record SourceLocation
{
    public string Path { get; init; } = string.Empty;
    public int Line { get; init; }
    public int Column { get; init; }

    public bool HasLocation => Path.Length > 0 || Line > 0;

    public string Format()
    {
#pragma warning disable TK008 // 无路径时不包含路径前缀
        if (Path.Length == 0)
#pragma warning restore TK008
        {
            return Line <= 0
                ? string.Empty
                : Column > 0 ? $"{Line}:{Column}" : $"{Line}";
        }

        if (Line <= 0)
        {
            return Path;
        }

        return Column > 0
            ? $"{Path}:{Line}:{Column}"
            : $"{Path}:{Line}";
    }

}

/// <summary>
/// 统一的错误/诊断上报模型。
/// </summary>
public sealed record ToolError(
    string? Path,
    int? Line,
    int? Column,
    string Message,
    ToolDiagnosticSeverity Severity = ToolDiagnosticSeverity.Error)
{
    public string? Code { get; init; }

    public string? Subject { get; init; }

    /// <summary>
    /// 源文件定位。
    /// </summary>
    public SourceLocation Source { get; init; } = new();

    /// <summary>
    /// 附加说明列表，与 Message 一起序列化为 message 数组。
    /// </summary>
    public IReadOnlyList<string>? ExtraMessages { get; init; }

    /// <summary>
    /// 错误产生位置的堆栈追踪（最多栈顶五帧）。
    /// </summary>
    public string StackTrace { get; init; } = StackTraceFormatter.ExtractFirstFiveFrames(new StackTrace(1, true).ToString());
}

// =============================================================================
// 运行时错误收集机制 / RUNTIME ERROR COLLECTION
// =============================================================================
//
// 设计原则：
// 1. RuntimeError 与 ToolError 严格分离 - 前者用于运行时异常，后者用于代码分析
// 2. 上下文关联 - 每个错误携带请求ID、操作类型等上下文
// 3. 边界拦截 - 在 MCP 入口、文件 IO、Host 通信等边界统一收集
// 4. 攻击性编程 - 内部异常直接抛出，边界统一捕获转化
//
// 使用场景：
// - MCP 请求执行期间的未处理异常
// - 文件 IO 操作失败
// - Host 进程通信错误
// - 其他运行时边界点的异常
//
// =============================================================================

/// <summary>
/// 运行时严重级别。
/// 统一使用 Warning/Error/Fatal 三级。
/// </summary>
public enum RuntimeErrorSeverity
{
    /// <summary>警告 - 操作完成但可能有副作用</summary>
    Warning,

    /// <summary>错误 - 操作失败但系统可继续</summary>
    Error,

    /// <summary>致命错误 - 影响后续操作，需要关注</summary>
    Fatal
}

/// <summary>
/// 运行时错误模型。
/// 与 ToolError 严格分离，专门用于收集运行时边界异常。
/// </summary>
public sealed record RuntimeError
{
    /// <summary>严重级别</summary>
    public required RuntimeErrorSeverity Severity { get; init; }

    /// <summary>错误消息</summary>
    public required string Message { get; init; }

    /// <summary>关联的请求/操作ID</summary>
    public string? OperationId { get; init; }

    /// <summary>操作类型描述</summary>
    public string? OperationType { get; init; }

    /// <summary>异常类型全名</summary>
    public string? ExceptionType { get; init; }

    /// <summary>异常堆栈追踪</summary>
    public string? StackTrace { get; init; }

    /// <summary>创建从异常构造的运行时错误</summary>
    public static RuntimeError FromException(
        Exception ex,
        string? operationId = null,
        string? operationType = null,
        RuntimeErrorSeverity severity = RuntimeErrorSeverity.Error)
    {
        ArgumentNullException.ThrowIfNull(ex);

        // 展开 AggregateException 显示所有内部错误
        string message;
        if (ex is AggregateException aggEx && aggEx.InnerExceptions.Count > 0)
        {
            string[] innerMessages = aggEx.InnerExceptions.Select(static e => $"[{e.GetType().Name}] {e.Message}").ToArray();
            message = $"{ex.Message}: {string.Join("; ", innerMessages)}";
        }
        else
        {
            message = ex.Message;
        }

        return new RuntimeError
        {
            Severity = severity,
            Message = message,
            OperationId = operationId,
            OperationType = operationType,
            ExceptionType = ex.GetType().FullName,
            StackTrace = ex.StackTrace
        };
    }
}

/// <summary>
/// 运行时错误收集器。
/// 统一收集运行时边界点的错误。
///
/// 职责：
/// 1. 结构化收集运行时错误（支持后续分析、上报）
/// 2. 同时将错误输出到 Log（统一日志系统）
///
/// 注意：此为运行时错误收集器，与 EntityLoadErrorCollector（代码分析错误）严格分离。
/// </summary>
public sealed class RuntimeErrorCollector
{
    private readonly List<RuntimeError> _errors = [];

    /// <summary>当前操作/请求的ID</summary>
    public string? OperationId { get; }

    /// <summary>当前操作类型</summary>
    public string? OperationType { get; }

    /// <summary>是否存在任何错误</summary>
    public bool HasErrors => _errors.Count > 0;

    /// <summary>是否同时输出到日志（默认 true）</summary>
    public bool EnableLogOutput { get; set; } = true;

    /// <summary>创建收集器</summary>
    public RuntimeErrorCollector(string? operationId = null, string? operationType = null)
    {
        OperationId = operationId;
        OperationType = operationType;
    }

    /// <summary>从异常创建并记录错误</summary>
    public void AddException(
        Exception ex,
        RuntimeErrorSeverity severity = RuntimeErrorSeverity.Error)
    {
        RuntimeError error = RuntimeError.FromException(
            ex, OperationId, OperationType, severity);
        _errors.Add(error);

        // 输出到通用日志
        if (EnableLogOutput)
        {
            LogRuntimeError(error);
        }
    }

    /// <summary>将运行时错误输出到通用日志</summary>
    private void LogRuntimeError(RuntimeError error)
    {
        LogLevel level = error.Severity switch
        {
            RuntimeErrorSeverity.Warning => LogLevel.Warning,
            RuntimeErrorSeverity.Error => LogLevel.Error,
            RuntimeErrorSeverity.Fatal => LogLevel.Fatal,
            _ => throw new UnreachableException($"Unexpected severity: {error.Severity}")
        };

        StringBuilder sb = new();
        sb.Append($"[core] [RuntimeError] Severity={error.Severity}");

        if (error.OperationId is not null)
        {
            sb.Append($", OpId={error.OperationId}");
        }
        if (error.OperationType is not null)
        {
            sb.Append($", OpType={error.OperationType}");
        }

        sb.Append($": {error.Message}");

        if (error.ExceptionType is not null)
        {
            sb.Append($" [{error.ExceptionType}]");
        }

        string logMessage = sb.ToString();

        Log.For(LogModule.Runtime).Write(level, logMessage);

        if (error.StackTrace is not null)
        {
            Log.For(LogModule.Runtime).Debug(StackTraceFormatter.ExtractFirstFiveFrames(error.StackTrace));
        }
    }
}

/// <summary>
/// 运行时错误上下文。
/// 使用 AsyncLocal 在异步操作中传递错误收集器。
/// </summary>
public static class RuntimeErrorContext
{
    private static readonly AsyncLocal<RuntimeErrorCollector?> _currentCollector = new();

    /// <summary>
    /// 设置当前错误收集器。
    /// 返回一个作用域对象，在 using 语句结束时自动清理。
    /// </summary>
    public static RuntimeErrorScope SetCollector(RuntimeErrorCollector collector)
    {
        ArgumentNullException.ThrowIfNull(collector);
        _currentCollector.Value = collector;
        return new RuntimeErrorScope();
    }

    /// <summary>清除当前错误收集器</summary>
    public static void ClearCollector()
    {
        _currentCollector.Value = null;
    }
}

/// <summary>
/// 运行时错误上下文作用域。
/// 用于自动清理 AsyncLocal 状态。
/// </summary>
public struct RuntimeErrorScope : IDisposable
{
    /// <summary>作用域是否已处置</summary>
    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        if (IsDisposed) return;
        RuntimeErrorContext.ClearCollector();
        IsDisposed = true;
    }
}
