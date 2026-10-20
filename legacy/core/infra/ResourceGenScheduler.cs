using System.Collections.Concurrent;
using System.Text.Json;

namespace Toolkit.Core.Infra;

/// <summary>
/// 资源生成调度器 - 任务调度业务逻辑唯一实现（Host 侧，C#）。
///
/// 职责边界：任务生命周期（task_id/状态机/异步提交/跨操作上下文/产物落盘）全部在此；
/// 引擎进程（creatai-assets server）只做原子执行，不含任何调度/状态/流程记忆。
/// 产物落 Host 侧缓存目录（用户级目录，禁落工程）；工具响应只带 base64 或 task_id 引用。
/// </summary>
public sealed class ResourceGenScheduler : IDisposable
{
    // 并发上限：引擎进程单实例，同时执行的生成任务数受限（防并发提交时线程/连接无限堆积）
    private const int MaxConcurrentExecutions = 4;

    private readonly Log _log = Log.For(LogModule.Core);
    private readonly ComfyUIEngineProcessManager _engine;
    private readonly string _cacheDirFullPath;
    private readonly ConcurrentDictionary<string, ResourceGenTask> _tasks = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _executionGate = new(MaxConcurrentExecutions, MaxConcurrentExecutions);
    private bool _disposed;

    public ResourceGenScheduler(ComfyUIEngineProcessManager engine, string cacheDirFullPath)
    {
        _engine = engine;
        _cacheDirFullPath = cacheDirFullPath;
        Directory.CreateDirectory(cacheDirFullPath);
    }

    /// <summary>提交生成任务。wait=false 立即返回 task_id；wait=true 阻塞到就绪并返回结果。</summary>
    public async Task<ResourceGenSubmitResult> SubmitAsync(string method, JsonElement arguments, bool wait, CancellationToken cancellationToken)
    {
        string taskId = Guid.NewGuid().ToString("N");
        ResourceGenTask task;
        using (JsonDocument cloned = JsonDocument.Parse(arguments.GetRawText()))
        {
            task = new ResourceGenTask(taskId, method, cloned.RootElement.Clone());
            _tasks[taskId] = task;
        }
        _log.Info($"任务提交: {taskId} method={method} wait={wait}");

        // 有界并发：SemaphoreSlim 门控执行，不按提交量开无限线程/任务
        Task runTask = Task.Run(
            () => ExecuteGatedAsync(task, cancellationToken),
            CancellationToken.None);
        if (!wait)
            return new ResourceGenSubmitResult(taskId, "pending", null);
        try
        {
            await runTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Fail(task, ex);
        }
        return new ResourceGenSubmitResult(taskId, task.State, task.Result);
    }

