using System.Collections.Concurrent;
using System.Text.Json;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Canvas;

/// <summary>
/// 浏览器渲染服务 — 多子进程负载均衡。
/// 调用方只提交任务（HTML + 尺寸），不关心进程/session 细节。
/// 每个子进程独立运行一个 headless 浏览器实例，各自一个 page，互不干扰。
/// </summary>
public sealed class BrowserRenderService : IDisposable
{
    private readonly int _workerCount;
    private readonly bool _disableExtensions;
    private readonly bool _useTemporaryUserDataDir;
    private readonly IReadOnlyList<int> _existingBrowserProcessIds;
    private readonly bool _keepBrowserProcesses;
    private readonly string? _browserExecutableFullPath;
    private readonly int _windowWidth;
    private readonly int _windowHeight;

    private readonly ConcurrentQueue<IBrowserWorker> _available = new();
    private readonly SemaphoreSlim _semaphore = new(0);
    private readonly IBrowserWorker?[] _workers;
    private readonly Log _log = Log.For(LogModule.Core);
    private bool _disposed;
    private bool _initialized;
    private bool _disabledByEnv;

    /// <summary>
    /// 滥用防护环境变量：headless 浏览器默认拒绝启动，正式入口显式设 1 才允许。
    /// 未设置时任何路径（含手工启动 Host/测试脚本）都无法拉起浏览器进程，防止污染系统。
    /// 正式允许入口：vscode_plugin 启动 Host 时传 env、RunTests.Main.ps1 测试入口。
    /// </summary>
    public const string BrowserEnabledEnvVar = "TOOLKIT_BROWSER_ENABLED";

    public static bool IsBrowserLaunchAllowed =>
        Environment.GetEnvironmentVariable(BrowserEnabledEnvVar) is "1" or "true";

    public BrowserRenderService(
        int workerCount,
        IReadOnlyList<int> existingBrowserProcessIds,
        bool disableExtensions = true,
        bool useTemporaryUserDataDir = true,
        bool keepBrowserProcesses = false,
        string? browserExecutableFullPath = null,
        int windowWidth = 1080,
        int windowHeight = 2160)
    {
        if (workerCount < 1 || workerCount > 8)
            throw new ArgumentOutOfRangeException(nameof(workerCount), "workerCount 必须在 1-8 之间");

        _workerCount = workerCount;
        _disableExtensions = disableExtensions;
        _useTemporaryUserDataDir = useTemporaryUserDataDir;
        _existingBrowserProcessIds = existingBrowserProcessIds;
        _keepBrowserProcesses = keepBrowserProcesses;
        _browserExecutableFullPath = browserExecutableFullPath;
        _windowWidth = windowWidth;
        _windowHeight = windowHeight;
        _workers = new IBrowserWorker[_workerCount];
    }

    /// <summary>
    /// 并行度（供调用方确定 Parallel.ForEach 的 MaxDegreeOfParallelism）。
    /// </summary>
    public int Parallelism => _workerCount;

    public IReadOnlyList<int> BrowserProcessIds
    {
        get
        {
            List<int> processIds = new(_workers.Length);
            foreach (IBrowserWorker? worker in _workers)
            {
                if (worker != null && worker.BrowserProcessId is int processId)
                    processIds.Add(processId);
            }
            return processIds;
        }
    }

    /// <summary>
    /// 启动所有 worker 进程。任一启动失败则整体失败。
    /// </summary>
    public void Initialize()
    {
        if (!IsBrowserLaunchAllowed)
        {
            // 滥用防护：默认拒绝启动无头浏览器；正式入口（插件 Host/测试套件）显式设置环境变量
            _disabledByEnv = true;
            _log.Warning($"无头浏览器渲染被环境变量禁用：{BrowserEnabledEnvVar}=1 才允许启动");
            return;
        }
        CanvasRuntimeResourceValidator.Validate();
        _log.Info($"浏览器渲染服务: 启动 {_workerCount} 个 worker (禁插件={_disableExtensions}, 临时配置目录={_useTemporaryUserDataDir})...");

        for (int i = 0; i < _workerCount; i++)
        {
            int existingBrowserProcessId = 0;
            if (_keepBrowserProcesses && i < _existingBrowserProcessIds.Count)
                existingBrowserProcessId = _existingBrowserProcessIds[i];
            IBrowserWorker worker = new BrowserProcessManager(i, _disableExtensions, _useTemporaryUserDataDir, existingBrowserProcessId, _keepBrowserProcesses, _browserExecutableFullPath, _windowWidth, _windowHeight);
            if (worker.AcquireSession() is null)
                throw new InvalidOperationException($"浏览器 worker {i} 启动失败");

            _workers[i] = worker;
            _available.Enqueue(worker);
        }

        _semaphore.Release(_workerCount);
        _initialized = true;
        _log.Info($"浏览器渲染服务: {_workerCount} 个 worker 已就绪");
    }

    /// <summary>
    /// 操作一：校验 HTML 渲染是否成功。失败抛异常。
    /// </summary>
    public void ValidateHtmlRender(string htmlContent, int width, int height)
    {
        IBrowserWorker worker = Acquire();
        try
        {
            ValidateHtmlRenderCore(worker, htmlContent, width, height);
        }
        finally
        {
            Release(worker);
        }
    }

