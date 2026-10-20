using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Toolkit.Core.Infra;

/// <summary>
/// ComfyUI 引擎进程管理器 - 持有唯一 creatai-assets 引擎进程（Host 调度执行面）。
///
/// 生命周期 = Host 生命周期：Host 启动时拉起（随机端口 + 健康探测），Host 退出时杀树。
/// 调度职责归 ResourceGenScheduler；本管理器只负责进程/端口/传输，不持任务状态。
/// </summary>
public sealed class ComfyUIEngineProcessManager : IDisposable
{
    private readonly Log _log = Log.For(LogModule.Core);
    private readonly string _pythonFullPath;
    private readonly string _engineModule;
    private readonly int _healthCheckTimeoutMs;
    private Process? _process;
    private HttpClient? _httpClient;
    private int _port;
    private bool _disposed;
    private readonly object _lifecycleLock = new();

    public ComfyUIEngineProcessManager(string pythonFullPath, string engineModule = "creatai_assets.server", int healthCheckTimeoutMs = 30_000)
    {
        // pythonFullPath 由壳层启动参数注入（--engine-python），壳层保证非空
        _pythonFullPath = pythonFullPath;
        _engineModule = engineModule;
        _healthCheckTimeoutMs = healthCheckTimeoutMs;
    }

    public int Port => _port;

    public string BaseUrl => $"http://127.0.0.1:{_port}";

    /// <summary>拉起引擎进程并等待健康（/rpc 回环探测）。失败抛异常，不静默。</summary>
    public void Start(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ComfyUIEngineProcessManager));
            if (_process is { HasExited: false })
                return;

            int port;
            using (TcpListener listener = new(IPAddress.Loopback, 0))
            {
                listener.Start();
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _pythonFullPath,
                    Arguments = $"-m {_engineModule} --port {port}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };
            if (!process.Start())
                throw new InvalidOperationException("引擎进程启动失败（Process.Start 返回 false）");
            _process = process;
            _port = port;
            _httpClient?.Dispose();
            _httpClient = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(30) };

            _log.Info($"引擎进程已启动: pid={process.Id}, 端口={port}");
            WaitHealthy(cancellationToken);
        }
    }

    /// <summary>健康探测：/rpc 回环（未知方法错误 = 服务响应正常）。</summary>
    public bool IsHealthy()
    {
        if (_httpClient == null)
            return false;
        try
        {
            using HttpResponseMessage response = _httpClient.PostAsync(
                "/rpc",
                new StringContent(
                    "{\"method\":\"__health\",\"params\":{},\"id\":0}",
                    Encoding.UTF8,
                    "application/json")).GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>执行一次 JSON-RPC 调用（调度器唯一传输入口）。返回原始 JSON 文本。</summary>
    public async Task<string> CallAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        HttpClient client = GetClient();
        var request = new { method, @params = parameters, id = 1 };
        using HttpResponseMessage response = await client.PostAsync(
            "/rpc",
            new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"),
            cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"引擎调用失败: HTTP {response.StatusCode}: {Truncate(body)}");
        return body;
    }

    /// <summary>Host 退出时杀引擎进程树（引擎生命周期 = Host 生命周期）。</summary>
    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
                return;
            _disposed = true;
            _httpClient?.Dispose();
            _httpClient = null;
            if (_process is { HasExited: false } process)
            {
                _log.Info($"终止引擎进程: pid={process.Id}");
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // 进程已自行退出
                }
                catch (Win32Exception)
                {
                    // Kill 失败（权限），尽力而为
                }
                process.Dispose();
            }
            _process = null;
        }
    }

    private HttpClient GetClient()
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ComfyUIEngineProcessManager));
            if (_httpClient == null)
                throw new InvalidOperationException("引擎进程未启动（先调 Start）");
            return _httpClient;
        }
    }

    private void WaitHealthy(CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < _healthCheckTimeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsHealthy())
                return;
            if (_process is { HasExited: true })
                throw new InvalidOperationException($"引擎进程提前退出: exit={_process.ExitCode}");
            Thread.Sleep(200);
        }
        throw new TimeoutException($"引擎进程健康探测超时（{_healthCheckTimeoutMs}ms）");
    }

    private static string Truncate(string text) => text.Length <= 500 ? text : text[..500];
}
