namespace Toolkit.Core.Infra;

/// <summary>
/// 日志模块标识。
/// 统一模块前缀，确保日志来源清晰可追踪。
/// </summary>
public enum LogModule
{
    /// <summary>通用/未分类</summary>
    General,

    /// <summary>核心库</summary>
    Core,

    /// <summary>MCP 服务器</summary>
    Mcp,

    /// <summary>Host 进程</summary>
    Host,

    /// <summary>运行时错误收集</summary>
    Runtime,

    /// <summary>TS 视图</summary>
    TsView,

    /// <summary>Canvas 视图</summary>
    Canvas,

    /// <summary>Lint 分析</summary>
    Lint,

    /// <summary>代码重构</summary>
    Refactor,

    /// <summary> Bescript 解析</summary>
    Bescript,

    /// <summary>工作进程</summary>
    Worker
}

/// <summary>
/// LogModule 扩展方法。
/// </summary>
public static class LogModuleExtensions
{
    /// <summary>
    /// 获取模块的日志标签。
    /// </summary>
    public static string GetTag(this LogModule module) => module switch
    {
        LogModule.General => "general",
        LogModule.Core => "core",
        LogModule.Mcp => "mcp",
        LogModule.Host => "host",
        LogModule.Runtime => "runtime",
        LogModule.TsView => "tsview",
        LogModule.Canvas => "canvas",
        LogModule.Lint => "lint",
        LogModule.Refactor => "refactor",
        LogModule.Bescript => "bescript",
        LogModule.Worker => "worker",
        _ => "unknown"
    };
}
