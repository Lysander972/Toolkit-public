
namespace Toolkit.Core.Infra;

// =============================================================================
// 统一日志系统 / UNIFIED LOGGING
// =============================================================================
//
// 设计原则：
// 1. 单一输出：只通过控制台输出，前端统一接收
// 2. 统一格式：[toolkit] [模块] [级别] 消息
// 3. 模块隔离：各模块使用自己的 Log 实例，避免混淆
// 4. 无文件日志：所有日志经 stdout/stderr 流向插件
//
// 使用方式：
//   Log.For(LogModule.Mcp).Info("服务器已启动");
//   Log.For(LogModule.Host).Error("启动失败");
//
// =============================================================================

/// <summary>
/// 统一日志入口。
/// </summary>
public sealed class Log
{
    private static readonly Dictionary<LogModule, Log> _moduleLogs = [];
    private static readonly object _initLock = new();
    private static ILogOutput? _output;
    private static bool _initialized;

    private readonly LogModule _module;

    private Log(LogModule module)
    {
        _module = module;
    }

    /// <summary>
    /// 初始化日志系统。
    /// 必须在首次使用之前调用。
    /// </summary>
    public static void Initialize(LogLevel minLevel = LogLevel.Info)
    {
        lock (_initLock)
        {
            if (_initialized) return;

            _output = new ConsoleLogOutput { MinLevel = minLevel };
            _initialized = true;
        }
    }

    /// <summary>
    /// 获取指定模块的日志实例。
    /// </summary>
    public static Log For(LogModule module)
    {
        lock (_initLock)
        {
            if (!_initialized)
            {
                // 自动初始化（默认 Info 级别）
                Initialize();
            }

            if (!_moduleLogs.ContainsKey(module))
            {
                _moduleLogs[module] = new Log(module);
            }
            return _moduleLogs[module];
        }
    }



    /// <summary>
    /// 记录 Debug 日志。
    /// </summary>
    public void Debug(string message)
    {
        Write(LogLevel.Debug, message);
    }

    /// <summary>
    /// 记录 Info 日志。
    /// </summary>
    public void Info(string message)
    {
        Write(LogLevel.Info, message);
    }

    /// <summary>
    /// 记录 Warning 日志。
    /// </summary>
    public void Warning(string message)
    {
        Write(LogLevel.Warning, message);
    }

    /// <summary>
    /// 记录 Error 日志。
    /// </summary>
    public void Error(string message)
    {
        Write(LogLevel.Error, message);
    }

    /// <summary>
    /// 记录 Fatal 日志。
    /// </summary>
    public void Fatal(string message)
    {
        Write(LogLevel.Fatal, message);
    }

    /// <summary>
    /// 记录异常。
    /// </summary>
    public void Exception(Exception ex, string? context = null)
    {
        ArgumentNullException.ThrowIfNull(ex);
        string detail = StackTraceFormatter.FormatException(ex);
        string msg = string.IsNullOrWhiteSpace(context)
            ? detail
            : $"[{context}] {detail}";
        Error(msg);
    }

    /// <summary>
    /// 写入指定级别的日志。
    /// </summary>
    public void Write(LogLevel level, string message)
    {
        _output?.WriteLine(level, _module, message);
    }
}
