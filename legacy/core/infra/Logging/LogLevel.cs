namespace Toolkit.Core.Infra;

/// <summary>
/// 日志级别。
/// 统一使用 Debug/Info/Warning/Error/Fatal 五级。
/// </summary>
public enum LogLevel
{
    /// <summary>调试信息，仅用于开发调试</summary>
    Debug,

    /// <summary>一般信息，用于日志记录</summary>
    Info,

    /// <summary>警告，非致命问题</summary>
    Warning,

    /// <summary>错误，操作失败</summary>
    Error,

    /// <summary>致命错误，系统无法继续</summary>
    Fatal
}
