using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Canvas;

/// <summary>
/// 浏览器进程管理器 - 整个 Toolkit 共享唯一一个浏览器进程。
/// 提供自动重连、多浏览器回退、线程安全的 CDP 会话管理。
/// </summary>
/// <summary>
/// 浏览器 worker 抽象。BrowserRenderService 依赖它而非具体 BrowserProcessManager，
/// 便于注入测试 worker 验证重启重试逻辑（F3）。
/// </summary>
internal interface IBrowserWorker : IDisposable
{
    int? BrowserProcessId { get; }
    CdpSession? AcquireSession();
    bool Restart();
}

internal sealed class BrowserProcessManager : IBrowserWorker
{
    private sealed record BrowserCandidate(
        string Name,
        string CommandName,
        IReadOnlyList<string> KnownFullPaths);

    private sealed record BrowserBinding(
        string Name,
        string ExecutableFullPath);

    private static readonly IReadOnlyList<BrowserCandidate> BrowserFallbackQueue =
    [
        new BrowserCandidate(
            "chrome",
            "chrome.exe",
            [
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
            ]),
        new BrowserCandidate(
            "msedge",
            "msedge.exe",
            [
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
            ])
    ];

    private readonly Log _log = Log.For(LogModule.Core);
    private readonly object _lock = new();
    private readonly int _workerIndex;
    private readonly bool _disableExtensions;
    private readonly bool _useTemporaryUserDataDir;
    private int _existingBrowserProcessId;
    private readonly bool _keepBrowserProcess;
    private readonly string? _browserExecutableOverride;
    private readonly int _windowWidth;
    private readonly int _windowHeight;

    // 当前绑定的浏览器（可能为 null 表示尚未初始化或初始化失败）
    private BrowserBinding? _browserBinding;

    // 浏览器进程和 CDP 会话
    private Process? _browserProcess;
    private CdpSession? _cdpSession;
    private int _cdpPort;
    private string? _userDataDirFullPath;

    // 状态标记
    private bool _disposed;
    private bool _isInitializing;

    public BrowserProcessManager(
        int workerIndex,
        bool disableExtensions = true,
        bool useTemporaryUserDataDir = true,
        int existingBrowserProcessId = 0,
        bool keepBrowserProcess = false,
        string? browserExecutableOverride = null,
        int windowWidth = 1080,
        int windowHeight = 2160)
    {
        _workerIndex = workerIndex;
        _disableExtensions = disableExtensions;
        _useTemporaryUserDataDir = useTemporaryUserDataDir;
        _existingBrowserProcessId = existingBrowserProcessId;
        _keepBrowserProcess = keepBrowserProcess;
        _browserExecutableOverride = browserExecutableOverride;
        _windowWidth = windowWidth;
        _windowHeight = windowHeight;
    }