    private async Task ExecuteGatedAsync(ResourceGenTask task, CancellationToken cancellationToken)
    {
        try
        {
            await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            task.State = "canceled";
            return;
        }
        try
        {
            await ExecuteAsync(task, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _executionGate.Release();
        }
    }

    public ResourceGenTaskStatus GetStatus(string taskId)
    {
        ResourceGenTask? task = _tasks[taskId];
        return task is null
            ? throw new KeyNotFoundException($"任务不存在: {taskId}")
            : new ResourceGenTaskStatus(taskId, task.State, task.Error);
    }

    public ResourceGenTaskResult GetResult(string taskId)
    {
        ResourceGenTask task = GetTask(taskId);
        if (task.State != "ready" || task.Result is null)
            throw new InvalidOperationException($"任务未就绪: {taskId}（state={task.State}）");
        return task.Result;
    }

    /// <summary>跨操作上下文：以任务内候选产物为输入（render/review/confirm 共用）。</summary>
    public async Task<JsonElement> RunOnTaskAsync(string taskId, string method, JsonElement extraArguments, CancellationToken cancellationToken)
    {
        ResourceGenTask task = GetTask(taskId);
        if (task.State == "failed")
            throw new InvalidOperationException($"任务已失败: {taskId}（{task.Error}）");
        if (task.Result?.OutputFiles is not { Count: > 0 } outputFiles || outputFiles.Count == 0)
            throw new InvalidOperationException($"任务无候选产物: {taskId}（state={task.State}）");

        JsonElement combined = BuildTaskArguments(extraArguments, GetCandidateFilePath(taskId));
        string body = await _engine.CallAsync(method, JsonSerializer.Deserialize<object>(combined.GetRawText()), cancellationToken).ConfigureAwait(false);
        return ParseResultBody(body, method);
    }

    /// <summary>确认入库：task 产物 + 确认信息 → 已存在的入库入口（调用方执行持久化事务）。</summary>
    public string GetCandidateFilePath(string taskId)
    {
        ResourceGenTask task = GetTask(taskId);
        if (task.Result?.OutputFiles is not { Count: > 0 } outputFiles || outputFiles.Count == 0)
            throw new InvalidOperationException($"任务无候选产物: {taskId}（state={task.State}）");
        return SystemIOWrapper.Combine(_cacheDirFullPath, outputFiles[0]);
    }

    public void Cancel(string taskId)
    {
        ResourceGenTask? task = _tasks[taskId];
        if (task is not null && task.State is "pending" or "running")
        {
            task.State = "canceled";
        }
    }

    private async Task ExecuteAsync(ResourceGenTask task, CancellationToken cancellationToken)
    {
        task.State = "running";
        try
        {
            string body = await _engine.CallAsync(task.Method, JsonSerializer.Deserialize<object>(task.Arguments.GetRawText()), cancellationToken).ConfigureAwait(false);
            JsonElement result = ParseResultBody(body, task.Method);
            IReadOnlyList<string> outputFiles = PersistOutputs(task, result);
            task.Result = new ResourceGenTaskResult(result, outputFiles);
            task.State = "ready";
            _log.Info($"任务完成: {task.TaskId} method={task.Method}");
        }
        catch (Exception ex)
        {
            Fail(task, ex);
        }
    }

    private void Fail(ResourceGenTask task, Exception ex)
    {
        task.State = "failed";
        task.Error = ex.Message;
        _log.Warning($"任务失败: {task.TaskId} method={task.Method}: {ex.Message}");
    }

    /// <summary>base64 产物字段落盘为缓存文件（image/audio/video 等大字段），返回文件名列表。</summary>
    private static IReadOnlyList<string> PersistOutputs(ResourceGenTask task, JsonElement result)
    {
        List<string> files = [];
        if (result.ValueKind != JsonValueKind.Object)
            return files;
        foreach (JsonProperty property in result.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                continue;
            string? base64 = property.Value.GetString();
            bool isPayload = base64 is not null && base64.Length >= 64
                && (property.Name.EndsWith("_base64", StringComparison.Ordinal)
                    || property.Name is "mp4_base64" or "audio_base64");
            if (!isPayload || base64 is null)
                continue;
            string fileName = $"{property.Name}.bin";
            File.WriteAllBytes(SystemIOWrapper.Combine(task.CacheDirFullPath, fileName), Convert.FromBase64String(base64));
            files.Add(fileName);
        }
        return files;
    }

    /// <summary>任务参数 + 候选产物引用（image_base64 注入任务候选文件），供跨操作调用。</summary>
    private static JsonElement BuildTaskArguments(JsonElement extraArguments, string candidateFileFullPath)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            if (extraArguments.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in extraArguments.EnumerateObject())
                {
                    property.WriteTo(writer);
                }
            }
            writer.WriteString("image_base64", Convert.ToBase64String(File.ReadAllBytes(candidateFileFullPath)));
            writer.WriteEndObject();
        }
        using JsonDocument document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static JsonElement ParseResultBody(string body, string method)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("error", out JsonElement error))
            throw new InvalidOperationException($"引擎执行失败[{method}]: {error.GetProperty("message").GetString()}");
        return root.GetProperty("result").Clone();
    }

    private ResourceGenTask GetTask(string taskId)
    {
        ResourceGenTask? task = _tasks[taskId];
        if (task is null)
            throw new KeyNotFoundException($"任务不存在: {taskId}");
        return task;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (ResourceGenTask task in _tasks.Values)
        {
            if (task.State is "pending" or "running")
                task.State = "canceled";
        }
        _tasks.Clear();
    }
}

/// <summary>调度任务：状态机 pending → running → ready / failed / canceled。</summary>
internal sealed class ResourceGenTask
{
    public ResourceGenTask(string taskId, string method, JsonElement arguments)
    {
        TaskId = taskId;
        Method = method;
        Arguments = arguments.Clone();
        CacheDirFullPath = SystemIOWrapper.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Toolkit", "ResourceGen", taskId);
        Directory.CreateDirectory(CacheDirFullPath);
    }

    public string TaskId { get; }
    public string Method { get; }
    public JsonElement Arguments { get; }
    public string CacheDirFullPath { get; }
    public string State { get; set; } = "pending";
    public string? Error { get; set; }
    public ResourceGenTaskResult? Result { get; set; }
}

public sealed record ResourceGenSubmitResult(string TaskId, string State, ResourceGenTaskResult? Result);

public sealed record ResourceGenTaskStatus(string TaskId, string State, string? Error);

public sealed record ResourceGenTaskResult(JsonElement Output, IReadOnlyList<string> OutputFiles);
