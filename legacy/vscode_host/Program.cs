using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Toolkit.Core;
using Toolkit.Core.Infra;
using Toolkit.Mcp;

using Toolkit.Core.Analysis;
using Toolkit.Core.Lint;
using Toolkit.Core.Refactor;
using Toolkit.Core.Worker;

using Toolkit.Core.Canvas;

using Toolkit.Core.Persistence;

namespace Toolkit.VSCodePluginHost;

internal static class Program
{
    private const string LintDiagnosticsPrefix = "[LINT_DIAGNOSTICS]";
    private const int LintPushDebounceMs = 120;
    private static readonly object LintPushLock = new();
    private static Timer? LintPushTimer;
    private static long _lastLintPushVersion = -1;
    // 浏览器渲染服务（局部提升为字段：CleanupAndExit 退出时必须 Dispose，
    // 否则 chrome 子进程成为孤儿泄漏，见 AGENTS.md 浏览器滥用防护）
    private static BrowserRenderService? _browserRenderService;

    // 脚本组一致性自愈（保护性交集强刷）的软失败状态
    private static int _consecutiveReconcileFailures;
    private static long _reconcileDisabledUntilTicks;
    private const int ReconcileFailureThreshold = 3;
    private static readonly long ReconcileCooldownTicks = TimeSpan.FromMinutes(1).Ticks;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [ExternalInterface] // exe 宿主入口（OS/启动器调用，仓内无调用方）
    public static void Main(string[] args)
    {
        // 确保主机进程使用 UTF-8 读取/写出，避免路径中的中文被误解码
        Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        // 启动方式唯一：Host 进程统一初始化，MCP 作为子组件共用同一套 Model 和 Logic 实例

        // 工程根目录固定使用当前工作目录（绝对路径）
        string projectRootFullPath = Directory.GetCurrentDirectory();

        // 初始化日志系统 - 统一走控制台输出，由前端接收处理
        Log.Initialize(LogLevel.Info);
        Log.For(LogModule.Host).Info($"Host 进程启动: 工程根目录={projectRootFullPath}");

        // 初始化核心单例（唯一数据源）
        ToolkitConfig config = new(ToolkitUserPaths.ConfigFileFullPath);
        int renderWorkerCount = config.GetInt("browser.render_worker_count");
        string browserExecutable = config.GetString("browser.executable_path");
        BrowserRenderService browser = new BrowserRenderService(renderWorkerCount, Array.Empty<int>(), disableExtensions: true, useTemporaryUserDataDir: true, keepBrowserProcesses: false, browserExecutableFullPath: browserExecutable.Length > 0 ? browserExecutable : null);
        _browserRenderService = browser;
        browser.Initialize();
        Log.For(LogModule.Host).Info("加载工程模型...");
        Stopwatch modelLoadStopwatch = Stopwatch.StartNew();
        ToolkitServices services = ToolkitServices.InitializeSingleton(projectRootFullPath, browser);
        Log.For(LogModule.Host).Info($"工程模型加载完成, 用时 {modelLoadStopwatch.ElapsedMilliseconds}ms");
        WorkerCommandHandler handler = new WorkerCommandHandler(services);
        services.Model.ModelRefreshed += (_, _) => ScheduleLintPush(services);
        ScheduleLintPush(services);

        // MCP 作为子组件启动时，共用同一套 Model 和 Logic 实例
        Log.For(LogModule.Host).Info("检查 MCP 服务配置...");
        Task? mcpTask = StartMcpInBackgroundIfEnabled(args, services, projectRootFullPath);

        Log.For(LogModule.Host).Info("Host 进程准备就绪，进入消息循环");
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            WorkerRequest? req;
            try
            {
                req = JsonSerializer.Deserialize<WorkerRequest>(line, JsonOptions);
            }
            catch (JsonException ex)
            {
                WriteResponse(new WorkerResponse(Ok: false, Message: $"JSON 解析失败: {ex.Message}"));
                continue;
            }

            WorkerResponse response = handler.Handle(req);
            if (req?.RequestId != null)
            {
                response = response with { RequestId = req.RequestId };
            }
            WriteResponse(response);
        }

