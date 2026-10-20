namespace Toolkit.Core.Lint;

/// <summary>
/// Lint 诊断严重级别。
/// Info &lt; Warning &lt; Error &lt; Fatal。
/// </summary>
public enum LintSeverity
{
    /// <summary>提示，非问题性的建议信息</summary>
    Info,

    /// <summary>警告，非阻断性问题</summary>
    Warning,

    /// <summary>错误，影响功能正确性</summary>
    Error,

    /// <summary>致命错误，结构损坏，停止分析</summary>
    Fatal
}

/// <summary>
/// Lint 定位信息。
/// 简化设计：统一使用 Path + Line，不再区分脚本/零件/载体。
/// </summary>
public sealed record LintLocation(
    /// <summary>文件相对路径</summary>
    string Path,
    /// <summary>行号（1-based）</summary>
    int Line
);

/// <summary>
/// Lint 诊断信息。
/// </summary>
public sealed record LintDiagnostic(
    string RuleId,
    LintSeverity Severity,
    string Message,
    LintLocation Location
);
