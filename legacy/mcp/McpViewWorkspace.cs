using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Toolkit.Core.Infra;
using Toolkit.Core.Bescript;
using Toolkit.Core.Canvas;
using Toolkit.Core.Persistence;
using Toolkit.Core.TsView;

namespace Toolkit.Mcp;

/// <summary>
/// MCP 视图工作区 - 仅负责视图内容缓存。
/// 所有文件操作统一通过 ProjectModel，禁止自建文件管理。
/// </summary>
public sealed class McpViewWorkspace : IDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly TimeSpan RebuildFailureWindow = TimeSpan.FromSeconds(60);
    private const int RebuildFailureExitThreshold = 3;
    // 单项构建/渲染超过该耗时即告警：重建期间不输出中间日志，慢项是卡死排查的第一线索。
    private const double SlowItemLogThresholdMs = 10_000;
    // 请求等待视图同步超过该耗时即告警：Monitor.Wait 无超时，等待过程必须可见。
    private const double SyncWaitLogThresholdMs = 2_000;

    private sealed record DeviceViewState(
        string DeviceRelativePath,
        string ViewPath,
        string ViewFileRelativePath,
        string ContentHash,
        string TsView,
        string[] ReferencedDeviceRelativePaths);

    /// <summary>
    /// RawHash = canvas 二进制数据的 hash（Layer 1 快速短路）。
    /// ContentHash = 生成的 HTML 文本的 hash（Layer 2 精准短路，用于 PNG 缓存）。
    /// </summary>
    private sealed record CanvasHtmlViewState(
        string CanvasPath,
        string ViewPath,
        string ViewFileRelativePath,
        string RawHash,
        string ContentHash,
        string Html);

    /// <summary>
    /// 内联 Canvas 预览条目。SourceHash = arg.DecodedText 的 hash（Layer 2，PNG 缓存用）。
    /// </summary>
    private sealed record InlineCanvasPreviewState(
        string ViewPath,
        string SourceHash,
        string Html);

    /// <summary>
    /// 按脚本路径缓存的内联 canvas 集合。ScriptHash = 脚本 SourceText 的 hash（Layer 1 快速短路）。
    /// ScriptHash 匹配时直接复用 Previews，跳过 AST 遍历 + FromInline + ToHtml 全链路。
    /// </summary>
    private sealed record ScriptInlineCacheEntry(
        string ScriptHash,
        List<InlineCanvasPreviewState> Previews);

    /// <summary>
    /// PNG缓存条目，字典 key 为视图路径。
    /// </summary>
    private sealed record PngCacheEntry(
        string SourceHash,
        byte[] PngData);

    /// <summary>
    /// CodeRepo.ts 产物缓存条目。Valid = Read 成功且无错误（此时 TsView 非 null）。
    /// </summary>
    private sealed record CodeRepoTsProduct(string? TsView, bool Valid);

    private readonly Core.ToolkitServices _services;
    private readonly string _viewsRootRelativePath;
    private readonly ProjectModel _model;
    private readonly object _lock = new();

    private readonly Dictionary<string, DeviceViewState> _deviceViews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CanvasHtmlViewState> _canvasViews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _deviceReverseRefs = new(StringComparer.Ordinal);
    /// <summary> 内联 canvas 按脚本缓存，key=scriptRelativePath。阶段四整批覆盖。 </summary>
    private readonly Dictionary<string, ScriptInlineCacheEntry> _scriptInlineCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PngCacheEntry> _pngCache = new(StringComparer.Ordinal);
    /// <summary>custom-logic.d.ts 产物缓存，key=逻辑内容指纹。指纹不变则产物不变，跳过 TsCompilerHost。</summary>
    private readonly Dictionary<string, string> _customLogicDtsByFingerprint = new(StringComparer.Ordinal);
    /// <summary>CodeRepo.ts 产物缓存，key=逻辑内容指纹。命中跳过整个 Read（含 TsCompilerHost 生成）。</summary>
    private readonly Dictionary<string, CodeRepoTsProduct> _codeRepoTsByFingerprint = new(StringComparer.Ordinal);

    private bool _disposed;
    private long _lastRebuiltRefreshVersion;
    private bool _isRebuildingViews;
    private bool _rebuildRequested;
    private long _pendingRebuildStartTicks;
    private string _pendingRebuildSource = string.Empty;
    private bool _isSynchronized;
    private int _consecutiveRebuildFailures;
    private DateTimeOffset _firstRebuildFailureAt;
    private string? _lastRebuildFailureMessage;
    private bool _rebuildFailedPermanently;
    public event EventHandler<EventArgs>? SynchronizationStateChanged;
    public bool IsSynchronized
    {
        get
        {
            lock (_lock)
            {
                return _isSynchronized && !_isRebuildingViews && !_rebuildRequested;
            }
        }
    }
    public string? LastRebuildFailureMessage
    {
        get
        {
            lock (_lock)
            {
                return _lastRebuildFailureMessage;
            }
        }
    }
    public bool HasPermanentRebuildFailure
    {
        get
        {
            lock (_lock)
            {
                return _rebuildFailedPermanently;
            }
        }
    }

    public McpViewWorkspace(Core.ToolkitServices services)
    {
        _services = services;
        _viewsRootRelativePath = ScriptPathHelper.ViewsPath;
        _model = services.Model;

        // 订阅模型刷新事件
        _model.ModelRefreshed += OnModelRefreshed;
        _model.FileChangeDetected += OnModelFileChangeDetected;
        _model.WorkspaceBlockStateChanged += OnWorkspaceBlockStateChanged;
        RequestViewRebuild("工作区初始化");
    }

    /// <summary>
    /// 模型检测到文件变化：撤销同步标记。
    /// </summary>
    private void OnModelFileChangeDetected(object? sender, EventArgs e)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _isSynchronized = false;
            Monitor.PulseAll(_lock);
        }
    }

    /// <summary>
    /// 模型刷新完成：请求视图重建（仅消费事件，不反向驱动 Model）。
    /// </summary>
    private void OnModelRefreshed(object? sender, EventArgs e)
    {
        try
        {
            // 内容版本未变 = 模型无实体变化（无影响刷新/自噪音）：视图仍然有效，
            // 仅恢复被 FileChangeDetected 保守撤销的同步标记，禁止级联全量重建。
            if (_model.RefreshVersion == _lastRebuiltRefreshVersion)
            {
                lock (_lock)
                {
                    if (_disposed) return;
                    if (!_isSynchronized && !_isRebuildingViews && !_rebuildRequested)
                    {
                        _isSynchronized = true;
                        Monitor.PulseAll(_lock);
                    }
                }
                return;
            }
            RequestViewRebuild("模型刷新事件");
        }
        catch (Exception ex)
        {
            Log.For(LogModule.Mcp).Exception(ex, "视图刷新失败");
            Log.For(LogModule.Mcp).Error($"[视图刷新失败][详细堆栈]\n{ex}");
            LogAggregateInnerExceptions(Log.For(LogModule.Mcp), "视图刷新失败", ex);
        }
    }

    /// <summary>
    /// 工作区受阻解除时重新请求视图重建。
    /// 受阻期间 loop 已退出；解除后由本回调重新启动，与 ProjectModel 受阻排空对称。
    /// 仅在解除（!IsBlocked）时触发——受阻进入时 loop 已自行捕获并退出，无需重复。
    /// </summary>
    private void OnWorkspaceBlockStateChanged(object? sender, WorkspaceBlockStateChangedEventArgs e)
    {
        if (e.State.IsBlocked) return;
        try
        {
            RequestViewRebuild("工作区受阻解除");
        }
        catch (Exception ex)
        {
            Log.For(LogModule.Mcp).Exception(ex, "受阻解除后请求视图重建失败");
        }
    }

    /// <summary>
    /// 请求视图重建；如果当前没有重建任务则启动后台重建循环。
    /// </summary>
    private void RequestViewRebuild(string source)
    {
        bool shouldStartWorker = false;

        lock (_lock)
        {
            ThrowIfDisposed();
            bool wasSynchronized = _isSynchronized && !_isRebuildingViews && !_rebuildRequested;
            _isSynchronized = false;
            if (!_rebuildRequested)
            {
                _pendingRebuildStartTicks = Stopwatch.GetTimestamp();
                _pendingRebuildSource = source;
            }
            _rebuildRequested = true;
            if (!_isRebuildingViews)
            {
                _isRebuildingViews = true;
                shouldStartWorker = true;
            }
            Monitor.PulseAll(_lock);
            if (wasSynchronized)
            {
                SynchronizationStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        if (!shouldStartWorker)
        {
            return;
        }

        Task.Run(ProcessViewRebuildLoop);
    }

    private void ProcessViewRebuildLoop()
    {
        while (true)
        {
            long requestStartTicks;
            string requestSource;

            lock (_lock)
            {
                if (_disposed)
                {
                    _isRebuildingViews = false;
                    Monitor.PulseAll(_lock);
                    SynchronizationStateChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }

                if (!_rebuildRequested)
                {
                    _isRebuildingViews = false;
                    Monitor.PulseAll(_lock);
                    SynchronizationStateChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }

                requestStartTicks = _pendingRebuildStartTicks;
                requestSource = _pendingRebuildSource;
                _pendingRebuildStartTicks = 0;
                _pendingRebuildSource = string.Empty;
                _rebuildRequested = false;
            }

            try
            {
                Log.For(LogModule.Mcp).Info($"视图刷新开始, 来源: {requestSource}");
                RebuildViewsOnlyInternal(requestStartTicks, requestSource);
            }
            catch (Exception ex)
            {
                WorkspaceFileBlockedException? blockedEx = ExtractBlocked(ex);
                if (blockedEx is not null)
                {
                    // 受阻 = 可恢复瞬态：不计失败、不杀进程；通知受阻态并退出 loop。
                    // 受阻解除后由 OnWorkspaceBlockStateChanged 重新 RequestViewRebuild 恢复（与 ProjectModel 事务排空对称）。
                    Log.For(LogModule.Mcp).Exception(blockedEx, $"视图刷新失败（文件被外部进程锁定: {blockedEx.RelativePath}，跳过本轮）");
                    FileSystemCache.Instance.ReportBlockedException(blockedEx);
                    lock (_lock)
                    {
                        _isRebuildingViews = false;
                        _isSynchronized = false;
                        Monitor.PulseAll(_lock);
                    }
                    SynchronizationStateChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }

                // 非受阻失败：计数重试；达阈值置 _rebuildFailedPermanently 软失败（BuildHostState 报 error、McpRequestHandler 拒请求），不杀进程。
                ReportViewRebuildFailure(ex);
                SynchronizationStateChanged?.Invoke(this, EventArgs.Empty);
                continue;
            }
        }
    }

    /// <summary>
    /// 执行操作并等待视图同步。
    /// 操作内部通过 SaveChanges 触发模型刷新 → ModelRefreshed → RequestViewRebuild，
    /// 本方法在操作返回后阻塞到 _isSynchronized 且无待处理重建。
    /// </summary>
    internal void WaitUntilSynchronized(Action operation)
    {
        operation();
        lock (_lock)
        {
            ThrowIfDisposed();
            WaitUntilSynchronizedLocked();
        }
    }

    /// <summary>
    /// 执行操作并等待视图同步，返回操作结果。
    /// 编译器可跟踪返回值明确赋值，无需外部捕获变量再判空。
    /// </summary>
    internal T WaitUntilSynchronized<T>(Func<T> operation)
    {
        T result = operation();
        lock (_lock)
        {
            ThrowIfDisposed();
            WaitUntilSynchronizedLocked();
        }
        return result;
    }

    private void WaitUntilSynchronizedLocked()
    {
        long waitStartTicks = Stopwatch.GetTimestamp();
        bool waitLogged = false;
        while (!_isSynchronized || _isRebuildingViews || _rebuildRequested)
        {
            // 受阻态下视图不可能同步——抛 WorkspaceFileBlockedException，由 MCP 层返回"工作区受阻"，
            // 与 ProjectModel.SaveChanges 受阻语义一致；禁止永久挂起。
            if (_model.IsWorkspaceBlocked)
            {
                throw new WorkspaceFileBlockedException(
                    WorkspacePath.RootDirectory,
                    "工作区受阻，视图无法同步",
                    WorkspaceFileBlockedException.DefaultBlockMessage);
            }
            if (!waitLogged && Stopwatch.GetElapsedTime(waitStartTicks).TotalMilliseconds > SyncWaitLogThresholdMs)
            {
                waitLogged = true;
                Log.For(LogModule.Mcp).Warning($"请求正在等待视图同步（重建中: {_isRebuildingViews}，待重建: {_rebuildRequested}）...");
            }
            Monitor.Wait(_lock);
            ThrowIfDisposed();
        }
        if (waitLogged)
        {
            Log.For(LogModule.Mcp).Info($"视图同步等待结束, 等待用时 {Stopwatch.GetElapsedTime(waitStartTicks).TotalMilliseconds:F0}ms");
        }
    }

    public string WriteViewDraft(string viewPath, string currentText, string draftText)
    {
        string content = JsonSerializer.Serialize(new ViewDraftFile(
            ComputeTextHash(currentText),
            DateTime.UtcNow,
            draftText));
        string draftRelativePath = DraftStore.DraftPathFor(_viewsRootRelativePath, viewPath);
        DraftStore.Write(draftRelativePath, content);
        return draftRelativePath;
    }

    public (string ViewPath, string CurrentText, string DraftText, IReadOnlyList<ToolError> Errors) ReadViewDraft(string draftRelativePath)
    {
        string draftPath = WorkspacePath.RequireRelativePathOrEmpty(draftRelativePath);
        if (!DraftStore.Exists(draftPath))
        {
            return (string.Empty, string.Empty, string.Empty, [new ToolError(draftPath, null, null, $"草稿文件不存在: {draftPath}")]);
        }

        string draftText = DraftStore.Read(draftPath);
        string viewPath = DraftStore.ViewPathFromDraft(_viewsRootRelativePath, draftPath);
        (bool ok, string baselineHash, _, string draftContent, string message) = ParseViewDraftFile(draftText);
        if (!ok)
        {
            return (viewPath, string.Empty, string.Empty, [new ToolError(draftPath, null, null, message)]);
        }

        (string currentText, IReadOnlyList<ToolError> readErrors) = ReadCurrentViewText(viewPath);
        if (readErrors.Count > 0)
        {
            return (viewPath, string.Empty, string.Empty, readErrors);
        }

        if (!string.Equals(ComputeTextHash(currentText), baselineHash, StringComparison.Ordinal))
        {
            return (viewPath, string.Empty, string.Empty, [new ToolError(draftPath, null, null, $"草稿基线已变化: {draftPath}")]);
        }

        return (viewPath, currentText, draftContent, Array.Empty<ToolError>());
    }

    public int CleanupViewDrafts(IReadOnlyList<string> draftRelativePaths)
    {
        if (draftRelativePaths.Count == 0)
        {
            return 0;
        }

        int removedCount = 0;
        HashSet<string> uniqueDraftPaths = new(StringComparer.Ordinal);
        foreach (string draftRelativePath in draftRelativePaths)
        {
            string draftPath = WorkspacePath.RequireRelativePathOrEmpty(draftRelativePath);
            if (!uniqueDraftPaths.Add(draftPath))
            {
                continue;
            }

            if (!DraftStore.Exists(draftPath))
            {
                continue;
            }

            DraftStore.Delete(draftPath);
            removedCount++;
        }

        return removedCount;
    }

    /// <summary>
    /// 仅使用当前 Model 快照重建视图，不主动刷新 Model。
    /// 简化流程：计算视图 → 生成 PNG → 一次保存。
    /// </summary>
    private void RebuildViewsOnlyInternal(long requestStartTicks, string requestSource)
    {
        long rebuildStartTicks = Stopwatch.GetTimestamp();

        // === 阶段一（并行）：计算所有视图内容 ===
        string[] devicePaths;
        lock (_lock) { devicePaths = EnumerateDevicePaths().ToArray(); }
        List<(string Path, DeviceViewState State)> deviceResults = BuildDeviceViewsParallel(devicePaths);

        string[] canvasPaths;
        lock (_lock) { canvasPaths = EnumerateCanvasPaths().ToArray(); }
        List<(string Path, CanvasHtmlViewState State)> canvasResults = BuildCanvasHtmlViewsParallel(canvasPaths);
        List<InlineCanvasPreviewState> inlineCanvasResults = CollectInlineCanvasPreviewHtmls();
        long phase1EndTicks = Stopwatch.GetTimestamp();

        // === 阶段二：PNG 预览生成（CDP 渲染校验仅在 patch 路径执行，刷新路径不做） ===
        List<(string Path, byte[] Data)> generatedPngs = [];
        if (canvasResults.Count > 0 || inlineCanvasResults.Count > 0)
        {
            generatedPngs = GeneratePngsConcurrent(canvasResults, inlineCanvasResults);
        }
        long phase2EndTicks = Stopwatch.GetTimestamp();

        // === 阶段三：构建完整可写实体树并一次保存 ===
        (WritableToolkitRootEntity writableToolkit, bool hierarchyChanged) = EnsureToolkitHierarchy();
        long versionAfterHierarchy = _model.RefreshVersion;
        WritableToolkitInternalDirectoryEntity writableViews = BuildWritableViewsTree(
            writableToolkit, deviceResults, canvasResults, generatedPngs);
        WritableToolkitInternalDirectoryEntity writableCanvasRender = new WritableToolkitInternalDirectoryEntity("canvas_render")
        {
            Children = [],
            Parent = writableToolkit
        };
        Dictionary<string, WritableToolkitInternalDirectoryEntity> canvasRenderDirCache = new Dictionary<string, WritableToolkitInternalDirectoryEntity>(StringComparer.Ordinal)
        {
            [string.Empty] = writableCanvasRender
        };
        ColorPalette palette = _services.Logic.GetColorPalette();
        IReadOnlyDictionary<string, PaletteColor> paletteColors = palette.GetAllColors();
        IReadOnlyList<CanvasHtmlSupportAsset> canvasAssets = CanvasHtmlViewAssets.BuildStaticSupportAssets(paletteColors, _services.CanvasPreviewSettings);
        foreach (CanvasHtmlSupportAsset asset in canvasAssets)
        {
            AddWritableFileToTree(writableCanvasRender, canvasRenderDirCache, asset.FileName, asset.Content);
        }
        string paletteScript = CanvasHtmlViewAssets.BuildPaletteScript(paletteColors);
        AddWritableFileToTree(writableCanvasRender, canvasRenderDirCache,
            CanvasHtmlViewAssets.PaletteFileName, paletteScript);
        string guidMapScript = CanvasHtmlViewAssets.BuildStandaloneGuidMapScript(_model);
        AddWritableFileToTree(writableCanvasRender, canvasRenderDirCache,
            CanvasHtmlViewAssets.StandaloneGuidMapFileName, guidMapScript);

        WritableToolkitInternalDirectoryEntity writableBescriptReference = new WritableToolkitInternalDirectoryEntity("bescript_reference")
        {
            Children = [],
            Parent = writableToolkit
        };
        Dictionary<string, WritableToolkitInternalDirectoryEntity> bescriptReferenceDirCache = new Dictionary<string, WritableToolkitInternalDirectoryEntity>(StringComparer.Ordinal)
        {
            [string.Empty] = writableBescriptReference
        };
        string sdkText = BescriptTsViewDeclarationLibrary.GetSdkText();
        string customLogicDts = GetOrBuildCustomLogicDts();
        AddWritableFileToTree(writableBescriptReference, bescriptReferenceDirCache,
            BescriptTsViewDeclarationLibrary.SdkFileName, sdkText);
        AddWritableFileToTree(writableBescriptReference, bescriptReferenceDirCache,
            CustomLogicTsViewDeclarationLibrary.DtsFileName, customLogicDts);
        CodeRepoTsProduct codeRepoProduct = GetOrBuildCodeRepoTs();
        if (codeRepoProduct is { Valid: true, TsView: string tsView })
        {
            AddWritableFileToTree(writableBescriptReference, bescriptReferenceDirCache,
                "CodeRepo.ts", tsView);
        }

        WritableToolkitFileEntity writableTsConfig = new WritableToolkitFileEntity(BescriptTsConfigFileName)
        {
            Content = BescriptTsConfigContent,
            Parent = writableToolkit
        };

        writableToolkit.Children.Clear();

        writableToolkit.Children.Add(writableViews);
        writableToolkit.Children.Add(writableCanvasRender);
        writableToolkit.Children.Add(writableBescriptReference);
        writableToolkit.Children.Add(writableTsConfig);

        (IReadOnlyList<string> writeAffected, bool versionMatched) =
            _model.SaveChangesIfVersionMatches(writableToolkit, versionAfterHierarchy);
        if (!versionMatched)
            return;
        bool viewFilesChanged = hierarchyChanged || writeAffected.Count > 0;
        long phase3EndTicks = Stopwatch.GetTimestamp();

        // === 阶段四（串行）：更新内存缓存 ===
        // 指纹计算持模型读锁，在 _lock 外完成避免嵌套
        string logicFingerprint = _services.Logic.ComputeLogicFingerprint();
        lock (_lock)
        {
            foreach ((string path, DeviceViewState state) in deviceResults)
            {
                if (_deviceViews.ContainsKey(path))
                    RemoveReverseRefsLocked(path, _deviceViews[path].ReferencedDeviceRelativePaths);
                AddReverseRefsLocked(path, state.ReferencedDeviceRelativePaths);
                _deviceViews[path] = state;
            }
            foreach ((string path, CanvasHtmlViewState state) in canvasResults)
            {
                _canvasViews[path] = state;
            }
            // 内联 canvas 缓存已在 CollectInlineCanvasFromScript 中按脚本更新。
            // 清理已不存在的脚本条目及其 PNG 缓存。
            RemoveStaleScriptInlineCacheLocked();
            // 产物缓存（dts/CodeRepo.ts）只保留当前逻辑指纹
            PruneFingerprintKeysLocked(_customLogicDtsByFingerprint, logicFingerprint);
            PruneFingerprintKeysLocked(_codeRepoTsByFingerprint, logicFingerprint);

            _isSynchronized = !_rebuildRequested;
            _lastRebuiltRefreshVersion = _model.RefreshVersion;
            _consecutiveRebuildFailures = 0;
            _firstRebuildFailureAt = default;
            _lastRebuildFailureMessage = null;
            _rebuildFailedPermanently = false;
            Monitor.PulseAll(_lock);
        }
        // AST 代际缓存：淘汰本轮未被复用的条目（ScriptAstCache 独立锁，在 _lock 外调用避免嵌套）
        _services.Logic.PruneScriptCache();
        long phase4EndTicks = Stopwatch.GetTimestamp();

        double queueMs = Stopwatch.GetElapsedTime(requestStartTicks, rebuildStartTicks).TotalMilliseconds;
        double phase1Ms = Stopwatch.GetElapsedTime(rebuildStartTicks, phase1EndTicks).TotalMilliseconds;
        double phase2Ms = Stopwatch.GetElapsedTime(phase1EndTicks, phase2EndTicks).TotalMilliseconds;
        double phase3Ms = Stopwatch.GetElapsedTime(phase2EndTicks, phase3EndTicks).TotalMilliseconds;
        double phase4Ms = Stopwatch.GetElapsedTime(phase3EndTicks, phase4EndTicks).TotalMilliseconds;
        double totalMs = Stopwatch.GetElapsedTime(requestStartTicks, phase4EndTicks).TotalMilliseconds;
        string syncText = "已同步";
        if (viewFilesChanged)
            syncText = "有文件变更";
        Log.For(LogModule.Mcp).Info(
            $"视图刷新完成（{syncText}）, 来源: {requestSource}, " +
            $"Device: {deviceResults.Count}, Canvas: {canvasResults.Count}（内联: {inlineCanvasResults.Count}）, PNG: {generatedPngs.Count}, " +
            $"总用时: {totalMs:F0}ms（排队: {queueMs:F0}ms, 计算视图: {phase1Ms:F0}ms, 预览图渲染: {phase2Ms:F0}ms, 持久化: {phase3Ms:F0}ms, 同步: {phase4Ms:F0}ms）");
    }

    private void ReportViewRebuildFailure(Exception ex)
    {
        Log log = Log.For(LogModule.Mcp);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int failureCount;
        lock (_lock)
        {
            if (_consecutiveRebuildFailures == 0 || now - _firstRebuildFailureAt > RebuildFailureWindow)
            {
                _firstRebuildFailureAt = now;
                _consecutiveRebuildFailures = 0;
            }

            _consecutiveRebuildFailures++;
            failureCount = _consecutiveRebuildFailures;
            _lastRebuildFailureMessage = $"{ex.GetType().Name}: {ex.Message}";
            _isSynchronized = false;
            if (failureCount < RebuildFailureExitThreshold)
            {
                _pendingRebuildStartTicks = Stopwatch.GetTimestamp();
                _pendingRebuildSource = $"视图刷新失败后重试 {failureCount}/{RebuildFailureExitThreshold}";
                _rebuildRequested = true;
            }
            else
            {
                _rebuildFailedPermanently = true;
            }
            Monitor.PulseAll(_lock);
        }

        string prefix = $"视图刷新失败（{failureCount}/{RebuildFailureExitThreshold}）";
        log.Exception(ex, prefix);
        log.Error($"[{prefix}][详细堆栈]\n{ex}");
        LogAggregateInnerExceptions(log, prefix, ex);

        if (failureCount >= RebuildFailureExitThreshold)
            log.Error($"视图刷新在 {RebuildFailureWindow.TotalSeconds:F0} 秒内连续失败 {failureCount} 次，Host 进入不可用态。最近一次: {_lastRebuildFailureMessage}");
        else
            log.Warning($"[{prefix}] 已保留当前视图状态，等待下一轮全量重建。");
    }

    /// <summary>
    /// 展开 AggregateException 的内部异常并逐条记录。
    /// </summary>
    private static void LogAggregateInnerExceptions(Log log, string prefix, Exception ex)
    {
        if (ex is AggregateException aggEx && aggEx.InnerExceptions.Count > 0)
        {
            for (int i = 0; i < aggEx.InnerExceptions.Count; i++)
            {
                Exception inner = aggEx.InnerExceptions[i];
                log.Error($"[{prefix}][内部异常 {i + 1}/{aggEx.InnerExceptions.Count}]\n{inner}");
            }
        }
    }

    /// <summary>
    /// 从异常树中提取受阻异常（递归解包 AggregateException）。无则 null。
    /// loop 用它把被并行任务包装的受阻异常从普通失败中分流——受阻可恢复，不进失败计数。
    /// </summary>
    internal static WorkspaceFileBlockedException? ExtractBlocked(Exception? exception)
    {
        if (exception is WorkspaceFileBlockedException blocked) return blocked;
        if (exception is AggregateException aggregate)
        {
            foreach (Exception inner in aggregate.InnerExceptions)
            {
                WorkspaceFileBlockedException? found = ExtractBlocked(inner);
                if (found is not null) return found;
            }
        }
        return null;
    }





    #region 视图读取（仅内存缓存）

    /// <summary>
    /// 读取 Device TS 视图（仅从内存缓存读取）。
    /// 发现缺失只报告当前状态，绝对禁止读时触发补建。
    /// forPatch 时仅等待已有重建完成。
    /// </summary>
    public (string Text, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) ReadDeviceView(
        string devicePath,
        bool forPatch = false)
    {
        string relativePath = WorkspacePath.RequireRelativePathOrEmpty(devicePath);

        lock (_lock)
        {
            ThrowIfDisposed();

            // forPatch 时等待视图同步完成
            if (forPatch)
            {
                WaitUntilSynchronizedLocked();
            }

            if (!_deviceViews.ContainsKey(relativePath))
            {
                if (!_model.EntityExists(relativePath))
                {
                    return (string.Empty, [new ToolError(relativePath, null, null, $"路径不存在: {relativePath}")], Array.Empty<ToolError>());
                }

                return (string.Empty, [new ToolError(relativePath, null, null, $"视图尚未同步: {relativePath}")], Array.Empty<ToolError>());
            }

            DeviceViewState state = _deviceViews[relativePath];
            return (state.TsView, Array.Empty<ToolError>(), Array.Empty<ToolError>());
        }
    }

    /// <summary>
    /// 读 device 内联脚本原始 code 全文（R13 闭包，TS→code 1:1；CodeRepo 1:N 引用不附）。
    /// 实时从 _model 取，code 不进视图缓存（控上下文；read_view 低频）。
    /// 契约：调用方必须已确认零件存在（read_view/patch 路径均先经实体检查）；
    /// 零件不存在属契约违约，抛异常暴露，不返回 null。
    /// </summary>
    public IReadOnlyList<(string ScriptName, string? EventName, string SourceText)> GetDeviceSourceCode(string devicePath)
    {
        string relativePath = WorkspacePath.RequireRelativePathOrEmpty(devicePath);
        _model.EnterReadLock();
        try
        {
            DeviceEntity? device = _model.Find<DeviceEntity>(relativePath);
            if (device is null)
            {
                throw new InvalidOperationException($"零件实体未找到: {relativePath}");
            }
            return device.AllScripts.Select(s => (s.ScriptName, s.EventName, s.SourceText)).ToList();
        }
        finally
        {
            _model.ExitReadLock();
        }
    }

    public IReadOnlyList<(string DeviceRelativePath, string TsView)> GetDeviceViewSnapshots()
    {
        lock (_lock)
        {
            if (_disposed) return Array.Empty<(string, string)>();

            return _deviceViews.Values
                .Select(v => (v.DeviceRelativePath, v.TsView))
                .ToArray();
        }
    }

    /// <summary>
    /// 读取 Canvas HTML 视图（仅从内存缓存读取）。
    /// 发现缺失只报告当前状态，绝对禁止读时触发补建。
    /// forPatch 时仅等待已有重建完成。
    /// </summary>
    public (string Html, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) ReadCanvasHtmlView(
        string canvasPath,
        bool forPatch = false)
    {
        string relativePath = WorkspacePath.RequireRelativePathOrEmpty(canvasPath);

        lock (_lock)
        {
            ThrowIfDisposed();

            // forPatch 时等待视图同步完成
            if (forPatch)
            {
                WaitUntilSynchronizedLocked();
            }

            if (!_canvasViews.ContainsKey(relativePath))
            {
                if (!_model.EntityExists(relativePath))
                {
                    return (
                        string.Empty,
                        [new ToolError(relativePath, null, null, $"路径不存在: {relativePath}")],
                        Array.Empty<ToolError>());
                }

                return (
                    string.Empty,
                    [new ToolError(relativePath, null, null, $"视图尚未同步: {relativePath}")],
                    Array.Empty<ToolError>());
            }

            CanvasHtmlViewState state = _canvasViews[relativePath];
            return (state.Html, Array.Empty<ToolError>(), Array.Empty<ToolError>());
        }
    }

    #endregion


    #region 可写实体树构建

    private (WritableToolkitRootEntity Entity, bool Changed) EnsureToolkitHierarchy()
    {
        // 情况 1: temp 不存在，在可写工程根下创建 temp（含 toolkit）
        if (_model.Find<TempRootEntity>("temp") is not TempRootEntity tempRoot)
        {
            Log.For(LogModule.Mcp).Info("temp 目录不存在，创建 temp/toolkit...");


            WritableProjectRootEntity writableRoot = _model.Root.ToWritable();
            WritableTempRootEntity newTemp = new WritableTempRootEntity("temp")
            {
                Parent = writableRoot,
                Children = []
            };
            WritableToolkitRootEntity newToolkit = new WritableToolkitRootEntity
            {
                Parent = newTemp,
                Children = []
            };
            newTemp.Children.Add(newToolkit);
            writableRoot.Children.Add(newTemp);
            IReadOnlyList<string> affected = _model.SaveChanges(writableRoot);

            if (_model.Find<ToolkitRootEntity>("temp/toolkit") is not ToolkitRootEntity savedToolkitRoot)
                throw new InvalidOperationException("创建 temp/toolkit 后无法获取实体");
            return (savedToolkitRoot.ToWritable(), affected.Count > 0);
        }

        // 情况 2: temp 存在但 toolkit 不存在
        if (_model.Find<ToolkitRootEntity>("temp/toolkit") is not ToolkitRootEntity toolkitRoot)
        {
            Log.For(LogModule.Mcp).Info("temp/toolkit 目录不存在，创建中...");

            WritableTempRootEntity writableTemp = tempRoot.ToWritable();
            WritableToolkitRootEntity newToolkit = new WritableToolkitRootEntity
            {
                Parent = writableTemp,
                Children = []
            };
            writableTemp.Children.Add(newToolkit);
            IReadOnlyList<string> affected = _model.SaveChanges(writableTemp);

            if (_model.Find<ToolkitRootEntity>("temp/toolkit") is not ToolkitRootEntity savedToolkitRoot)
                throw new InvalidOperationException("创建 temp/toolkit 后无法获取实体");
            return (savedToolkitRoot.ToWritable(), affected.Count > 0);
        }

        // 情况 3: 都存在，直接返回可写副本
        return (toolkitRoot.ToWritable(), false);
    }

    /// <summary>
    /// 从头构建完整的 views 可写目录树（TS + HTML + PNG）。
    /// </summary>
    private WritableToolkitInternalDirectoryEntity BuildWritableViewsTree(
        WritableToolkitRootEntity parent,
        List<(string Path, DeviceViewState State)> deviceViews,
        List<(string Path, CanvasHtmlViewState State)> canvasViews,
        IReadOnlyList<(string Path, byte[] Data)> pngFiles)
    {
        WritableToolkitInternalDirectoryEntity viewsDir = new WritableToolkitInternalDirectoryEntity("views")
        {
            Children = [],
            Parent = parent
        };

        Dictionary<string, WritableToolkitInternalDirectoryEntity> dirCache = new Dictionary<string, WritableToolkitInternalDirectoryEntity>(StringComparer.Ordinal)
        {
            [string.Empty] = viewsDir
        };

        foreach ((string _, DeviceViewState state) in deviceViews)
        {
            AddWritableFileToTree(viewsDir, dirCache, state.ViewPath, state.TsView);
        }

        foreach ((string _, CanvasHtmlViewState state) in canvasViews)
        {
            AddWritableFileToTree(viewsDir, dirCache, state.ViewPath, state.Html);
        }

        foreach ((string draftPath, string content) in CollectMaintainedViewDrafts(deviceViews, canvasViews))
        {
            AddWritableFileToTree(viewsDir, dirCache, draftPath, content);
        }

        foreach ((string path, byte[] pngBytes) in pngFiles)
        {
            AddWritableBinaryFileToTree(viewsDir, dirCache, path, pngBytes);
        }

        return viewsDir;
    }

    private void AddWritableFileToTree(
        WritableToolkitInternalDirectoryEntity root,
        Dictionary<string, WritableToolkitInternalDirectoryEntity> dirCache,
        string fileRelativePath,
        string content)
    {
        string parentPath = RelativePathUtils.GetParentDirectory(fileRelativePath);
        string fileName = RelativePathUtils.GetLastSegment(fileRelativePath);

        WritableToolkitInternalDirectoryEntity parentDir = EnsureWritableDirectory(root, dirCache, parentPath);

        WritableToolkitFileEntity file = new WritableToolkitFileEntity(fileName)
        {
            Parent = parentDir,
            Content = content
        };
        parentDir.Children.Add(file);
    }

    private void AddWritableBinaryFileToTree(
        WritableToolkitInternalDirectoryEntity root,
        Dictionary<string, WritableToolkitInternalDirectoryEntity> dirCache,
        string fileRelativePath,
        byte[] content)
    {
        string parentPath = RelativePathUtils.GetParentDirectory(fileRelativePath);
        string fileName = RelativePathUtils.GetLastSegment(fileRelativePath);

        WritableToolkitInternalDirectoryEntity parentDir = EnsureWritableDirectory(root, dirCache, parentPath);

        WritableToolkitBinaryFileEntity file = new WritableToolkitBinaryFileEntity(fileName)
        {
            Parent = parentDir,
            Data = content
        };
        parentDir.Children.Add(file);
    }

    private IReadOnlyList<(string DraftPath, string Content)> CollectMaintainedViewDrafts(
        IReadOnlyList<(string Path, DeviceViewState State)> deviceViews,
        IReadOnlyList<(string Path, CanvasHtmlViewState State)> canvasViews)
    {
        if (!DraftStore.ViewsRootExists(_viewsRootRelativePath))
        {
            return Array.Empty<(string DraftPath, string Content)>();
        }

        Dictionary<string, string> currentHashes = new(StringComparer.Ordinal);
        foreach ((_, DeviceViewState state) in deviceViews)
        {
            currentHashes[state.ViewPath] = state.ContentHash;
        }

        foreach ((_, CanvasHtmlViewState state) in canvasViews)
        {
            currentHashes[state.ViewPath] = state.ContentHash;
        }

        string[] draftFiles = DraftStore.Enumerate(_viewsRootRelativePath);
        List<(string DraftPath, string Content)> kept = new(draftFiles.Length);
        foreach (string draftPath in draftFiles)
        {
            string viewPath = DraftStore.ViewPathFromDraft(_viewsRootRelativePath, DraftStore.DraftPathFor(_viewsRootRelativePath, draftPath));
            if (!currentHashes.ContainsKey(viewPath))
            {
                continue;
            }

            string currentHash = currentHashes[viewPath];
            string draftText = DraftStore.Read(DraftStore.DraftPathFor(_viewsRootRelativePath, draftPath));
            (bool Ok, string BaselineHash, DateTime CreatedUtc, string DraftContent, string Message) draftInfo = ParseViewDraftFile(draftText);
            if (!draftInfo.Ok || !string.Equals(currentHash, draftInfo.BaselineHash, StringComparison.Ordinal))
            {
                continue;
            }

            kept.Add((draftPath, draftText));
        }

        return kept;
    }

    private WritableToolkitInternalDirectoryEntity EnsureWritableDirectory(
        WritableToolkitInternalDirectoryEntity root,
        Dictionary<string, WritableToolkitInternalDirectoryEntity> dirCache,
        string dirPath)
    {
        if (dirCache.TryGetValue(dirPath, out WritableToolkitInternalDirectoryEntity? existing))
        {
            return existing;
        }

        string[] segments = dirPath.Split('/');
        string currentPath = WorkspacePath.RootDirectory;
        WritableToolkitInternalDirectoryEntity currentDir = root;

        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];
            currentPath = RelativePathUtils.Combine(currentPath, segment);
            if (dirCache.TryGetValue(currentPath, out WritableToolkitInternalDirectoryEntity? cached))
            {
                currentDir = cached;
                continue;
            }

            WritableToolkitInternalDirectoryEntity newDir = new WritableToolkitInternalDirectoryEntity(segment)
            {
                Parent = currentDir,
                Children = []
            };
            currentDir.Children.Add(newDir);
            dirCache[currentPath] = newDir;
            currentDir = newDir;
        }

        return currentDir;
    }

    #endregion

    #region 视图构建与写入

    private List<(string Path, DeviceViewState State)> BuildDeviceViewsParallel(string[] devicePaths)
    {
        // 原版游戏存在没有任何零件的地图/机械，空列表是合法状态，直接返回空结果
        if (devicePaths.Length == 0)
        {
            return [];
        }

        List<(string Path, DeviceViewState State)> results = [];

        if (devicePaths.Length <= 2)
        {
            foreach (string devicePath in devicePaths)
            {
                DeviceViewState state = BuildDeviceView(devicePath);
                results.Add((state.DeviceRelativePath, state));
            }
            return results;
        }

        ConcurrentBag<(string Path, DeviceViewState State)> concurrentResults = [];
        Parallel.ForEach(devicePaths, devicePath =>
        {
            DeviceViewState state = BuildDeviceView(devicePath);
            concurrentResults.Add((state.DeviceRelativePath, state));
        });

        return concurrentResults.ToList();
    }

    private DeviceViewState BuildDeviceView(string devicePath)
    {
        long startTicks = Stopwatch.GetTimestamp();
        (DeviceViewState? state, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = BuildDeviceViewCore(devicePath);
        if (state == null)
        {
            string errorMessages = string.Join("; ", errors.Select(e => e.Message));
            throw new InvalidOperationException($"Device TS 视图构建失败 ({devicePath}): {errorMessages}");
        }
        double elapsedMs = Stopwatch.GetElapsedTime(startTicks).TotalMilliseconds;
        if (elapsedMs > SlowItemLogThresholdMs)
        {
            Log.For(LogModule.Mcp).Warning($"Device TS 视图构建缓慢: {devicePath}, 用时 {elapsedMs:F0}ms");
        }
        return state;
    }

    private List<(string Path, CanvasHtmlViewState State)> BuildCanvasHtmlViewsParallel(string[] canvasPaths)
    {
        // 原版游戏存在没有任何独立 canvas 文件的工程，空列表是合法状态，直接返回空结果
        if (canvasPaths.Length == 0)
        {
            return [];
        }

        List<(string Path, CanvasHtmlViewState State)> results = [];

        if (canvasPaths.Length <= 2)
        {
            foreach (string canvasPath in canvasPaths)
            {
                CanvasHtmlViewState state = BuildCanvasHtmlView(canvasPath);
                results.Add((state.CanvasPath, state));
            }
            return results;
        }

        ConcurrentBag<(string Path, CanvasHtmlViewState State)> concurrentResults = [];
        Parallel.ForEach(canvasPaths, canvasPath =>
        {
            CanvasHtmlViewState state = BuildCanvasHtmlView(canvasPath);
            concurrentResults.Add((state.CanvasPath, state));
        });

        return concurrentResults.ToList();
    }

    /// <summary>
    /// Layer 1 快速短路：canvas 二进制 hash 未变 → 直接复用缓存。
    /// 未命中时走完整 Read + BinaryToHtml 管线。
    /// hash 直接从模型实体的 Data 计算，不穿透到文件缓存层。
    /// </summary>
    private CanvasHtmlViewState BuildCanvasHtmlView(string canvasPath)
    {
        string canvasRelativePath = WorkspacePath.RequireRelativePathOrEmpty(canvasPath);

        // 从模型实体获取二进制数据计算 hash（视图层只消费模型层，不穿透到文件缓存层）
        if (_model.Find<CanvasEntity>(canvasRelativePath) is not CanvasEntity canvasEntity)
            throw new InvalidOperationException($"Canvas 实体不存在: {canvasRelativePath}");
        string rawHash = Convert.ToHexString(SHA256.HashData(canvasEntity.Data));

        // Layer 1 命中：二进制未变，跳过 BinaryToHtml
        lock (_lock)
        {
            if (_canvasViews.ContainsKey(canvasRelativePath)
                && string.Equals(_canvasViews[canvasRelativePath].RawHash, rawHash, StringComparison.Ordinal))
            {
                return _canvasViews[canvasRelativePath];
            }
        }

        // Layer 1 未命中：完整转换
        long startTicks = Stopwatch.GetTimestamp();
        (CanvasHtmlViewState? builtState, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = BuildCanvasHtmlViewCore(canvasRelativePath, rawHash);
        if (builtState == null)
        {
            string errorMessages = string.Join("; ", errors.Select(e => e.Message));
            throw new InvalidOperationException($"Canvas HTML 视图构建失败 ({canvasPath}): {errorMessages}");
        }
        double elapsedMs = Stopwatch.GetElapsedTime(startTicks).TotalMilliseconds;
        if (elapsedMs > SlowItemLogThresholdMs)
        {
            Log.For(LogModule.Mcp).Warning($"Canvas HTML 视图构建缓慢: {canvasRelativePath}, 用时 {elapsedMs:F0}ms");
        }
        return builtState;
    }

    private (DeviceViewState? State, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) BuildDeviceViewCore(string devicePath)
    {
        string deviceRelativePath = WorkspacePath.RequireRelativePathOrEmpty(devicePath);

        DeviceTsView tool = _services.DeviceTsView;
        (DeviceTsViewReadData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = tool.Read(deviceRelativePath);
        if (data == null || errors.Count > 0)
        {
            return (null, errors, warnings);
        }

        string viewPath = $"{deviceRelativePath}.ts";
        string viewFileRelativePath = RelativePathUtils.Combine(_viewsRootRelativePath, viewPath);
        string tsView = RewriteTsViewReferencePaths(data.TsView, viewFileRelativePath);
        string hash = ComputeTextHash(tsView);

        string[] referenced = data.ReferencedDeviceRelativePaths
            .Select(p => WorkspacePath.RequireRelativePath(p))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        DeviceViewState state = new DeviceViewState(
            deviceRelativePath,
            viewPath,
            viewFileRelativePath,
            hash,
            tsView,
            referenced);

        return (state, errors, warnings);
    }

    /// <summary>
    /// rawHash 由调用方提前算好传入，避免重复读取 canvasEntity.Data。
    /// </summary>
    private (CanvasHtmlViewState? State, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) BuildCanvasHtmlViewCore(
        string canvasRelativePath,
        string rawHash)
    {
        CanvasHtmlView tool = _services.CanvasHtmlView;
        (CanvasHtmlViewReadData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = tool.Read(canvasRelativePath);
        if (data == null || errors.Count > 0)
        {
            return (null, errors, warnings);
        }

        string viewPath = GetCanvasViewPath(canvasRelativePath);
        string viewFileRelativePath = RelativePathUtils.Combine(_viewsRootRelativePath, viewPath);
        string contentHash = ComputeTextHash(data.Html);

        CanvasHtmlViewState state = new CanvasHtmlViewState(
            canvasRelativePath,
            viewPath,
            viewFileRelativePath,
            rawHash,
            contentHash,
            data.Html);

        return (state, errors, warnings);
    }

    #endregion

    #region PNG 预览生成辅助方法

    /// <summary>
    /// 生成所有 PNG：缓存命中项直接收集，未命中项通过 BrowserRenderService 多进程并行渲染。
    /// </summary>
    private List<(string Path, byte[] Data)> GeneratePngsConcurrent(
        List<(string Path, CanvasHtmlViewState State)> canvasViews,
        List<InlineCanvasPreviewState> inlineCanvasViews)
    {
        List<(string Path, byte[] Data)> results = [];
        int width = _services.CanvasPreviewSettings.DefaultWidth;
        int height = _services.CanvasPreviewSettings.DefaultHeight;

        // 串行分拣：缓存命中项直接收集，未命中项加入待渲染列表
        List<(string ViewPath, string Hash, string Html)> toRender = [];

        foreach ((string _, CanvasHtmlViewState state) in canvasViews)
            CheckCache(state.ViewPath, state.ContentHash, state.Html);
        foreach (InlineCanvasPreviewState state in inlineCanvasViews)
            CheckCache(state.ViewPath, state.SourceHash, state.Html);

        void CheckCache(string viewPath, string hash, string html)
        {
            byte[]? cached = GetCachedPng(viewPath, hash);
            if (cached != null)
            {
                string pngPath = $"{viewPath}.png";
                if (viewPath.EndsWith(".html", StringComparison.Ordinal))
                    pngPath = viewPath[..^".html".Length] + ".png";
                results.Add((pngPath, cached));
                return;
            }
            toRender.Add((viewPath, hash, html));
        }

        if (toRender.Count == 0)
            return results;

        // 预构建共享只读数据
        ColorPalette palette = _services.Logic.GetColorPalette();
        IReadOnlyDictionary<string, PaletteColor> paletteColors = palette.GetAllColors();
        Core.Canvas.Render.CanvasPreviewSettings settings = _services.CanvasPreviewSettings;
        BrowserRenderService browser = _services.Browser;
        ConcurrentBag<(string Path, byte[] Data)> rendered = [];

        // 多进程并行渲染：每个 worker 是独立的浏览器子进程
        Parallel.ForEach(toRender, new ParallelOptions { MaxDegreeOfParallelism = browser.Parallelism }, item =>
        {
            string bodyHtml = CanvasHtmlViewCodec.ExtractCanvasRootFragment(item.Html);
            string selfContainedHtml = CanvasHtmlViewAssets.BuildSelfContainedHtml(
                bodyHtml,
                paletteColors,
                settings,
                _model);

            long renderStartTicks = Stopwatch.GetTimestamp();
            byte[] pngData = browser.RenderHtmlToPng(selfContainedHtml, width, height);
            double renderElapsedMs = Stopwatch.GetElapsedTime(renderStartTicks).TotalMilliseconds;
            if (renderElapsedMs > SlowItemLogThresholdMs)
            {
                Log.For(LogModule.Mcp).Warning($"Canvas PNG 渲染缓慢: {item.ViewPath}, 用时 {renderElapsedMs:F0}ms");
            }
            lock (_lock)
            {
                _pngCache[item.ViewPath] = new PngCacheEntry(item.Hash, pngData);
            }
            string pngPath = $"{item.ViewPath}.png";
            if (item.ViewPath.EndsWith(".html", StringComparison.Ordinal))
                pngPath = item.ViewPath[..^".html".Length] + ".png";
            rendered.Add((pngPath, pngData));
        });

        results.AddRange(rendered);
        return results;
    }

    private byte[]? GetCachedPng(string viewPath, string currentSourceHash)
    {
        lock (_lock)
        {
            if (!_pngCache.ContainsKey(viewPath))
                return null;

            PngCacheEntry entry = _pngCache[viewPath];
            if (!string.Equals(entry.SourceHash, currentSourceHash, StringComparison.Ordinal))
                return null;

            return entry.PngData;
        }
    }

    #endregion

    #region 产物缓存（dts / CodeRepo.ts 按逻辑内容指纹）

    /// <summary>
    /// custom-logic.d.ts 按逻辑内容指纹缓存：指纹不变则产物不变，跳过 TsCompilerHost。
    /// 锁内查命中，未命中则锁外生成（避免长持锁），再锁内写入。
    /// 构建期间模型被并发改动（前后指纹不一致）则产物来源混杂，不写缓存，下轮重建。
    /// </summary>
    private string GetOrBuildCustomLogicDts()
    {
        string fingerprint = _services.Logic.ComputeLogicFingerprint();
        lock (_lock)
        {
            if (_customLogicDtsByFingerprint.ContainsKey(fingerprint))
                return _customLogicDtsByFingerprint[fingerprint];
        }

        string built = CustomLogicTsViewDeclarationLibrary.GetDtsText(_services.Model, _services.Logic);

        if (string.Equals(fingerprint, _services.Logic.ComputeLogicFingerprint(), StringComparison.Ordinal))
        {
            lock (_lock)
            {
                _customLogicDtsByFingerprint[fingerprint] = built;
            }
        }
        return built;
    }

    /// <summary>
    /// CodeRepo.ts 按逻辑内容指纹缓存。命中跳过整个 Read（含 TsCompilerHost 生成）。
    /// 并发一致性策略同 <see cref="GetOrBuildCustomLogicDts"/>。
    /// </summary>
    private CodeRepoTsProduct GetOrBuildCodeRepoTs()
    {
        string fingerprint = _services.Logic.ComputeLogicFingerprint();
        lock (_lock)
        {
            if (_codeRepoTsByFingerprint.ContainsKey(fingerprint))
                return _codeRepoTsByFingerprint[fingerprint];
        }

        CodeRepoTsView codeRepoTool = _services.CodeRepoTsView;
        (CodeRepoTsViewReadData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = codeRepoTool.Read(_services.Model);
        CodeRepoTsProduct product = new(data?.TsView, data is not null && errors.Count == 0);

        if (string.Equals(fingerprint, _services.Logic.ComputeLogicFingerprint(), StringComparison.Ordinal))
        {
            lock (_lock)
            {
                _codeRepoTsByFingerprint[fingerprint] = product;
            }
        }
        return product;
    }

    private static void PruneFingerprintKeysLocked<T>(Dictionary<string, T> cache, string keepFingerprint)
    {
        List<string>? stale = null;
        foreach (string k in cache.Keys)
        {
            if (k != keepFingerprint)
                (stale ??= new List<string>()).Add(k);
        }
        if (stale is not null)
        {
            foreach (string k in stale)
                cache.Remove(k);
        }
    }

    #endregion

    #region 依赖追踪

    private void AddReverseRefsLocked(string devicePath, string[] referenced)
    {
        foreach (string dep in referenced)
        {
            if (!_deviceReverseRefs.ContainsKey(dep))
            {
                _deviceReverseRefs[dep] = new HashSet<string>(StringComparer.Ordinal);
            }
            _deviceReverseRefs[dep].Add(devicePath);
        }
    }

    private void RemoveReverseRefsLocked(string devicePath, string[] referenced)
    {
        foreach (string dep in referenced)
        {
            if (!_deviceReverseRefs.ContainsKey(dep)) continue;

            HashSet<string> set = _deviceReverseRefs[dep];
            set.Remove(devicePath);
            if (set.Count == 0)
            {
                _deviceReverseRefs.Remove(dep);
            }
        }
    }

    #endregion


    private const string BescriptTsConfigFileName = "tsconfig.json";

    private const string BescriptTsConfigContent = @"{
  ""compilerOptions"": {
    ""target"": ""ES2015"",
    ""module"": ""None"",
    ""ignoreDeprecations"": ""6.0"",
    ""noLib"": true,
    ""strict"": true,
    ""noEmit"": true,
    ""skipLibCheck"": false,
    ""experimentalDecorators"": true,
    ""noImplicitAny"": true,
    ""strictNullChecks"": true,
    ""noUnusedLocals"": false,
    ""noUnusedParameters"": false,
    ""allowUnreachableCode"": true
  },
  ""include"": [
    ""views/**/*.ts"",
    ""bescript_reference/**/*.ts"",
    ""bescript_reference/**/*.d.ts""
  ],
  ""exclude"": [
    ""canvas_render/**/*""
  ]
}";

    #region 路径处理

    private string GetCanvasViewPath(string canvasRelativePath)
    {
        string normalized = WorkspacePath.RequireRelativePath(canvasRelativePath);
        if (!normalized.EndsWith(ProjectEntityLayout.CanvasExt, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Canvas 路径必须以 {ProjectEntityLayout.CanvasExt} 结尾: {normalized}");
        }

        string viewRelativePath = normalized[..^ProjectEntityLayout.CanvasExt.Length];
#pragma warning disable TK008 // 边界：路径格式验证
        if (viewRelativePath.Length == 0)
        {
            throw new InvalidOperationException($"Canvas 路径无效: {normalized}");
        }
#pragma warning restore TK008

        return $"{viewRelativePath}.html";
    }

#pragma warning disable TK006 // 引用路径重写
    private string RewriteTsViewReferencePaths(string tsView, string viewFileRelativePath)
#pragma warning restore TK006
    {
        return TsViewSupportReferenceRewriter.Rewrite(
            tsView,
            viewFileRelativePath,
            ScriptPathHelper.ViewsPath,
            ScriptPathHelper.BescriptReferenceRootPath);
    }


    #endregion

    #region 枚举与工具

#pragma warning disable TK006 // 枚举方法，与 EnumerateCanvasPaths 对称
    private IEnumerable<string> EnumerateDevicePaths()
#pragma warning restore TK006
    {
        foreach (DeviceEntity device in _model.ListDevices().OrderBy(d => d.device目录_完整相对路径, StringComparer.Ordinal))
        {
            yield return WorkspacePath.RequireRelativePath(device.device目录_完整相对路径);
        }
    }

#pragma warning disable TK006 // 枚举方法，与 EnumerateDevicePaths 对称
    private IEnumerable<string> EnumerateCanvasPaths()
#pragma warning restore TK006
    {
        foreach (CanvasEntity canvas in _model.ListCanvases().OrderBy(c => c.实体_完整相对路径标识, StringComparer.Ordinal))
        {
            yield return WorkspacePath.RequireRelativePath(canvas.实体_完整相对路径标识);
        }
    }

    /// <summary>
    /// 收集内联 Canvas 预览 HTML。双重 hash 短路：
    /// Layer 1（快速）：脚本 SourceText hash 未变 → 整条脚本的全部内联 canvas 直接复用。
    /// Layer 2（精准）：单个 arg.DecodedText hash 未变 → 跳过 FromInline + ToHtml。
    /// 仅在参数语法不匹配时跳过；命中 Canvas 参数但 Base64 非法时直接抛异常。
    /// </summary>
    private List<InlineCanvasPreviewState> CollectInlineCanvasPreviewHtmls()
    {
        List<InlineCanvasPreviewState> previews = [];

        foreach (InlineScriptEntity script in _model.List<InlineScriptEntity>())
            CollectInlineCanvasFromScript(script.实体_完整相对路径标识, script.SourceText, previews);
        foreach (CodeRepoScriptEntity script in _model.List<CodeRepoScriptEntity>())
            CollectInlineCanvasFromScript(script.实体_完整相对路径标识, script.SourceText, previews);

        return previews;
    }

    private void CollectInlineCanvasFromScript(
        string scriptRelativePath,
        string sourceText,
        List<InlineCanvasPreviewState> previews)
    {
        string scriptHash = ComputeTextHash(sourceText);
        lock (_lock)
        {
            if (_scriptInlineCache.ContainsKey(scriptRelativePath))
            {
                ScriptInlineCacheEntry cached = _scriptInlineCache[scriptRelativePath];
                if (string.Equals(cached.ScriptHash, scriptHash, StringComparison.Ordinal))
                {
                    previews.AddRange(cached.Previews);
                    return;
                }
            }
        }

        (BescriptParseData? parseData, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) =
            BescriptParser.Parse(scriptRelativePath, sourceText);
        if (parseData?.Ast == null)
        {
            if (errors.Count > 0)
            {
                string errorMessages = string.Join("; ", errors.Select(error => error.Message));
                throw new InvalidOperationException($"脚本解析失败: {scriptRelativePath}: {errorMessages}");
            }
            return;
        }

        List<InlineCanvasPreviewState> scriptPreviews = [];
        string scriptDir = RelativePathUtils.GetParentDirectory(scriptRelativePath);
        string scriptName = RelativePathUtils.GetLastSegmentWithoutExtension(scriptRelativePath);
        int inlineIndex = 0;

        void CollectFromNode(BescriptAstNode node)
        {
            if (node is IHasCall hasCall)
            {
                foreach (BescriptCallArgument arg in hasCall.Call.Arguments)
                {
                    if (arg.IsVariable || !string.Equals(arg.TypeName, "Canvas", StringComparison.Ordinal))
                        continue;
                    if (CanvasInlineArg.Parse(arg.DecodedText) is null)
                        continue;

                    string viewPath = $"{scriptDir}/{scriptName}_inline_{inlineIndex}.html";
                    string sourceHash = ComputeTextHash(arg.DecodedText);

                    // Layer 2: 单个内联 canvas 的 arg hash 精准短路
                    lock (_lock)
                    {
                        if (_scriptInlineCache.ContainsKey(scriptRelativePath))
                        {
                            ScriptInlineCacheEntry oldEntry = _scriptInlineCache[scriptRelativePath];
                            InlineCanvasPreviewState? hit = oldEntry.Previews
                                .FirstOrDefault(p => string.Equals(p.ViewPath, viewPath, StringComparison.Ordinal)
                                    && string.Equals(p.SourceHash, sourceHash, StringComparison.Ordinal));
                            if (hit != null)
                            {
                                scriptPreviews.Add(hit);
                                inlineIndex++;
                                continue;
                            }
                        }
                    }

                    CanvasNodeResult canvasResult = CanvasConverter.FromInline(arg.DecodedText);
                    if (!canvasResult.Ok || canvasResult.Root == null)
                    {
                        throw new InvalidOperationException(
                            $"内联 canvas 解析失败: script={scriptRelativePath}, index={inlineIndex}, ok={canvasResult.Ok}, diagnostics={canvasResult.Diagnostics.Count}");
                    }

                    string supportPrefix = CanvasHtmlRelativePath.BuildRelativePrefix(
                        _viewsRootRelativePath,
                        ScriptPathHelper.CanvasRenderRootPath);
#pragma warning disable TK008 // 边界：解析结果 CanvasName 可能为空
                    string canvasName = canvasResult.CanvasName;
                    if (canvasName.Length == 0)
                        canvasName = "InlineCanvas";
#pragma warning restore TK008
                    string html = CanvasConverter.ToHtml(canvasResult.Root, canvasName, supportPrefix);
                    scriptPreviews.Add(new InlineCanvasPreviewState(viewPath, sourceHash, html));
                    inlineIndex++;
                }
            }

            foreach (BescriptAstNode child in node.Children)
                CollectFromNode(child);
        }

        foreach (BescriptAstNode root in parseData.Ast.RootNodes)
            CollectFromNode(root);

        // 立即更新按脚本的缓存（Layer 1 的数据源）
        lock (_lock)
        {
            _scriptInlineCache[scriptRelativePath] = new ScriptInlineCacheEntry(scriptHash, scriptPreviews);
        }

        previews.AddRange(scriptPreviews);
    }

    private (string Text, IReadOnlyList<ToolError> Errors) ReadCurrentViewText(string viewPath)
    {
        if (viewPath.EndsWith(".ts", StringComparison.Ordinal))
        {
            (string text, IReadOnlyList<ToolError> errors, _) = ReadDeviceView(viewPath[..^".ts".Length], forPatch: true);
            if (errors.Count > 0)
            {
                return (string.Empty, errors);
            }

            return (text, Array.Empty<ToolError>());
        }

        (string html, IReadOnlyList<ToolError> htmlErrors, _) = ReadCanvasHtmlView(viewPath[..^".html".Length] + ".canvas", forPatch: true);
        if (htmlErrors.Count > 0)
        {
            return (string.Empty, htmlErrors);
        }

        return (html, Array.Empty<ToolError>());
    }

    private static (bool Ok, string BaselineHash, DateTime CreatedUtc, string DraftContent, string Message) ParseViewDraftFile(string draftText)
    {
        try
        {
            ViewDraftFile? draft = JsonSerializer.Deserialize<ViewDraftFile>(draftText);
            if (draft is null || draft.BaselineHash is null || draft.DraftContent is null)
                return (false, string.Empty, DateTime.MinValue, string.Empty, "草稿 JSON 结构无效");
#pragma warning disable TK008 // 草稿文件是 MCP 写盘边界，空字段代表损坏 JSON
            if (draft.BaselineHash.Length == 0)
                return (false, string.Empty, DateTime.MinValue, string.Empty, "草稿缺少 baselineHash");
            if (draft.CreatedUtc == DateTime.MinValue)
                return (false, string.Empty, DateTime.MinValue, string.Empty, "草稿缺少 createdUtc");
            if (draft.DraftContent.Length == 0)
                return (false, string.Empty, DateTime.MinValue, string.Empty, "草稿内容为空");
#pragma warning restore TK008
            return (true, draft.BaselineHash, draft.CreatedUtc, draft.DraftContent, string.Empty);
        }
        catch (JsonException)
        {
            return (false, string.Empty, DateTime.MinValue, string.Empty, "草稿不是有效 JSON");
        }
    }

    private sealed record ViewDraftFile(string BaselineHash, DateTime CreatedUtc, string DraftContent);

    /// <summary>
    /// 对内存中的文本做 SHA256（Layer 2 用，对象是转换产物而非原始文件）。
    /// </summary>
    private static string ComputeTextHash(string text)
    {
        byte[] bytes = TextEncoding.Utf8.GetBytes(text);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    /// <summary>
    /// 清理 _scriptInlineCache 中已不存在的脚本条目，连带清理其 PNG 缓存。
    /// 调用方已持有 _lock。
    /// </summary>
    private void RemoveStaleScriptInlineCacheLocked()
    {
        HashSet<string> activeScripts = _model.List<InlineScriptEntity>()
            .Select(s => s.实体_完整相对路径标识)
            .Concat(_model.List<CodeRepoScriptEntity>().Select(s => s.实体_完整相对路径标识))
            .ToHashSet(StringComparer.Ordinal);

        string[] staleScripts = _scriptInlineCache.Keys
            .Where(path => !activeScripts.Contains(path))
            .ToArray();
        foreach (string staleScript in staleScripts)
        {
            foreach (InlineCanvasPreviewState preview in _scriptInlineCache[staleScript].Previews)
                _pngCache.Remove(preview.ViewPath);
            _scriptInlineCache.Remove(staleScript);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(McpViewWorkspace));
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            // 取消订阅模型刷新事件，阻止新的重建请求
            _model.ModelRefreshed -= OnModelRefreshed;
            _model.FileChangeDetected -= OnModelFileChangeDetected;
            _model.WorkspaceBlockStateChanged -= OnWorkspaceBlockStateChanged;

            // 等待正在运行的重建循环退出
            while (_isRebuildingViews)
                Monitor.Wait(_lock);

            _deviceViews.Clear();
            _canvasViews.Clear();
            _deviceReverseRefs.Clear();
            _scriptInlineCache.Clear();
            _pngCache.Clear();
            _customLogicDtsByFingerprint.Clear();
            _codeRepoTsByFingerprint.Clear();
        }
    }

    #endregion
}