        // stdin 关闭意味着插件侧不再通信，清理资源后退出
        Log.For(LogModule.Host).Info("stdin 已关闭，Host 进程退出");
        CleanupAndExit();
    }

    private static void CleanupAndExit()
    {
        try
        {
            lock (LintPushLock)
            {
                LintPushTimer?.Dispose();
                LintPushTimer = null;
            }
            ToolkitServices.ShutdownSingleton();
            FileSystemCache.Shutdown();
            // 浏览器进程树随 Host 退出显式释放（杀树 + 删临时目录），防止 chrome 孤儿泄漏
            _browserRenderService?.Dispose();
            _browserRenderService = null;
            Log.For(LogModule.Host).Info("资源清理完成");
        }
        catch (Exception ex)
        {
            Log.For(LogModule.Host).Exception(ex, "资源清理异常");
        }
        Environment.Exit(0);
    }

    private static Task? StartMcpInBackgroundIfEnabled(string[] args, ToolkitServices services, string projectRootFullPath)
    {
        if (!HasFlag(args, "--mcp"))
        {
            Log.For(LogModule.Host).Info("MCP 服务未启用");
            return null;
        }

        Log.For(LogModule.Host).Info("MCP 服务已启用，准备启动...");
        Task task = Task.Run(() => McpHostRunner.RunAsync(args, services, projectRootFullPath));
        Log.For(LogModule.Host).Info("MCP 服务启动任务已提交");
        _ = task.ContinueWith(t =>
        {
            Exception? ex = t.Exception?.GetBaseException();
            if (ex != null)
            {
                Log.For(LogModule.Host).Exception(ex, "MCP 服务运行失败");
            }
            else
            {
                Log.For(LogModule.Host).Error("MCP 服务运行失败: 未知异常");
            }
            // MCP 是本进程的对外服务面（--mcp 显式启用）：它死了而进程装活，
            // 插件侧只会看到无限"启动中"且收不到任何状态推送。退出进程让插件走 onProcessExit 分支，
            // 把失败暴露为可见状态，而不是掩盖。
            Log.For(LogModule.Host).Error("MCP 服务已死亡，Host 进程退出");
            Environment.Exit(1);
        }, TaskContinuationOptions.OnlyOnFaulted);
        return task;
    }

    private static bool HasFlag(string[] args, string flag)
    {
        foreach (string arg in args)
        {
            if (string.Equals(arg, flag, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static void WriteResponse(WorkerResponse response)
    {
        string json = JsonSerializer.Serialize(response, JsonOptions);
        Console.WriteLine(json);
        Console.Out.Flush();
    }

    private static void ScheduleLintPush(ToolkitServices services)
    {
        lock (LintPushLock)
        {
            LintPushTimer?.Dispose();
            LintPushTimer = new Timer(static state =>
            {
                if (state is not ToolkitServices current)
                {
                    return;
                }
                PushLintDiagnostics(current);
            }, services, LintPushDebounceMs, Timeout.Infinite);
        }
    }

    private static void PushLintDiagnostics(ToolkitServices services)
    {
        try
        {
            // 短路前提（全局性证明，非子树假设）：
            // 1. Lint 输入全部来自内存模型实体——src/core/lint 无 FileIO 读取，
            //    ScriptAnalysisService.GetScriptSourceText 只读实体 SourceText（本调用不传 overrides）；
            // 2. RefreshVersion 只在全树 diff 非空时前进——版本不变 ⟺ 模型实体逐实例相同
            //    （无影响分支不重建任何实体）⟹ Lint = f(内存模型) 结果确定性相同。
            // 时间戳保留型内容篡改对整个管线（含模型本身）均不可见，本门控不改变该场景的可观测行为。
            if (services.Model.RefreshVersion == _lastLintPushVersion)
            {
                return;
            }
            LintDiagnostic[] deduplicated;
            services.Model.EnterReadLock();
            try
            {
                ScriptAnalysisService analysisService = new(services.Model);
                IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.AnalyzeProject(services.Model, analysisService);
                LintDiagnostic[] processed = diagnostics.Select(LintEngine.NormalizeDiagnostic).ToArray();
                deduplicated = LintEngine.DeduplicateDiagnostics(processed);
            }
            finally
            {
                services.Model.ExitReadLock();
            }
            string json = JsonSerializer.Serialize(deduplicated, JsonOptions);
            AsyncConsoleErrorWriter.WriteLine($"{LintDiagnosticsPrefix}{json}");

            // 保护性自愈：检测到清单↔实体不一致（越界直写污染）→ 交集强刷（在读锁外，允许写）
            if (deduplicated.Any(static d => d.RuleId == LintRuleIds.StructScriptOrphan))
                TryReconcileScriptGroups(services);

            _lastLintPushVersion = services.Model.RefreshVersion;
        }
        catch (Exception ex)
        {
            Log.For(LogModule.Host).Exception(ex, "推送 Lint 结果失败");
        }
    }

    /// <summary>
    /// 保护性自愈：检测到脚本清单与实体不一致时，以交集强刷把磁盘强制刷回一致。
    /// 软失败：连续异常达阈值后进入冷却期，避免反复失败抖动。
    /// </summary>
    private static void TryReconcileScriptGroups(ToolkitServices services)
    {
        long nowTicks = DateTime.UtcNow.Ticks;
        if (_reconcileDisabledUntilTicks != 0 && nowTicks < _reconcileDisabledUntilTicks)
        {
            Log.For(LogModule.Host).Warning("脚本组一致性自愈处于软降级冷却期，本轮跳过");
            return;
        }

        try
        {
            ScriptGroupReconcile.ReconcileAllDevices(services.Model);
            _consecutiveReconcileFailures = 0;
            _reconcileDisabledUntilTicks = 0;
        }
        catch (Exception ex)
        {
            _consecutiveReconcileFailures++;
            Log.For(LogModule.Host).Exception(ex, "脚本组一致性自愈失败");
            if (_consecutiveReconcileFailures >= ReconcileFailureThreshold)
            {
                _reconcileDisabledUntilTicks = DateTime.UtcNow.Ticks + ReconcileCooldownTicks;
                Log.For(LogModule.Host).Error($"脚本组一致性自愈连续失败 {_consecutiveReconcileFailures} 次，进入 60s 软降级冷却");
            }
        }
    }
}
