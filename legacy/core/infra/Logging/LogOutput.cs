
namespace Toolkit.Core.Infra;

// =============================================================================
// 统一控制台日志输出 / CONSOLE LOG OUTPUT
// =============================================================================
//
// 设计原则：
// 1. 只接受控制台输出，统一规范，拒绝分裂
// 2. 所有日志通过 stdout/stderr 输出，由前端插件统一接收处理
// 3. Host 的全部输出重定向给前端，逻辑收归一起
//
// 格式: [toolkit] [模块] [级别] 消息
// 示例: [toolkit] [mcp] [ERR] 服务器启动失败
//
// =============================================================================

/// <summary>
/// 日志输出接口。
/// 统一契约：所有输出必须走控制台，由前端接收处理。
/// </summary>
public interface ILogOutput
{
    void WriteLine(LogLevel level, LogModule module, string message);
    bool IsEnabled(LogLevel level);
    LogLevel MinLevel { get; set; }
}

/// <summary>
/// 统一控制台日志输出。
/// 唯一输出目标，所有日志经此流向插件前端。
/// </summary>
public sealed class ConsoleLogOutput : ILogOutput
{
    public LogLevel MinLevel { get; set; } = LogLevel.Info;

    public bool IsEnabled(LogLevel level) => level >= MinLevel;

    public void WriteLine(LogLevel level, LogModule module, string message)
    {
        if (!IsEnabled(level)) return;

        string formatted = Format(level, module, message);

        // 所有日志统一走 stderr，stdout 专用于协议数据；
        // 必须经异步写入器——同步写会在下游读取停滞时焊死调用线程。
        AsyncConsoleErrorWriter.WriteLine(formatted);
    }

    private static string Format(LogLevel level, LogModule module, string message)
    {
        string levelTag = level switch
        {
            LogLevel.Debug => "[DBG]",
            LogLevel.Info => "[INF]",
            LogLevel.Warning => "[WRN]",
            LogLevel.Error => "[ERR]",
            LogLevel.Fatal => "[FTL]",
            _ => "[???]"
        };

        // 转义消息中的换行符，保持日志单行格式
        string escapedMessage = TextLineEndings.ToLf(message)
            .Replace("\n", "\\n");

        // 统一三层结构: [toolkit] [模块] [级别] 消息
        return $"[toolkit] [{module.GetTag()}] {levelTag} {escapedMessage}";
    }
}