    /// <summary>
    /// 浏览器进程是否可用（已初始化且连接正常）。
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            lock (_lock)
            {
                return !_disposed && _cdpSession != null && _cdpSession.IsAlive && _browserProcess != null && !_browserProcess.HasExited;
            }
        }
    }

    /// <summary>
    /// 当前绑定的浏览器名称（可能为 null）。
    /// </summary>
    public string? BrowserName
    {
        get
        {
            lock (_lock)
            {
                return _browserBinding?.Name;
            }
        }
    }

    public int? BrowserProcessId
    {
        get
        {
            lock (_lock)
            {
                return _browserProcess?.Id;
            }
        }
    }

    /// <summary>
    /// 获取 CDP 会话实例。
    /// 如果当前无可用会话，自动尝试定位浏览器并重建进程。
    /// 返回 null 表示获取失败（无可用浏览器或初始化失败）。
    /// </summary>
    public CdpSession? AcquireSession()
    {
        lock (_lock)
        {
            ThrowIfDisposed();

            // 当前会话可用，直接返回
            if (_cdpSession != null && _browserProcess != null && !_browserProcess.HasExited)
            {
                return _cdpSession;
            }

            // 正在初始化中，等待完成
            if (_isInitializing)
            {
                _log.Info("浏览器进程正在初始化中，等待完成...");
                // 使用 Monitor.Wait 等待初始化完成信号
                Monitor.Wait(_lock, TimeSpan.FromSeconds(60));

                // 等待后重新检查状态
                if (_disposed)
                    throw new ObjectDisposedException(nameof(BrowserProcessManager));

                // 如果初始化成功，会话应该已经可用
                if (_cdpSession != null && _cdpSession.IsAlive && _browserProcess != null && !_browserProcess.HasExited)
                {
                    return _cdpSession;
                }

                // 初始化可能失败，需要重新初始化
                if (_isInitializing)
                {
                    // 仍在初始化中（理论上不应发生），返回null
                    return null;
                }
            }

            // 需要初始化
            _isInitializing = true;
        }

        try
        {
            // 在锁外执行初始化（避免长时间阻塞）
            InitializeInternal();

            lock (_lock)
            {
                _isInitializing = false;
                Monitor.PulseAll(_lock);  // 通知所有等待的线程初始化完成
                return _cdpSession;
            }
        }
        catch
        {
            lock (_lock)
            {
                _isInitializing = false;
                CleanupInternal(false);
                Monitor.PulseAll(_lock);  // 通知等待的线程初始化失败
            }
            throw;
        }
    }

    /// <summary>
    /// 强制重新初始化浏览器进程（用于故障恢复）。
    /// </summary>
    public bool Restart()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            _existingBrowserProcessId = 0;
            CleanupInternal(false);
        }

        CdpSession? session = AcquireSession();
        return session != null;
    }

    /// <summary>
    /// 释放浏览器进程和 CDP 会话。
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            CleanupInternal(_keepBrowserProcess);
            _log.Info("浏览器进程管理器已释放");
        }
    }

    private void InitializeInternal()
    {
        if (_existingBrowserProcessId > 0)
        {
            Process? existingProcess;
            try
            {
                existingProcess = Process.GetProcessById(_existingBrowserProcessId);
            }
            catch (ArgumentException)
            {
                // 目标进程已不存在（可能被外部清理），降级为启动新浏览器
                _log.Info($"浏览器 worker {_workerIndex} 进程 {_existingBrowserProcessId} 已不存在，将启动新实例");
                existingProcess = null;
                _existingBrowserProcessId = 0;
            }

            if (existingProcess != null)
            {
                _browserProcess = existingProcess;
                string preservedUserDataDirFullPath = GetBrowserUserDataDirFullPath(isPreserved: true, workerOrSessionName: $"worker-{_workerIndex}");
                _cdpPort = int.Parse(File.ReadAllText(Path.Combine(preservedUserDataDirFullPath, "cdp-port.txt")));
                _userDataDirFullPath = preservedUserDataDirFullPath;
                using CancellationTokenSource existingBrowserCts = new(TimeSpan.FromSeconds(30));
                string existingBrowserPageWebSocketUrl = WaitForPageWebSocketUrlAsync(
                    existingProcess,
                    _cdpPort,
                    existingBrowserCts.Token,
                    () => $"浏览器进程已退出: {existingProcess.Id}，退出码: {existingProcess.ExitCode}").GetAwaiter().GetResult();
                ClientWebSocket existingWs = ConnectCdpClient(new Uri(existingBrowserPageWebSocketUrl), existingBrowserCts.Token);
                CdpSession existingBrowserSession = new(existingWs);
                existingBrowserSession.SendCommandAsync("Page.enable", null, existingBrowserCts.Token).GetAwaiter().GetResult();
                existingBrowserSession.SendCommandAsync("Runtime.enable", null, existingBrowserCts.Token).GetAwaiter().GetResult();
                _cdpSession = existingBrowserSession;
                _log.Info($"已接管浏览器 worker {_workerIndex} 进程: {_existingBrowserProcessId} (CDP端口: {_cdpPort})");
                return;
            }
        }

        if (_browserBinding != null)
        {
            StartBoundBrowser(_browserBinding);
            return;
        }

        if (_browserExecutableOverride != null)
        {
            string overrideFullPath = NormalizeFullPath(_browserExecutableOverride);
            if (!File.Exists(overrideFullPath))
                throw new InvalidOperationException("浏览器覆盖路径无效");

            string overrideFileName = Path.GetFileName(overrideFullPath);
            BrowserBinding overrideBinding = overrideFileName switch
            {
                "chrome.exe" => new BrowserBinding("chrome", overrideFullPath),
                "msedge.exe" => new BrowserBinding("msedge", overrideFullPath),
                _ => throw new InvalidOperationException("浏览器覆盖路径无效")
            };
            _browserBinding = overrideBinding;
            _log.Info($"已绑定浏览器: {overrideBinding.Name}");
            StartBoundBrowser(overrideBinding);
            return;
        }

        // 外部浏览器属于系统边界，文件存在不等于真正可用。
        // 初始化时必须按固定队列逐个探测实际启动结果，首个能建立 CDP 的浏览器就是唯一绑定结果；
        // 一旦绑定成功，后续重启只复用同一个浏览器，不再重新猜测。
        Exception? lastError = null;
        HashSet<string> seen = new(StringComparer.Ordinal);

        bool tryBindAndStart(string name, string fullPath)
        {
            if (!seen.Add(fullPath)) return false;
            BrowserBinding candidateBinding = new(name, fullPath);
            _browserBinding = candidateBinding;
            _log.Info($"已绑定浏览器: {candidateBinding.Name}");
            try { StartBoundBrowser(candidateBinding); return true; }
            catch (Exception ex)
            {
                lastError = ex;
                _log.Warning($"浏览器启动失败，继续探测下一个候选: {candidateBinding.Name}, {ex.Message}");
                CleanupInternal(false);
                _browserBinding = null;
                return false;
            }
        }

        foreach (BrowserCandidate candidate in BrowserFallbackQueue)
        {
            foreach (string rawPath in candidate.KnownFullPaths)
            {
                string fullPath = NormalizeFullPath(rawPath);
                if (File.Exists(fullPath) && tryBindAndStart(candidate.Name, fullPath))
                    return;
            }

            string? pathExe = FindInPath(candidate.CommandName);
            if (pathExe != null && tryBindAndStart(candidate.Name, pathExe))
                return;
        }

        throw new InvalidOperationException("未找到可用浏览器", lastError);
    }

    /// <summary>
    /// 创建并连接 CDP 客户端 WebSocket，将底层连接故障包装为 CdpConnectionClosedException。
    /// 连接是 ClientWebSocket 的特有职责（WebSocket 基类无 ConnectAsync），故独立于 CdpSession：
    /// 连接成功后交给 CdpSession（依赖可注入的 WebSocket 基类）负责命令收发。
    /// </summary>
    private static ClientWebSocket ConnectCdpClient(Uri pageWebSocketUrl, CancellationToken cancellationToken)
    {
        ClientWebSocket ws = new ClientWebSocket();
        try
        {
            ws.ConnectAsync(pageWebSocketUrl, cancellationToken).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (CdpSession.IsConnectionClosed(ex))
        {
            throw new CdpConnectionClosedException(ex);
        }
        return ws;
    }

    private void StartBoundBrowser(BrowserBinding browserBinding)
    {
        // 启动浏览器进程
        TcpListener portListener = new(IPAddress.Loopback, 0);
        portListener.Start();
        try { _cdpPort = ((IPEndPoint)portListener.LocalEndpoint).Port; }
        finally { portListener.Stop(); }
        StringBuilder argsBuilder = new();
        argsBuilder.Append("--headless=new ");
        // 新版 --headless=new 仍会创建一个本应隐藏的平台窗口，部分 Chrome/Edge 版本会让它泄露为左上角可见且无法交互的窗口。
        // 用负坐标把窗口推到可视区之外消除副作用；窗口尺寸取渲染设置尺寸，与渲染度量保持同量级。
        argsBuilder.Append("--window-position=-2400,-2400 ");
        argsBuilder.Append($"--window-size={_windowWidth},{_windowHeight} ");
        argsBuilder.Append("--no-sandbox ");
        argsBuilder.Append("--noerrdialogs ");
        argsBuilder.Append("--disable-gpu ");
        argsBuilder.Append("--disable-software-rasterizer ");
        argsBuilder.Append("--disable-dev-shm-usage ");
        if (_disableExtensions)
        {
            argsBuilder.Append("--disable-extensions ");
        }
        argsBuilder.Append("--disable-background-networking ");
        argsBuilder.Append("--disable-breakpad ");
        argsBuilder.Append("--disable-crashpad-for-testing ");
        argsBuilder.Append("--disable-crash-reporter ");
        argsBuilder.Append("--disable-logging ");
        argsBuilder.Append("--disable-features=VizDisplayCompositor ");
        argsBuilder.Append("--allow-file-access-from-files ");
        argsBuilder.Append("--hide-scrollbars ");
        string userDataDirFullPath;
        if (_keepBrowserProcess)
        {
            userDataDirFullPath = GetBrowserUserDataDirFullPath(isPreserved: true, workerOrSessionName: $"worker-{_workerIndex}");
            // 清理上次启动失败残留的配置目录，避免 Chrome 因锁定文件或损坏状态退出（退出码 21）
            if (Directory.Exists(userDataDirFullPath))
            {
                try { Directory.Delete(userDataDirFullPath, recursive: true); }
                catch (Exception ex)
                {
                    _log.Warning($"清理残留浏览器配置目录失败，使用新目录: {ex.Message}");
                    userDataDirFullPath = GetBrowserUserDataDirFullPath(isPreserved: true,
                        workerOrSessionName: $"worker-{_workerIndex}-{Guid.NewGuid():N}");
                }
            }
        }
        else
            userDataDirFullPath = GetBrowserUserDataDirFullPath(isPreserved: false, workerOrSessionName: Guid.NewGuid().ToString("N"));
        if (_useTemporaryUserDataDir)
        {
            Directory.CreateDirectory(userDataDirFullPath);
            _userDataDirFullPath = userDataDirFullPath;
            argsBuilder.Append($"--user-data-dir=\"{userDataDirFullPath}\" ");
        }
        argsBuilder.Append($"--remote-debugging-port={_cdpPort} ");
        argsBuilder.Append("about:blank");
        string args = argsBuilder.ToString();
        Process browserProcess = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = browserBinding.ExecutableFullPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };

        _log.Info($"启动浏览器进程: {browserBinding.Name} (CDP端口: {_cdpPort}, 禁插件={_disableExtensions}, 临时配置目录={_useTemporaryUserDataDir})");
        object startupOutputLock = new();
        List<string> startupStdoutLines = [];
        List<string> startupStderrLines = [];
        void rememberLine(List<string> lines, string? data)
        {
            if (data is null) return;

            lock (startupOutputLock)
            {
                if (lines.Count == 12)
                    lines.RemoveAt(0);
                lines.Add(data);
            }
        }
        string buildExitMessage()
        {
            string message = $"浏览器进程已退出: {browserProcess.Id}，退出码: {browserProcess.ExitCode}";
            lock (startupOutputLock)
            {
                if (startupStderrLines.Count > 0)
                    message += $"\nstderr:\n{string.Join("\n", startupStderrLines)}";
                if (startupStdoutLines.Count > 0)
                    message += $"\nstdout:\n{string.Join("\n", startupStdoutLines)}";
            }
            return message;
        }
        _browserProcess = browserProcess;

        // 收集 stderr：仅放行进程级故障日志（白名单），其余 Chromium 内部噪音全部静默
        browserProcess.OutputDataReceived += (_, e) => rememberLine(startupStdoutLines, e.Data);
        browserProcess.ErrorDataReceived += (_, e) =>
        {
            rememberLine(startupStderrLines, e.Data);
            if (e.Data is null)
                return;
            foreach (string pattern in ForwardablePatterns)
            {
                if (e.Data.Contains(pattern, StringComparison.Ordinal))
                {
                    _log.Warning($"浏览器: {e.Data}");
                    return;
                }
            }
        };

        browserProcess.Start();
        browserProcess.BeginOutputReadLine();
        browserProcess.BeginErrorReadLine();

        // 连接 CDP（首个 page，即浏览器启动时自动创建的 about:blank）
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
        string pageWebSocketUrl = WaitForPageWebSocketUrlAsync(browserProcess, _cdpPort, cts.Token, buildExitMessage).GetAwaiter().GetResult();

        ClientWebSocket ws = ConnectCdpClient(new Uri(pageWebSocketUrl), cts.Token);
        CdpSession session = new(ws);

        // 初始化 CDP 域
        session.SendCommandAsync("Page.enable", null, cts.Token).GetAwaiter().GetResult();
        session.SendCommandAsync("Runtime.enable", null, cts.Token).GetAwaiter().GetResult();
        _cdpSession = session;

        _log.Info("浏览器 CDP 连接已建立");
        if (_keepBrowserProcess)
        {
            Directory.CreateDirectory(userDataDirFullPath);
            _existingBrowserProcessId = browserProcess.Id;
            File.WriteAllText(Path.Combine(userDataDirFullPath, "cdp-port.txt"), _cdpPort.ToString());
        }
    }

    /// <summary>
    /// 白名单：仅放行表明进程级故障的 stderr 行。
    /// headless 渲染浏览器 99%+ 的 stderr 输出是 Chromium 内部噪音（GCM/Mojo/TFLite/crashpad 等），
    /// 采用白名单可自动免疫版本升级带来的新噪音源。
    /// </summary>
    private static readonly string[] ForwardablePatterns =
    [
        "FATAL",                    // 致命断言失败
        "CHECK failed",             // 运行时检查失败
        "Assertion failure",        // 断言失败
        "Out of memory",            // OOM
        "GPU process exited",       // GPU 进程异常退出（影响渲染结果）
        "GPU process crashed",      // GPU 进程崩溃
        "Renderer process exited",  // 渲染进程异常退出
        "Renderer process crashed", // 渲染进程崩溃
        "Segmentation fault",       // 段错误
        "Stack trace:",             // 崩溃堆栈
    ];

    private void CleanupInternal(bool keepBrowserProcess)
    {
        // 先杀浏览器进程，使 WebSocket 连接自然断开，
        // 避免 CdpSession.Dispose 在 CloseAsync 握手上永久阻塞
        try
        {
            if (!keepBrowserProcess && _browserProcess != null && !_browserProcess.HasExited)
            {
                _browserProcess.Kill(entireProcessTree: true);
                _browserProcess.WaitForExit();
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"浏览器进程清理失败: {ex.Message}");
        }
        finally
        {
            _browserProcess?.Dispose();
            _browserProcess = null;
        }

        _cdpSession?.Dispose();
        _cdpSession = null;

        if (!keepBrowserProcess && _userDataDirFullPath is not null)
        {
            try
            {
                if (Directory.Exists(_userDataDirFullPath))
                    Directory.Delete(_userDataDirFullPath, recursive: true);
            }
            catch (Exception ex)
            {
                _log.Warning($"浏览器用户目录清理失败: {ex.Message}");
            }
        }

        _userDataDirFullPath = null;
    }

    private static string? FindInPath(string commandName)
    {
        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        if (pathValue is null)
            return null;

        string[] dirs = pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string dir in dirs)
        {
            string full = NormalizeFullPath(Path.Combine(dir, commandName));
            if (File.Exists(full))
                return full;
        }
        return null;
    }