    /// <summary>
    /// 渲染校验核心循环（独立于池管理）：首次失败若是连接级故障，重启 worker 重试一次。
    /// 抽成 internal 便于注入测试 worker 验证重启重试逻辑（F3）。
    /// </summary>
    internal void ValidateHtmlRenderCore(IBrowserWorker worker, string htmlContent, int width, int height)
    {
        for (bool retried = false; ; retried = true)
        {
            try
            {
                CdpSession cdp = RequireSession(worker);
                using CancellationTokenSource cts = new(TimeSpan.FromSeconds(25));
                PrepareAndValidateRender(cdp, htmlContent, width, height, cts.Token);
                return;
            }
            catch (CdpConnectionClosedException ex) when (!retried)
            {
                _log.Warning($"浏览器 worker 渲染校验中断，重启后重试一次: {ex.Message}");
                if (!worker.Restart())
                    throw new InvalidOperationException("浏览器 worker 重启失败", ex);
            }
            catch (OperationCanceledException ex) when (!retried)
            {
                _log.Warning($"浏览器 worker 渲染校验超时，重启后重试一次: {ex.Message}");
                if (!worker.Restart())
                    throw new InvalidOperationException("浏览器 worker 重启失败（渲染校验超时）", ex);
            }
        }
    }

    /// <summary>
    /// 操作二：渲染 HTML 为 PNG 字节数组。校验 + 截图。失败抛异常。
    /// </summary>
    public byte[] RenderHtmlToPng(string htmlContent, int width, int height)
    {
        IBrowserWorker worker = Acquire();
        try
        {
            for (bool retried = false; ; retried = true)
            {
                try
                {
                    CdpSession cdp = RequireSession(worker);
                    using CancellationTokenSource cts = new(TimeSpan.FromSeconds(25));
                    PrepareAndValidateRender(cdp, htmlContent, width, height, cts.Token);
                    string base64 = CaptureScreenshot(cdp, width, height, cts.Token);
                    return Convert.FromBase64String(base64);
                }
                catch (CdpConnectionClosedException ex) when (!retried)
                {
                    _log.Warning($"浏览器 worker 渲染中断，重启后重试一次: {ex.Message}");
                    if (!worker.Restart())
                        throw new InvalidOperationException("浏览器 worker 重启失败", ex);
                }
                catch (OperationCanceledException ex) when (!retried)
                {
                    _log.Warning($"浏览器 worker 渲染超时，重启后重试一次: {ex.Message}");
                    if (!worker.Restart())
                        throw new InvalidOperationException("浏览器 worker 重启失败（渲染超时）", ex);
                }
            }
        }
        finally
        {
            Release(worker);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (BrowserProcessManager? worker in _workers)
            worker?.Dispose();

        _semaphore.Dispose();
        _log.Info("浏览器渲染服务已释放");
    }

    // ─── 池管理 ───

    private IBrowserWorker Acquire()
    {
        if (_disabledByEnv)
            throw new InvalidOperationException($"浏览器渲染被环境变量禁用（{BrowserEnabledEnvVar}=1 才允许启动）");
        if (!_initialized)
            throw new InvalidOperationException("浏览器渲染服务尚未初始化");

        // worker 全忙是正常回压，不是错误。
        // 真正的错误应由初始化/渲染链自己抛出，不能在这里凭空捏造"超时"把根因冲掉。
        _semaphore.Wait();

        if (!_available.TryDequeue(out IBrowserWorker? worker))
            throw new InvalidOperationException("semaphore 通过但队列为空");

        return worker;
    }

#pragma warning disable TK006 // 资源池回收，与 Acquire 对称
    private void Release(IBrowserWorker worker)
    {
        _available.Enqueue(worker);
        _semaphore.Release();
    }
#pragma warning restore TK006

    private static CdpSession RequireSession(IBrowserWorker worker)
    {
        CdpSession? cdp = worker.AcquireSession();
        if (cdp != null) return cdp;

        // 尝试重启
        if (!worker.Restart())
            throw new InvalidOperationException("浏览器 worker 不可用且无法重启");

        cdp = worker.AcquireSession();
        if (cdp == null)
            throw new InvalidOperationException("浏览器 worker 重启后仍无法获取会话");

        return cdp;
    }

    // ─── CDP 渲染协议 ───

    private static void PrepareAndValidateRender(
        CdpSession cdp, string htmlContent, int width, int height, CancellationToken cancellationToken)
    {
        // 1. 设置视口
        cdp.SendCommandAsync("Emulation.setDeviceMetricsOverride", new
        {
            width = Math.Max(1, width),
            height = Math.Max(1, height),
            deviceScaleFactor = 1,
            mobile = false
        }, cancellationToken).GetAwaiter().GetResult();

        // 2. 导航到 about:blank 重置页面
        cdp.SendCommandAsync("Page.navigate", new { url = "about:blank" }, cancellationToken).GetAwaiter().GetResult();
        EvaluateAndAwait(cdp, "new Promise(function(r){if(document.readyState==='complete')r();else window.addEventListener('load',r,{once:true})})", cancellationToken);

        // 3. 注入 HTML
        JsonElement frameTreeResponse = cdp.SendCommandAsync("Page.getFrameTree", null, cancellationToken).GetAwaiter().GetResult();
        string frameId = frameTreeResponse.GetProperty("frameTree").GetProperty("frame").GetProperty("id").GetString()
            ?? throw new InvalidOperationException("无法获取 frame id");

        cdp.SendCommandAsync("Page.setDocumentContent", new
        {
            frameId,
            html = htmlContent
        }, cancellationToken).GetAwaiter().GetResult();

        // 4. 等待渲染完成。失败时才读取引擎的按需快照，正常路径不序列化 DOM。
        try
        {
            EvaluateAndAwait(cdp, "window.CanvasLayoutEngine.initialRender", cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            InvalidOperationException failure = new($"{ex.Message}（诊断位于异常 Data: CanvasInputHtml, CanvasRuntimeSnapshot）", ex);
            failure.Data["CanvasInputHtml"] = htmlContent;
            failure.Data["CanvasRuntimeSnapshot"] = GetRenderDiagnostics(cdp, cancellationToken);
            throw failure;
        }

        // 5. 断言渲染状态
        AssertRenderComplete(cdp, cancellationToken);

        // 6. 等浏览器完成绘制
        EvaluateAndAwait(cdp, "new Promise(function(r){requestAnimationFrame(function(){requestAnimationFrame(r)})})", cancellationToken);
    }

    private static string CaptureScreenshot(CdpSession cdp, int clipWidth, int clipHeight, CancellationToken cancellationToken)
    {
        object screenshotParams = new { format = "png", fromSurface = true };
        if (clipWidth > 0 && clipHeight > 0)
        {
            screenshotParams = new { format = "png", fromSurface = true, clip = new { x = 0, y = 0, width = clipWidth, height = clipHeight, scale = 1 } };
        }

        JsonElement shot = cdp.SendCommandAsync("Page.captureScreenshot", screenshotParams, cancellationToken).GetAwaiter().GetResult();

        if (!shot.TryGetProperty("data", out JsonElement dataElem))
            throw new InvalidOperationException("CDP 响应缺少 screenshot.data");

        string? base64 = dataElem.GetString();
        if (base64 is null)
            throw new InvalidOperationException("CDP 返回空截图");

        return base64;
    }

    // ─── CDP 辅助 ───

    private static void EvaluateAndAwait(CdpSession cdp, string expression, CancellationToken cancellationToken)
    {
        JsonElement response = cdp.SendCommandAsync("Runtime.evaluate", new
        {
            expression,
            returnByValue = true,
            awaitPromise = true
        }, cancellationToken).GetAwaiter().GetResult();

        ThrowIfEvaluationException(response, expression);
    }

    private static void AssertRenderComplete(CdpSession cdp, CancellationToken cancellationToken)
    {
        JsonElement result = cdp.SendCommandAsync("Runtime.evaluate", new
        {
            expression = "(function(){var s=CanvasLayoutEngine.getRenderStatus();if(s.status!=='complete'||s.pending!==0||s.hasError)throw new Error('渲染未完成: status='+s.status+', pending='+s.pending+', hasError='+s.hasError);return'complete'})()",
            returnByValue = true,
            awaitPromise = false
        }, cancellationToken).GetAwaiter().GetResult();

        ThrowIfEvaluationException(result, "getRenderStatus");

        if (!result.TryGetProperty("result", out JsonElement resultProp)
            || !resultProp.TryGetProperty("value", out JsonElement value)
            || value.GetString() != "complete")
        {
            throw new InvalidOperationException("渲染未完成");
        }
    }

    private static string GetRenderDiagnostics(CdpSession cdp, CancellationToken cancellationToken)
    {
        JsonElement response = cdp.SendCommandAsync("Runtime.evaluate", new
        {
            expression = "JSON.stringify(window.CanvasLayoutEngine.getRenderDiagnostics())",
            returnByValue = true,
            awaitPromise = false
        }, cancellationToken).GetAwaiter().GetResult();

        return response.GetProperty("result").GetProperty("value").GetString()
            ?? throw new InvalidOperationException("Canvas 诊断快照为空");
    }

    private static void ThrowIfEvaluationException(JsonElement response, string context)
    {
        if (!response.TryGetProperty("exceptionDetails", out JsonElement exceptionDetails))
            return;

        string message = context;
        if (exceptionDetails.TryGetProperty("exception", out JsonElement exception)
            && exception.TryGetProperty("description", out JsonElement desc)
            && desc.ValueKind == JsonValueKind.String
            && desc.GetString() is { } descText)
        {
            message = descText;
        }
        else if (exceptionDetails.TryGetProperty("text", out JsonElement text)
            && text.ValueKind == JsonValueKind.String
            && text.GetString() is { } textBody)
        {
            message = textBody;
        }

        throw new InvalidOperationException($"CDP JS 异常 [{context}]: {message}");
    }
}