#pragma warning disable TK006 // 路径规范化：统一分隔符，与工程路径体系对齐
    private static string NormalizeFullPath(string path)
    {
        string full = Path.GetFullPath(path);
        return full.Replace('\\', '/');
    }
#pragma warning restore TK006

    private static string GetBrowserUserDataDirFullPath(bool isPreserved, string workerOrSessionName)
    {
        if (isPreserved)
            return Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Toolkit.Browser.Preserved", workerOrSessionName));

        return Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Toolkit.Browser", workerOrSessionName));
    }

    private static async Task<string> WaitForPageWebSocketUrlAsync(
        Process browserProcess,
        int cdpPort,
        CancellationToken cancellationToken,
        Func<string> buildProcessExitMessage)
    {
        using HttpClient client = new HttpClient();
        string listUrl = $"http://127.0.0.1:{cdpPort}/json/list";

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (browserProcess.HasExited)
            {
                browserProcess.WaitForExit();
                throw new InvalidOperationException(buildProcessExitMessage());
            }
            try
            {
                string json = await client.GetStringAsync(listUrl, cancellationToken);
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in doc.RootElement.EnumerateArray())
                    {
                        if (!item.TryGetProperty("type", out JsonElement typeElem))
                            continue;
                        if (!string.Equals(typeElem.GetString(), "page", StringComparison.Ordinal))
                            continue;
                        if (!item.TryGetProperty("webSocketDebuggerUrl", out JsonElement wsElem))
                            continue;
                        string? ws = wsElem.GetString();
                        if (ws != null)
                            return ws;
                    }
                }
            }
            catch (HttpRequestException)
            {
                // CDP 端口未就绪时重试
            }

            await Task.Delay(120, cancellationToken);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(BrowserProcessManager));
        }
    }
}

/// <summary>
/// CDP 底层连接已断（WebSocket/IO/Socket 故障或对端发送 Close 帧）。
/// 继承 InvalidOperationException 保留"操作无效"语义归属；上层用 catch (CdpConnectionClosedException) 精确识别并触发 worker 重启。
/// 业务级异常（CDP 命令失败、CDP JS 异常）不是本类型，不触发重启。
/// </summary>
internal sealed class CdpConnectionClosedException : InvalidOperationException
{
    public CdpConnectionClosedException() : base("CDP 连接已关闭") { }
    public CdpConnectionClosedException(Exception inner) : base("CDP 连接已关闭", inner) { }
}

/// <summary>
/// CDP (Chrome DevTools Protocol) 会话封装。
/// </summary>
internal sealed class CdpSession : IDisposable
{
    private readonly WebSocket _ws;
    private int _nextId = 1;

    /// <summary>
    /// 接受任意 WebSocket 实例：生产环境由 BrowserProcessManager.ConnectCdpClient 创建并连接 ClientWebSocket，
    /// 测试可注入 stub 子类以验证 SendAsync/ReceiveAsync 的连接级异常包装。
    /// 连接是 ClientWebSocket 的特有职责，故放在调用方，使本类只依赖可注入的 WebSocket 基类。
    /// </summary>
    public CdpSession(WebSocket ws)
    {
        ArgumentNullException.ThrowIfNull(ws);
        _ws = ws;
    }

    /// <summary>
    /// 底层 WebSocket 是否处于可收发状态。供上层在复用 session 前主动判定健康度。
    /// </summary>
    public bool IsAlive => _ws.State == WebSocketState.Open;

    /// <summary>
    /// 判定是否为连接级异常（WebSocket/IO/Socket 故障）。
    /// SendAsync/ReceiveAsync 包装与 BrowserProcessManager.ConnectCdpClient 三处复用，统一抛 CdpConnectionClosedException。
    /// </summary>
    internal static bool IsConnectionClosed(Exception ex) =>
        ex is IOException or SocketException or WebSocketException
        || ex is OperationCanceledException { InnerException: IOException or SocketException };

    public async Task<JsonElement> SendCommandAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        int id = _nextId++;
        using (MemoryStream ms = new MemoryStream())
        {
            using (Utf8JsonWriter writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", id);
                writer.WriteString("method", method);
                if (parameters != null)
                {
                    writer.WritePropertyName("params");
                    JsonSerializer.Serialize(writer, parameters);
                }
                writer.WriteEndObject();
            }

            try
            {
                await _ws.SendAsync((ReadOnlyMemory<byte>)ms.ToArray(), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
            }
            catch (Exception ex) when (IsConnectionClosed(ex))
            {
                throw new CdpConnectionClosedException(ex);
            }
        }

        while (true)
        {
            using JsonDocument message = await ReceiveMessageAsync(cancellationToken);
            JsonElement root = message.RootElement;

            if (!root.TryGetProperty("id", out JsonElement responseId))
                continue;
            if (responseId.GetInt32() != id)
                continue;

            if (root.TryGetProperty("error", out JsonElement error))
            {
                throw new InvalidOperationException($"CDP 命令失败: {method} {error}");
            }

            if (root.TryGetProperty("result", out JsonElement result))
            {
                return result.Clone();
            }

            throw new InvalidOperationException($"CDP 响应缺少 result 或 error 字段: {method}");
        }
    }

    private async Task<JsonDocument> ReceiveMessageAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[64 * 1024];
        using MemoryStream ms = new MemoryStream();

        while (true)
        {
            ValueWebSocketReceiveResult result;
            try
            {
                result = await _ws.ReceiveAsync((Memory<byte>)buffer, cancellationToken);
            }
            catch (Exception ex) when (IsConnectionClosed(ex))
            {
                throw new CdpConnectionClosedException(ex);
            }
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new CdpConnectionClosedException();
            }

            if (result.Count > 0)
            {
                ms.Write(buffer, 0, result.Count);
            }

            if (result.EndOfMessage)
                break;
        }

        string json = Encoding.UTF8.GetString(ms.ToArray());
        return JsonDocument.Parse(json);
    }

    public void Dispose()
    {
        try
        {
            if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
            {
                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            Log.For(LogModule.Core).Warning($"WebSocket 关闭失败: {ex.Message}");
        }

        _ws.Dispose();
    }
}
