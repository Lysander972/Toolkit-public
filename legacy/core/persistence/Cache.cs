using System.Collections.Concurrent;
using System.Security.Cryptography;
using Toolkit.Core.Infra;

// FileTreeSnapshot 和 FileTreeNode 现在位于 Toolkit.Core 命名空间（FileTreeSnapshot.cs）



namespace Toolkit.Core.Persistence;

// =============================================================================
// 文件系统缓存层 - 统一文件/目录缓存
// =============================================================================
//
// 这是文件系统缓存的唯一入口。禁止在其他地方重复缓存文件原始数据！
//
// 职责：
// 1. 缓存文件原始内容（字节）
// 2. 缓存目录扫描结果
// 3. 维护文件树快照（供实体构建使用）
// 4. 通过全局 FileSystemWatcher 监听变更并通知上层
//
// 分层规则：
// - 文件原始数据只能在此层缓存
// - 上层如需文本内容，可在此层获取
// - 上层禁止重复缓存文件原始字节
//
// 【架构铁律】单向流动 + 整体刷新，绝对禁止外部单点淘汰！
// - 文件监听器事件、本地变更结束、受阻入口只能 ScheduleRefresh，统一交给计时器处理
// - 禁止公开 RefreshStableSnapshotNow / WaitForStableSnapshot / Invalidate* 这类分裂入口
// - 禁止任何外部代码手动篡改污染缓存
// - 禁止任何"贴心"的局部失效包装方法（如 EvictFileCache）
// - 禁止在写入路径上夹带缓存失效调用——写入和缓存失效分属不同层，严禁耦合
// - 唯一合法的缓存失效路径：ProcessPendingInvalidations 防抖后的整体刷新
// - 违反此铁律的代码一律视为 GPT 幻觉垃圾，必须铲除！
//
// =============================================================================

/// <summary>
/// 文件系统缓存 - 统一的文件/目录缓存层。
///
/// 这是文件原始数据的唯一缓存位置。禁止在其他地方重复缓存文件内容！
/// </summary>
public sealed class FileSystemCache
{
    #region Static Instance Management

    private static FileSystemCache? _instance;
    private static readonly object InitLock = new();

    /// <summary>
    /// 稳定文件树快照刷新完成事件。
    /// </summary>
    internal static event EventHandler<FileTreeChangedEventArgs>? FileTreeChanged;

    /// <summary>
    /// 工作区阻塞状态变更事件。
    /// </summary>
    public static event EventHandler<WorkspaceBlockStateChangedEventArgs>? WorkspaceBlockStateChanged;

    /// <summary>
    /// 获取缓存实例（必须先调用 Initialize）
    /// </summary>
    public static FileSystemCache Instance
    {
        get
        {
            if (_instance == null)
            {
                throw new InvalidOperationException("FileSystemCache 未初始化，必须先调用 Initialize()");
            }
            return _instance;
        }
    }

    public static bool ExecuteIfInitialized(Action<FileSystemCache> action)
    {
        lock (InitLock)
        {
            if (_instance == null)
            {
                return false;
            }

            action(_instance);
            return true;
        }
    }

    /// <summary>
    /// 初始化缓存系统（设置工程根目录）
    /// </summary>
    private static bool _processExitHooked;

    public static void Initialize(string projectRootFullPath)
        => CreateInstance(projectRootFullPath, RealFileIdentity.Instance);

    /// <summary>
    /// 测试注入:用自定义 IFileIdentity(模拟链接归并)。生产请用 Initialize。
    /// </summary>
    internal static void InitializeForTesting(string projectRootFullPath, IFileIdentity fileIdentity)
        => CreateInstance(projectRootFullPath, fileIdentity);

    private static void CreateInstance(string projectRootFullPath, IFileIdentity fileIdentity)
    {
        lock (InitLock)
        {
            _instance?.Dispose();
            _instance = new FileSystemCache(projectRootFullPath, fileIdentity);

            if (!_processExitHooked)
            {
                _processExitHooked = true;
                AppDomain.CurrentDomain.ProcessExit += static (_, _) => Shutdown();
            }
        }
    }

    /// <summary>
    /// 释放所有资源（用于测试或进程退出）
    /// </summary>
    public static void Shutdown()
    {
        lock (InitLock)
        {
            _instance?.Dispose();
            _instance = null;
            FileTreeChanged = null;
            WorkspaceBlockStateChanged = null;
        }
    }

    #endregion

    // =========================================================================
    // 实例成员
    // =========================================================================
    #region Watcher

    private readonly string _projectRootFullPath;
    private readonly IFileIdentity _fileIdentity;

    public string ProjectRootFullPath => _projectRootFullPath;

    // 文件内容缓存：相对路径 -> 缓存条目
    private readonly ConcurrentDictionary<string, FileContentEntry> _fileCache;

    // 目录扫描缓存："cacheKey|relativePath|pattern" -> 缓存条目
    private readonly ConcurrentDictionary<string, DirectoryScanEntry> _directoryCache;

    // 写入锁：相对路径 -> 锁对象
    private readonly ConcurrentDictionary<string, object> _writeLocks;

    // 全局文件树监听器（监听整个工程根目录）
    private FileSystemWatcher? _globalWatcher;
    private bool _isDisposed;

    private readonly object _snapshotRefreshLock = new();
    private FileTreeSnapshot _stableSnapshot = new() { Root = new FileTreeNode(WorkspacePath.RootDirectory, string.Empty, 0) };
    private bool _hasStableSnapshot;
    private InMemoryWorkspaceOverlay? _overlay;

    // 统一计时器 —— 唯一计时器入口。
    // 同时驱动：防抖刷新（DebounceMs 无事件后整体刷新）+ 受阻轮询（BlockedProbeIntervalMs 间隔探测）。
    // 绝对禁止在此计时器之外再引入任何其他计时器分裂调度路径！
    private System.Threading.Timer? _unifiedTimer;
    private long _lastFileSystemEventTicks;

    private readonly object _debounceLock = new();
    private readonly SortedSet<string> _blockedRelativePaths = new(StringComparer.Ordinal);
    // 非关键占据容错路径：参与轮询探测与解除后重扫，但不触发全局受阻状态
    private readonly SortedSet<string> _toleratedRelativePaths = new(StringComparer.Ordinal);
    // 容错恢复待执行标志（EnterToleratedPath 置位，全部解除后消费执行一次快照重扫）
    private int _toleratedRecoveryPending;
    private int _blockedProbeRunning;
    // 刷新处理防重入：ScheduleRefresh→RescheduleUnifiedTimer 可能在上一轮刷新仍在执行时再次触发计时器，
    // 无守卫时多个 tick 并发执行全量扫描并排队模型写锁——线程堆积、读写锁护送效应饿死读方（曾观测 30+ 线程卡死）。
    private int _refreshProcessing;
    private bool _isWorkspaceBlocked;
    private string _workspaceBlockedMessage = WorkspaceFileBlockedException.DefaultBlockMessage;
    private bool _refreshPending;
    private const int DebounceMs = 200;
    private const int BlockedProbeIntervalMs = 1000;
    // 空闲心跳间隔：FileSystemWatcher 漏掉的外部变化在此周期内被全量重扫 + diff 自愈。
    private const int HeartbeatMs = 30_000;

    // 待失效路径集合（用于批量细粒度失效）
    private readonly HashSet<string> _pendingInvalidatePaths = new(StringComparer.Ordinal);

    private FileSystemCache(string projectRootFullPath, IFileIdentity fileIdentity)
    {
        _projectRootFullPath = WorkspacePath.ToUnixSeparator(projectRootFullPath);
        _fileIdentity = fileIdentity;
        _fileCache = new ConcurrentDictionary<string, FileContentEntry>(StringComparer.Ordinal);
        _directoryCache = new ConcurrentDictionary<string, DirectoryScanEntry>(StringComparer.Ordinal);
        _writeLocks = new ConcurrentDictionary<string, object>(StringComparer.Ordinal);
        _unifiedTimer = new System.Threading.Timer(
            OnUnifiedTimerTick,
            null,
            System.Threading.Timeout.Infinite,
            System.Threading.Timeout.Infinite);
    }

    internal void AttachOverlay(InMemoryWorkspaceOverlay overlay)
    {
        FileTreeSnapshot snapshot = GetFileTreeSnapshot(WorkspacePath.RootDirectory);
        overlay.InitializeFromSnapshot(snapshot);
        lock (_snapshotRefreshLock)
        {
            if (_globalWatcher != null)
            {
                _globalWatcher.EnableRaisingEvents = false;
                _globalWatcher.Dispose();
                _globalWatcher = null;
            }
            _overlay = overlay;
            _stableSnapshot = overlay.GetFileTreeSnapshot(WorkspacePath.RootDirectory);
            _hasStableSnapshot = true;
        }
    }

    internal bool TryApplyOverlayOperation(AtomicOperation operation)
    {
        InMemoryWorkspaceOverlay? overlay = _overlay;
        if (overlay == null)
        {
            return false;
        }

        overlay.Apply([operation]);
        NotifyLocalMutationCompleted();
        return true;
    }

    /// <summary>
    /// 初始化全局文件系统监听器
    /// </summary>
    private void InitializeGlobalWatcher()
    {
        // 覆盖前 Dispose 旧实例（半死/残留），收口 FileSystemWatcher 资源释放——
        // GC 句柄 + IO 完成端口注册不随引用覆盖自动回收。半死实例（new 成功但 EnableRaisingEvents
        // 失败）被 IsGlobalWatcherAlive 兜底重建覆盖时，旧实例必须显式释放，否则泄露。Dispose 幂等。
        _globalWatcher?.Dispose();
        _globalWatcher = new FileSystemWatcher(_projectRootFullPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                           | NotifyFilters.DirectoryName
                           | NotifyFilters.LastWrite
                           | NotifyFilters.Size
                           | NotifyFilters.CreationTime,
            // 默认 4-8KB 缓冲区在批量移入时易溢出丢事件，加大到 64KB（Microsoft 推荐值）。
            // 仅降低 InternalBufferOverflowException 概率；彻底兜底依赖空闲心跳。
            InternalBufferSize = 64 * 1024
        };

        _globalWatcher.Changed += OnFileSystemChanged;
        _globalWatcher.Created += OnFileSystemChanged;
        _globalWatcher.Deleted += OnFileSystemChanged;
        _globalWatcher.Renamed += OnFileSystemChanged;
        _globalWatcher.Error += OnWatcherError;

        _globalWatcher.EnableRaisingEvents = true;
        Log.For(LogModule.Core).Info($"全局文件监听器已启动: {_projectRootFullPath}");
    }

    /// <summary>
    /// 文件系统变更回调（带防抖）
    /// </summary>
    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        if (_isDisposed) return;
        try
        {
            string relativePath = ConvertToRelativePath(e.FullPath);
            if (WorkspacePath.IsIgnoredFileName(RelativePathUtils.GetLastSegment(relativePath)))
            {
                return;
            }
            if (_fileIdentity.IsReparsePoint(e.FullPath))
            {
                return; // 链接(symlink/junction)变更不纳入文件树
            }
            lock (_debounceLock)
            {
                _pendingInvalidatePaths.Add(relativePath);
            }
            ScheduleRefresh();
        }
        catch (ArgumentException)
        {
            // 路径不在工程根目录内，忽略
        }
    }

    /// <summary>
    /// 记录文件系统事件时间并确保统一计时器按需排期。
    /// 受阻期间同样记录事件 —— 受阻解除后统一计时器自然处理积压。
    /// </summary>
    private long ScheduleRefresh()
    {
        if (_isDisposed) return DateTime.UtcNow.Ticks;
        long eventTicks = DateTime.UtcNow.Ticks;
        Interlocked.Exchange(ref _lastFileSystemEventTicks, eventTicks);
        MarkRefreshPending();
        return eventTicks;
    }

    /// <summary>
    /// 仅置 pending 并重排计时器，不触碰事件时间戳。
    /// 供非文件事件来源（防重入反弹）使用：事件时间戳是快照覆盖判定的依据，伪造它会引发不必要的全量重扫。
    /// </summary>
    private void MarkRefreshPending()
    {
        if (_isDisposed) return;
        lock (_debounceLock)
        {
            _refreshPending = true;
        }
        RescheduleUnifiedTimer();
    }

    /// <summary>
    /// 【整体刷新唯一入口】防抖后处理待失效路径并触发稳定快照整体重建。
    /// 绝对禁止在此方法之外对 _fileCache / _directoryCache 做单点淘汰！
    /// 绝对禁止在任何写入路径上夹带私货调用 InvalidateFile/InvalidateDirectory！
    /// 缓存一致性由 EnsureFileCached 的 LastWriteTimeUtcTicks 自动保证，无需手动捅。
    /// </summary>
    private void ProcessPendingInvalidations(string source)
    {
        // 串行化：已有刷新在执行时，本轮只保持 pending 标记，工作由在跑的一轮自然覆盖。
        if (Interlocked.CompareExchange(ref _refreshProcessing, 1, 0) != 0)
        {
            MarkRefreshPending();
            return;
        }
        try
        {
            ProcessPendingInvalidationsCore(source);
        }
        finally
        {
            Interlocked.Exchange(ref _refreshProcessing, 0);
        }
    }

    private void ProcessPendingInvalidationsCore(string source)
    {
        List<string> pathsToInvalidate;

        lock (_debounceLock)
        {
            pathsToInvalidate = _pendingInvalidatePaths.ToList();
            _pendingInvalidatePaths.Clear();
            _refreshPending = false;
        }

        // 细粒度失效：清理具体文件的内容缓存和锁
        foreach (string path in pathsToInvalidate)
        {
            _fileCache.TryRemove(path, out _);
            _writeLocks.TryRemove(path, out _);
        }

        _directoryCache.Clear();

        Log.For(LogModule.Core).Debug($"文件系统缓存已失效（{pathsToInvalidate.Count} 文件），准备刷新稳定快照");

        // 当轮重试次数统一：乐观并发校验在 FSW 事件并发时偶发打断本轮，无论来源是空闲心跳还是
        // 文件事件，都需要当轮多次重试抓住瞬时稳定窗口完成自愈。原 source 区分（空闲心跳 4 / 文件事件 1）
        // 在失败循环中退化为单次（_refreshPending 永久 true → 永远走文件事件分支），违背自愈语义。
        const int maxRefreshAttempts = 4;
        for (int attempt = 0; attempt < maxRefreshAttempts; attempt++)
        {
            try
            {
                StableSnapshotRefresh refresh = RefreshStableSnapshot(source);
                // 快照覆盖判定：快照起点之前到达的事件已被本轮扫描完整覆盖，直接消化积压，
                // 禁止"扫描刚结束立刻又全扫一轮"的飞轮空转（多次事件最终只需一次稳定重建）。
                if (Interlocked.Read(ref _lastFileSystemEventTicks) <= refresh.SnapshotStartTicks)
                {
                    lock (_debounceLock)
                    {
                        _pendingInvalidatePaths.Clear();
                        _refreshPending = false;
                    }
                }
                try
                {
                    FileTreeChanged?.Invoke(null, new FileTreeChangedEventArgs(refresh.SnapshotStartTicks));
                }
                catch (Exception ex)
                {
                    Log.For(LogModule.Core).Error($"FileTreeChanged 事件处理异常: {ex.Message}");
                }
                return;
            }
            catch (WorkspaceFileBlockedException)
            {
                // EnterBlockedState 已记录受阻路径；本轮快照没有完成，不触发 FileTreeChanged。
                return;
            }
            catch (SnapshotRefreshInvalidatedException ex)
            {
                Log.For(LogModule.Core).Debug($"稳定文件树快照刷新被文件变化打断，已丢弃本轮临时数据: {ex.Message}");
                if (attempt + 1 < maxRefreshAttempts)
                {
                    System.Threading.Thread.Sleep(15); // 短暂退避，让并发文件变化沉淀后重试
                }
            }
        }

        // 不变量：重试全失败才落到此——仅 ScheduleRefresh 兜底，绝不广播 FileTreeChanged。
        // 广播只在上方循环内成功路径 return 前触发；失败不广播，保证消费者不收到未完成快照通知。
        // 该不变量由循环结构承载（无可行确定性持续触发源：未来时间戳已豁免、独占走 BlockedException）。
        ScheduleRefresh();
    }

    /// <summary>
    /// watcher 健康判定：非 null 且 EnableRaisingEvents（已启动监听）。
    /// disposed 实例访问 EnableRaisingEvents 抛 ObjectDisposedException，捕获后视为死亡。
    /// 用于 RefreshStableSnapshot 兜底重建——避免「非 null 即健康」假设让死亡残留（重建部分失败 /
    /// Dispose 后未置 null）永久不被恢复，FSW 静默死亡。
    /// </summary>
    private bool IsGlobalWatcherAlive()
    {
        if (_globalWatcher == null)
        {
            return false;
        }
        try
        {
            return _globalWatcher.EnableRaisingEvents;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    /// 监听器错误回调。
    /// 重建段纳入 _snapshotRefreshLock，与 RefreshStableSnapshot 的 watcher 重建互斥；
    /// 持锁后校验 sender 仍是当前 watcher，忽略泄露/已 Dispose 旧实例的残留回调，避免误 Dispose 活实例。
    /// </summary>
    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        // 工程根已删除：静默停止监听器，避免日志噪音。仅置 null，无重建竞态，无需持锁做 DirectoryExists。
        if (!SystemIOWrapper.DirectoryExists(_projectRootFullPath))
        {
            lock (_snapshotRefreshLock)
            {
                _globalWatcher?.Dispose();
                _globalWatcher = null;
            }
            return;
        }

        Log.For(LogModule.Core).Error($"文件监听器错误: {e.GetException().Message}");
        ScheduleRefresh();

        // 重建与 RefreshStableSnapshot 的 InitializeGlobalWatcher 互斥；
        // 持锁后确认 sender 仍是当前 watcher，过时 sender（泄露实例/dispose 后回调）直接忽略。
        lock (_snapshotRefreshLock)
        {
            if (!ReferenceEquals(sender, _globalWatcher))
            {
                return;
            }

            try
            {
                InitializeGlobalWatcher(); // 内部首行已 Dispose 旧 _globalWatcher，此处不重复释放
            }
            catch (Exception ex)
            {
                // 重建失败强制 Dispose + 置 null，让 RefreshStableSnapshot 健康探针兜底重建，
                // 杜绝半死实例（new 成功但 EnableRaisingEvents 失败）残留永久死亡。
                _globalWatcher.Dispose();
                _globalWatcher = null;
                Log.For(LogModule.Core).Error($"重建文件监听器失败: {ex.Message}");
            }
        }
    }

    #endregion

    #region Workspace Blocking

    public bool IsWorkspaceBlocked => _isWorkspaceBlocked;

    public WorkspaceBlockStateSnapshot GetWorkspaceBlockState()
    {
        lock (_blockedRelativePaths)
        {
            string message = "未阻塞";
            if (_isWorkspaceBlocked)
            {
                message = _workspaceBlockedMessage;
            }

            return new WorkspaceBlockStateSnapshot(
                _isWorkspaceBlocked,
                _blockedRelativePaths.ToArray(),
                message);
        }
    }

    // =========================================================================
    // 受阻状态管理
    //
    // 流程：文件/目录被外部进程占用（共享冲突）
    //   → EnterBlockedState：记录受阻路径，确保统一计时器运行，触发事件
    //   → ProcessBlockedFiles：统一计时器回调内逐路径探测是否解除
    //   → TryLeaveBlockedState：全部路径解除 → 刷新快照
    //
    // 受阻期间统一计时器持续排期（受阻轮询间隔），不触发防抖刷新。
    // 受阻解除后统一计时器在当次回调内继续处理积压的失效路径。
    // =========================================================================

    public void ReportBlockedException(WorkspaceFileBlockedException ex) => EnterBlockedState(ex);

    /// <summary>
    /// 记录非关键占据容错路径（快照用旧节点/占位继续）。
    /// 不触发全局受阻；参与统一计时器轮询探测，解除后重扫快照恢复真实状态。
    /// </summary>
    private void EnterToleratedPath(string relativePath)
    {
        string path = WorkspacePath.RequireRelativePath(relativePath);
        lock (_toleratedRelativePaths)
        {
            _toleratedRelativePaths.Add(path);
        }

        Interlocked.Exchange(ref _toleratedRecoveryPending, 1);
        RescheduleUnifiedTimer();
    }

    /// <summary>测试访问：当前容错路径集合（非关键占据恢复验证）。</summary>
    internal IReadOnlyList<string> GetToleratedRelativePaths()
    {
        lock (_toleratedRelativePaths)
        {
            return _toleratedRelativePaths.ToArray();
        }
    }

    /// <summary>
    /// 记录受阻路径，触发受阻状态。若刚从非受阻进入受阻则启动轮询计时器。
    /// </summary>
    private void EnterBlockedState(WorkspaceFileBlockedException ex)
    {
        bool enteredBlockedState = false;
        lock (_blockedRelativePaths)
        {
            _blockedRelativePaths.Add(ex.RelativePath);

            if (!_isWorkspaceBlocked)
            {
                _isWorkspaceBlocked = true;
                _workspaceBlockedMessage = ex.BlockMessage;
                enteredBlockedState = true;
            }
        }

        RescheduleUnifiedTimer();

        if (!enteredBlockedState) return;

        WorkspaceBlockStateSnapshot snapshot = GetWorkspaceBlockState();
        Log.For(LogModule.Core).Warning($"工作区进入阻塞状态，已知受阻路径: {string.Join(", ", snapshot.BlockedRelativePaths)}");
        WorkspaceBlockStateChanged?.Invoke(this, new WorkspaceBlockStateChangedEventArgs(snapshot));
    }

    private void ProcessBlockedFiles()
    {
        if (_isDisposed) return;
        if (Interlocked.CompareExchange(ref _blockedProbeRunning, 1, 0) != 0) return;

        try
        {
            // 依次探测：受阻路径（关键，触发全局受阻）→ 容错路径（非关键，解除后重扫恢复）
            while (true)
            {
                bool probedBlocked = ProbeNextPath(_blockedRelativePaths, isTolerated: false);
                if (_isDisposed) return;
                bool probedTolerated = ProbeNextPath(_toleratedRelativePaths, isTolerated: true);
                if (!probedBlocked && !probedTolerated)
                    break;
            }

            TryLeaveBlockedState();
            TryRecoverToleratedPaths();
        }
        finally
        {
            _blockedProbeRunning = 0;
        }
    }

    /// <summary>
    /// 探测指定集合中的下一个路径。集合空返回 false。
    /// 路径已可读 → 从集合移除并返回 true（继续探测下一个）。
    /// </summary>
    private bool ProbeNextPath(SortedSet<string> paths, bool isTolerated)
    {
        string path;
        lock (paths)
        {
            if (paths.Count == 0) return false;
            using SortedSet<string>.Enumerator enumerator = paths.GetEnumerator();
            enumerator.MoveNext();
            path = enumerator.Current;
        }

        try
        {
            ProbePathReadable(path);
        }
        catch (FileNotFoundException)
        {
            lock (paths) { paths.Remove(path); }
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            lock (paths) { paths.Remove(path); }
            return true;
        }
        catch (WorkspaceFileBlockedException)
        {
            // 仍被占据：停止本轮轮询，路径保留在集合中，下一 tick 继续探测
            return false;
        }

        lock (paths)
        {
            paths.Remove(path);
        }
        return true;
    }

    /// <summary>
    /// 容错路径全部解除后重扫快照，恢复被占据文件/目录的真实状态。
    /// 仅在有容错历史（EnterToleratedPath 曾发生）且集合已清空时执行一次。
    /// </summary>
    private void TryRecoverToleratedPaths()
    {
        lock (_toleratedRelativePaths)
        {
            if (_toleratedRelativePaths.Count > 0 || _isWorkspaceBlocked)
                return;
        }

        if (Interlocked.Exchange(ref _toleratedRecoveryPending, 0) == 0)
            return;

        try
        {
            StableSnapshotRefresh refresh = RefreshStableSnapshot("容错路径解除");
            try
            {
                FileTreeChanged?.Invoke(null, new FileTreeChangedEventArgs(refresh.SnapshotStartTicks));
            }
            catch (Exception ex)
            {
                Log.For(LogModule.Core).Error($"FileTreeChanged 事件处理异常: {ex.Message}");
            }
        }
        catch (WorkspaceFileBlockedException)
        {
            // 重扫期间重新进入受阻，由受阻状态机接管
        }
        catch (SnapshotRefreshInvalidatedException ex)
        {
            Log.For(LogModule.Core).Debug($"稳定文件树快照刷新被文件变化打断，已丢弃本轮临时数据: {ex.Message}");
            ScheduleRefresh();
        }
    }

    private void TryLeaveBlockedState()
    {
        lock (_blockedRelativePaths)
        {
            if (_blockedRelativePaths.Count > 0 || !_isWorkspaceBlocked) return;
            _isWorkspaceBlocked = false;
            _workspaceBlockedMessage = WorkspaceFileBlockedException.DefaultBlockMessage;
        }

        WorkspaceBlockStateSnapshot snapshot;
        try
        {
            StableSnapshotRefresh refresh = RefreshStableSnapshot("受阻解除");
            snapshot = GetWorkspaceBlockState();
            if (snapshot.IsBlocked)
            {
                return;
            }

            try
            {
                FileTreeChanged?.Invoke(null, new FileTreeChangedEventArgs(refresh.SnapshotStartTicks));
            }
            catch (Exception ex)
            {
                Log.For(LogModule.Core).Error($"FileTreeChanged 事件处理异常: {ex.Message}");
            }
        }
        catch (WorkspaceFileBlockedException)
        {
            return;
        }
        catch (SnapshotRefreshInvalidatedException ex)
        {
            Log.For(LogModule.Core).Debug($"稳定文件树快照刷新被文件变化打断，已丢弃本轮临时数据: {ex.Message}");
            ScheduleRefresh();
            snapshot = GetWorkspaceBlockState();
            if (snapshot.IsBlocked)
            {
                return;
            }
        }

        Log.For(LogModule.Core).Info("工作区已脱离阻塞状态");
        WorkspaceBlockStateChanged?.Invoke(this, new WorkspaceBlockStateChangedEventArgs(snapshot));
    }

    #endregion

    #region Timer/Debounce

    // =========================================================================
    // 统一计时器 —— 唯一计时器回调 + 按需排期
    // =========================================================================

    /// <summary>
    /// 统一计时器回调。
    /// 先处理受阻文件探测，若脱离受阻则继续处理积压的防抖刷新。
    /// </summary>
    private void OnUnifiedTimerTick(object? _)
    {
        if (_isDisposed) return;

        // 线程池 Timer 回调是系统边界:任何未预见异常绝不能逃逸,否则 .NET 会终止整个进程。
        // 已知受管异常(WorkspaceFileBlockedException/SnapshotRefreshInvalidatedException)已在上游分类处理,
        // 到此处的均为未分类异常,拦截后由下一轮计时器继续工作。
        try
        {
            // 受阻文件探测（ProcessBlockedFiles 内部 Interlocked 防重入）
            ProcessBlockedFiles();

            // 受阻期间不处理失效刷新，受阻解除后当次回调自然继续处理
            if (!_isWorkspaceBlocked)
            {
                bool hasPending;
                lock (_debounceLock)
                {
                    hasPending = _refreshPending;
                }
                if (hasPending)
                {
                    long now = DateTime.UtcNow.Ticks;
                    long lastEvent = Interlocked.Read(ref _lastFileSystemEventTicks);
                    long elapsedMs = (now - lastEvent) / TimeSpan.TicksPerMillisecond;
                    if (elapsedMs >= DebounceMs)
                    {
                        ProcessPendingInvalidations("文件变化事件");
                    }
                }
                else
                {
                    // 空闲心跳：FileSystemWatcher 可能漏掉外部变化（缓冲区溢出/静默停止），
                    // 无 pending 时主动走整体刷新唯一入口，全量重扫 + AnalyzeChanges diff 自愈。
                    ProcessPendingInvalidations("空闲心跳校验");
                }
            }
        }
        catch (Exception ex)
        {
            Log.For(LogModule.Core).Error($"统一计时器回调出现未预见异常,已拦截以防进程崩溃: {ex.Message}");
        }

        RescheduleUnifiedTimer();
    }

    /// <summary>
    /// 按当前状态重新排期统一计时器。
    /// 受阻 → 轮询间隔；有待失效且未受阻 → 防抖剩余时间；全空闲 → 心跳间隔（兜底 FSW 漏事件）。
    /// 防抖分支刷新间隔故意无上限：只要事件持续到来就反复重置防抖窗口，宁可延迟也不取半新半旧的快照。
    /// </summary>
    private void RescheduleUnifiedTimer()
    {
        if (_isDisposed) return;

        int nextDelayMs = System.Threading.Timeout.Infinite;

        if (_isWorkspaceBlocked)
        {
            nextDelayMs = BlockedProbeIntervalMs;
        }
        else
        {
            bool hasTolerated;
            lock (_toleratedRelativePaths)
            {
                hasTolerated = _toleratedRelativePaths.Count > 0;
            }

            if (hasTolerated)
            {
                // 容错路径存在：保持轮询间隔探测解除
                nextDelayMs = BlockedProbeIntervalMs;
            }
            else
            {
                bool hasPending;
                lock (_debounceLock)
                {
                    hasPending = _refreshPending;
                }
                if (hasPending)
                {
                    long now = DateTime.UtcNow.Ticks;
                    long lastEvent = Interlocked.Read(ref _lastFileSystemEventTicks);
                    long elapsedMs = (now - lastEvent) / TimeSpan.TicksPerMillisecond;
                    int remaining = DebounceMs - (int)elapsedMs;
                    nextDelayMs = Math.Max(0, remaining);
                }
                else
                {
                    // 全空闲：排一次心跳，兜底捕获 FileSystemWatcher 漏掉的外部变化。
                    nextDelayMs = HeartbeatMs;
                }
            }
        }

        _unifiedTimer?.Change(nextDelayMs, System.Threading.Timeout.Infinite);
    }

    #endregion

    private void ProbePathReadable(string relativePath)
    {
        string validatedPath = WorkspacePath.RequireRelativePath(relativePath);
        string fullPath = ResolveFullPath(validatedPath);
        if (SystemIOWrapper.FileExists(fullPath))
        {
            ProbeFileReadable(validatedPath);
            return;
        }

        if (!SystemIOWrapper.DirectoryExists(fullPath))
        {
            throw new FileNotFoundException($"受阻路径已不存在: {validatedPath}", validatedPath);
        }

        try
        {
            _ = SystemIOWrapper.GetFiles(fullPath, "*", SearchOption.TopDirectoryOnly);
            _ = SystemIOWrapper.GetDirectories(fullPath, "*", SearchOption.TopDirectoryOnly);
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
        {
            WorkspaceFileBlockedException blockedEx = new(
                validatedPath,
                $"目录正在被外部进程占用: {validatedPath}。{ex.Message}",
                ex);
            EnterBlockedState(blockedEx);
            throw blockedEx;
        }
        catch (UnauthorizedAccessException ex)
        {
            WorkspaceFileBlockedException blockedEx = new(
                validatedPath,
                $"目录访问被拒绝(可能被外部进程独占): {validatedPath}。{ex.Message}",
                ex);
            EnterBlockedState(blockedEx);
            throw blockedEx;
        }
    }

    public void Dispose()
    {
        _isDisposed = true;

        if (_globalWatcher != null)
        {
            _globalWatcher.EnableRaisingEvents = false;
            _globalWatcher.Dispose();
            _globalWatcher = null;
        }

        System.Threading.Timer? timer = _unifiedTimer;
        _unifiedTimer = null;
        timer?.Dispose();

        _fileCache.Clear();
        _directoryCache.Clear();
        _writeLocks.Clear();
        _overlay = null;
        _hasStableSnapshot = false;

        FileTreeBinding.Instance.Clear();
    }

    // =========================================================================
    // 路径处理
    // =========================================================================

    internal string ResolveFullPath(string relativePath)
    {
        string validatedPath = WorkspacePath.RequireRelativePath(relativePath);
        if (validatedPath == WorkspacePath.RootDirectory)
        {
            return _projectRootFullPath;
        }
        return $"{_projectRootFullPath}/{validatedPath}";
    }

    private string ConvertToRelativePath(string absolutePath)
    {
        string unixPath = WorkspacePath.ToUnixSeparator(absolutePath);
        string root = _projectRootFullPath;

        if (!unixPath.StartsWith(root + "/", StringComparison.Ordinal))
        {
            throw new ArgumentException("路径必须在工程根目录内", nameof(absolutePath));
        }

        string relative = unixPath[(root.Length + 1)..];
        return WorkspacePath.RequireRelativePath(relative);
    }

#pragma warning disable TK006 // 独立职责：路径校验+锁获取，与 GetReadLock 对称
    #region Write Lock

    private object GetWriteLock(string relativePath)
    {
        string validatedPath = WorkspacePath.RequireRelativePath(relativePath);
        return _writeLocks.GetOrAdd(validatedPath, static _ => new object());
    }
#pragma warning restore TK006

    #endregion

    #region Content Cache

    // =========================================================================
    // 文件内容缓存
    // =========================================================================

    /// <summary>
    /// 读取文件全部字节（带缓存）
    /// </summary>
    public byte[] ReadAllBytes(string relativePath)
    {
        InMemoryWorkspaceOverlay? overlay = _overlay;
        if (overlay != null)
        {
            return overlay.ReadAllBytes(relativePath);
        }

        object writeLock = GetWriteLock(relativePath);
        lock (writeLock)
        {
            FileContentEntry entry = EnsureFileCached(relativePath);
            if (!entry.Exists)
                throw new FileNotFoundException($"文件不存在: {relativePath}", relativePath);
            return entry.Bytes.ToArray(); // 返回副本防止外部修改
        }
    }

    /// <summary>
    /// 获取文件内容的 SHA256 哈希（带缓存，与 ReadAllBytes 共享同一缓存条目）。
    /// 返回 32 字节原始哈希。文件不存在时抛 FileNotFoundException。
    /// </summary>
    public byte[] GetFileHash(string relativePath)
    {
        object writeLock = GetWriteLock(relativePath);
        lock (writeLock)
        {
            FileContentEntry entry = EnsureFileCached(relativePath);
            if (!entry.Exists)
                throw new FileNotFoundException($"文件不存在: {relativePath}", relativePath);
            return entry.Hash;
        }
    }

    /// <summary>
    /// 读取文件文本（带缓存，铲除所有 CR）
    /// 内部固定使用 UTF-8 编码（自动检测 BOM）
    /// </summary>
    public string ReadAllText(string relativePath)
    {
        byte[] bytes = ReadAllBytes(relativePath);

        using MemoryStream ms = new(bytes, writable: false);
        using StreamReader reader = new(ms, TextEncoding.Utf8, detectEncodingFromByteOrderMarks: true);
        string text = reader.ReadToEnd();
        return TextLineEndings.ToLf(text);
    }

    /// <summary>
    /// 写入文件字节（更新缓存）
    /// </summary>
    public void WriteAllBytes(string relativePath, byte[] bytes)
    {
        if (TryApplyOverlayOperation(new AtomicOperation(AtomicOperationType.WriteFile, relativePath, bytes)))
        {
            return;
        }

        object writeLock = GetWriteLock(relativePath);
        bool changed = false;
        try
        {
            lock (writeLock)
            {
                string fullPath = ResolveFullPath(relativePath);
                byte[] desiredHash = SHA256.HashData(bytes);

                // 检查是否需要写入（通过哈希比对）
                FileContentEntry entry = _fileCache.GetOrAdd(relativePath, static _ => new FileContentEntry());
                if (entry.Exists && entry.Hash.AsSpan().SequenceEqual(desiredHash))
                {
                    // 缓存可能被外部工具改写而过期，跳过写入前必须先与磁盘同步一次。
                    entry = EnsureFileCached(relativePath);
                    if (entry.Exists && entry.Hash.AsSpan().SequenceEqual(desiredHash))
                    {
                        return; // 内容相同，跳过写入
                    }
                }

                // 写入文件（父目录创建由实体层负责，见 ProjectModel.SaveChanges）
                SystemIOWrapper.WriteAllBytes(fullPath, bytes);
                changed = true;

                // 更新缓存（使用 UtcNow.Ticks 避免文件系统时间戳精度问题）
                entry.Exists = true;
                entry.LastWriteTimeUtcTicks = DateTime.UtcNow.Ticks;
                entry.Bytes = bytes.ToArray(); // 存储副本
                entry.Hash = desiredHash;

            }
        }
        finally
        {
            if (changed)
            {
                NotifyLocalMutationCompleted();
            }
        }
    }

    /// <summary>
    /// 写入文件文本（统一使用 UTF-8 无 BOM + LF 换行）
    /// </summary>
    public void WriteAllText(string relativePath, string text)
    {
        byte[] bytes = TextEncoding.GetBytesLfOnly(text);
        WriteAllBytes(relativePath, bytes);
    }

    /// <summary>
    /// 获取文件缓存条目（如果不存在或过期则重新加载）。
    /// 通过 LastWriteTimeUtcTicks 对比自动检测外部变更，无需外部手动失效缓存。
    /// 这是缓存层自我纠正的唯一机制——禁止绕过此方法直接操作 _fileCache。
    /// </summary>
    private FileContentEntry EnsureFileCached(string relativePath)
    {
        string fullPath = ResolveFullPath(relativePath);
        FileContentEntry entry = _fileCache.GetOrAdd(relativePath, static _ => new FileContentEntry());

        // 检查文件是否存在
        if (!SystemIOWrapper.FileExists(fullPath))
        {
            entry.Exists = false;
            entry.Bytes = Array.Empty<byte>();
            entry.Hash = Array.Empty<byte>();
            return entry;
        }

        // 检查缓存是否有效
        long lastWriteTicks;
        try
        {
            lastWriteTicks = SystemIOWrapper.GetFileLastWriteTimeUtc(fullPath).Ticks;
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
        {
            if (TryTolerateOccupiedRead(relativePath, entry))
                return entry;

            WorkspaceFileBlockedException blockedEx = new(
                relativePath,
                $"文件正在被外部进程占用: {relativePath}。{ex.Message}",
                ex);
            EnterBlockedState(blockedEx);
            throw blockedEx;
        }
        if (entry.Exists && entry.LastWriteTimeUtcTicks == lastWriteTicks)
        {
            // 缓存命中 — 仍需探测文件锁（外部进程占用时 ReadAllBytes 语义必须保持一致）
            try
            {
                using FileStream _ = SystemIOWrapper.OpenFileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
            {
                if (TryTolerateOccupiedRead(relativePath, entry))
                    return entry;

                WorkspaceFileBlockedException blockedEx = new(
                    relativePath,
                    $"文件正在被外部进程占用: {relativePath}。{ex.Message}",
                    ex);
                EnterBlockedState(blockedEx);
                throw blockedEx;
            }
            return entry;
        }

        // 重新加载
        byte[] bytes;
        try
        {
            bytes = SystemIOWrapper.ReadAllBytes(fullPath);
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
        {
            if (TryTolerateOccupiedRead(relativePath, entry))
                return entry;

            WorkspaceFileBlockedException blockedEx = new(
                relativePath,
                $"文件正在被外部进程占用: {relativePath}。{ex.Message}",
                ex);
            EnterBlockedState(blockedEx);
            throw blockedEx;
        }
        entry.Exists = true;
        entry.LastWriteTimeUtcTicks = lastWriteTicks;
        entry.Bytes = bytes;
        entry.Hash = SHA256.HashData(bytes);

        return entry;
    }

    /// <summary>
    /// 非关键文件被外部占据时容错读取：已有缓存返回旧条目，无缓存返回"不存在"占位，
    /// 不进入受阻并记录容错路径（解除后重扫恢复）。关键文件返回 false（调用方维持现状受阻）。
    /// </summary>
    private bool TryTolerateOccupiedRead(string relativePath, FileContentEntry entry)
    {
        if (CriticalFilePolicy.IsCriticalPath(relativePath))
            return false;

        if (!entry.Exists)
        {
            entry.Exists = false;
            entry.Bytes = Array.Empty<byte>();
            entry.Hash = Array.Empty<byte>();
        }

        EnterToleratedPath(relativePath);
        return true;
    }

    #endregion

    #region Directory Cache

    // =========================================================================
    // 目录扫描缓存
    // =========================================================================

    /// <summary>
    /// 获取目录下的文件列表（带缓存）
    /// </summary>
    public IReadOnlyList<string> GetFiles(string relativeDir, string pattern = "*")
    {
        InMemoryWorkspaceOverlay? overlay = _overlay;
        if (overlay != null)
        {
            return overlay.GetFiles(relativeDir, pattern);
        }

        return GetDirectoryEntries(relativeDir, pattern, true);
    }

    /// <summary>
    /// 获取目录下的子目录列表（带缓存）
    /// </summary>
    public IReadOnlyList<string> GetDirectories(string relativeDir, string pattern = "*")
    {
        InMemoryWorkspaceOverlay? overlay = _overlay;
        if (overlay != null)
        {
            return overlay.GetDirectories(relativeDir, pattern);
        }

        return GetDirectoryEntries(relativeDir, pattern, false);
    }

    private IReadOnlyList<string> GetDirectoryEntries(string relativeDir, string pattern, bool files)
    {
        string validatedDir = WorkspacePath.RequireRelativePath(relativeDir);
        string cacheKey = $"entries|{validatedDir}|{pattern}|{(files ? "f" : "d")}";

        DirectoryScanEntry entry = _directoryCache.GetOrAdd(cacheKey, static _ => new DirectoryScanEntry { IsDirty = true });
        if (!entry.IsDirty)
        {
            return entry.Entries;
        }

        // 缓存未命中或已脏，重新扫描
        string fullPath = ResolveFullPath(validatedDir);
        if (!SystemIOWrapper.DirectoryExists(fullPath))
        {
            return Array.Empty<string>();
        }

        string[] entries;
        try
        {
            if (files)
            {
                entries = SystemIOWrapper.GetFiles(fullPath, pattern, SearchOption.TopDirectoryOnly)
                    .Select(ConvertToRelativePath)
                    .Where(p => !WorkspacePath.IsIgnoredFileName(RelativePathUtils.GetLastSegment(p))
                                && !_fileIdentity.IsReparsePoint(ResolveFullPath(p)))
                    .ToArray();
            }
            else
            {
                entries = SystemIOWrapper.GetDirectories(fullPath, pattern, SearchOption.TopDirectoryOnly)
                    .Select(ConvertToRelativePath)
                    .Where(p => !_fileIdentity.IsReparsePoint(ResolveFullPath(p)))
                    .ToArray();
            }
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
        {
            // 占据分级：非关键目录容错（返回旧缓存条目，无缓存返回空列表），不进入受阻。
            if (!CriticalFilePolicy.IsCriticalPath(validatedDir))
            {
                EnterToleratedPath(validatedDir);
                return entry.IsDirty ? Array.Empty<string>() : entry.Entries;
            }

            WorkspaceFileBlockedException blockedEx = new(
                validatedDir,
                $"目录正在被外部进程占用: {validatedDir}。{ex.Message}",
                ex);
            EnterBlockedState(blockedEx);
            throw blockedEx;
        }

        // 更新缓存
        DirectoryScanEntry newEntry = new()
        {
            Entries = entries,
            IsDirty = false
        };
        _directoryCache[cacheKey] = newEntry;

        return entries;
    }

    #endregion

    #region Snapshot

    // =========================================================================
    // 文件树快照（供实体构建使用）
    // =========================================================================

    /// <summary>
    /// 最大扫描深度
    /// </summary>
    public const int MaxTreeDepth = 50;

    /// <summary>
    /// 获取当前稳定文件树快照。
    /// 返回值可能整体落后于磁盘，但必须来自同一轮成功扫描，不允许半新半旧。
    /// </summary>
    public FileTreeSnapshot GetFileTreeSnapshot(string relativeRoot)
    {
        string validatedRoot = WorkspacePath.RequireRelativePath(relativeRoot);
        InMemoryWorkspaceOverlay? overlay = _overlay;
        if (overlay != null)
        {
            return overlay.GetFileTreeSnapshot(validatedRoot);
        }

        FileTreeSnapshot snapshot;
        if (_hasStableSnapshot)
        {
            snapshot = _stableSnapshot;
        }
        else
        {
            try
            {
                StableSnapshotRefresh refresh = RefreshStableSnapshot("首次读取稳定快照");
                snapshot = refresh.Snapshot;
            }
            catch (SnapshotRefreshInvalidatedException ex)
            {
                Log.For(LogModule.Core).Debug($"首次稳定文件树快照刷新被文件变化打断，已丢弃本轮临时数据: {ex.Message}");
                ScheduleRefresh();
                snapshot = _stableSnapshot;
            }
        }

        if (validatedRoot == WorkspacePath.RootDirectory)
        {
            return snapshot;
        }

        FileTreeNode? node = FindNode(snapshot.Root, validatedRoot);
        if (node == null)
        {
            throw new DirectoryNotFoundException($"稳定文件树快照中不存在目录: {validatedRoot}");
        }

        return new FileTreeSnapshot { Root = CloneNode(node, string.Empty, 0) };
    }

    /// <summary>
    /// 构建稳定快照（唯一入口）。乐观并发策略，为低并发场景设计。
    /// 快照级一致性：每个目录进入时记录修改时间戳+成员列表，退出时再次获取比较；
    /// 文件仅比较时间戳。任一环节不一致则整轮快照作废，由统一计时器重试。
    /// 不原地重建 —— 缓存层自治，按统一计时器节奏刷新。
    /// 【禁止在此处引入写入状态检查 —— 缓存层不感知业务写入。】
    /// </summary>
    private StableSnapshotRefresh RefreshStableSnapshot(string source)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(FileSystemCache));

        lock (_snapshotRefreshLock)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(FileSystemCache));

            long snapshotStartTicks = DateTime.UtcNow.Ticks;
            InMemoryWorkspaceOverlay? overlay = _overlay;
            FileTreeSnapshot snapshot;
            if (overlay != null)
            {
                snapshot = overlay.GetFileTreeSnapshot(WorkspacePath.RootDirectory);
            }
            else
            {
                snapshot = BuildTreeSnapshot(WorkspacePath.RootDirectory, snapshotStartTicks);
            }
            _stableSnapshot = snapshot;
            _hasStableSnapshot = true;
            if (overlay == null && !IsGlobalWatcherAlive())
            {
                InitializeGlobalWatcher();
            }
            Log.For(LogModule.Core).Debug($"稳定文件树快照已刷新，来源: {source}");
            return new StableSnapshotRefresh(snapshot, snapshotStartTicks);
        }
    }

    private FileTreeSnapshot BuildTreeSnapshot(string relativeRoot, long snapshotStartTicks)
    {
        FileTreeNode root = new(relativeRoot, string.Empty, 0);
        HashSet<string> visited = new(StringComparer.Ordinal) { relativeRoot };
        FileTreeNode? previousRoot = _hasStableSnapshot ? _stableSnapshot.Root : null;

        BuildTreeRecursive(root, previousRoot, visited, snapshotStartTicks);

        return new FileTreeSnapshot
        {
            Root = root
        };
    }

    private static FileTreeNode? FindNode(FileTreeNode current, string targetPath)
    {
        if (current.RelativePath == targetPath)
        {
            return current;
        }

        foreach (FileTreeNode child in current.Children)
        {
            FileTreeNode? found = FindNode(child, targetPath);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static FileTreeNode CloneNode(FileTreeNode source, string name, int depth)
    {
        FileTreeNode clone = new(source.RelativePath, name, depth);
        clone.SetDirectorySignature(source.DirectorySignature);
        clone.SetHashes(source.MemberHash, source.SubtreeHash);
        foreach (string fileName in source.Files)
        {
            clone.AddFile(fileName, source.GetFileSignature(fileName));
        }

        foreach (FileTreeNode child in source.Children)
        {
            clone.Children.Add(CloneNode(child, child.Name, depth + 1));
        }

        return clone;
    }

    /// <summary>
    /// 快照级一致性递归构建（乐观并发，为低并发场景设计）。
    /// 进入目录时记录修改时间戳 + 成员列表，退出时再次获取比较。
    /// 整个递归处理包裹在进入/退出检查之间，任一不一致则整轮快照作废。
    /// 文件仅比较时间戳（创建 + 修改），不读内容。
    /// </summary>
    private void BuildTreeRecursive(FileTreeNode node, FileTreeNode? previousNode, HashSet<string> visited, long snapshotStartTicks)
    {
        if (node.Depth >= MaxTreeDepth) return;

        string fullPath = ResolveFullPath(node.RelativePath);
        DirectoryInfo dirInfo = SystemIOWrapper.GetDirectoryInfo(fullPath);

        // ── 进入：记录目录修改时间戳 + 成员列表 ──
        long dirWriteTimeBefore;
        string[] entryMembers;
        List<FileInfo> canonicalFiles;
        List<DirectoryInfo> canonicalDirs;
        Dictionary<string, FileTreeNode> previousChildren = new(StringComparer.Ordinal);
        bool membersReused = false;
        TreeNodeHash memberHash = TreeNodeHash.Zero;
        try
        {
            dirInfo.Refresh();
            if (!dirInfo.Exists)
                throw new SnapshotRefreshInvalidatedException($"构建文件树快照时目录不存在: {node.RelativePath}");

            dirWriteTimeBefore = dirInfo.LastWriteTimeUtc.Ticks;
            FileInfo[] files = dirInfo.GetFiles();
            DirectoryInfo[] dirs = dirInfo.GetDirectories();

            if (previousNode != null)
            {
                foreach (FileTreeNode previousChild in previousNode.Children)
                    previousChildren[previousChild.Name] = previousChild;
            }

            // 成员级短路：本层直接成员的递归哈希（文件名 + 创建/修改时间戳、子目录名）与上轮一致时，
            // 跳过逐文件 GetFileId/IsReparsePoint/可读性探测（每文件多次开句柄 syscall）。
            // 短路仅豁免本层 syscall，子目录仍逐层递归、各自独立判定——父级判定绝不替代子级真实变更
            // （目录 mtime 只在直接成员增删改名时变化，文件内容变化不冒泡，不能作为深层依据）。
            // 含 reparse/hardlink 的目录枚举哈希与旧树（已归并）必然不等，自然回落全量路径。
            // 信任边界与全量路径一致：现有变更检测同样只比较名字 + 时间戳，从不读内容。
            TreeNodeHash enumMemberHash = ComputeMemberHash(files, dirs);
            if (previousNode != null && previousNode.MemberHash.Equals(enumMemberHash))
            {
                membersReused = true;
                memberHash = enumMemberHash;
                foreach (string fileName in previousNode.Files)
                    node.AddFile(fileName, previousNode.GetFileSignature(fileName));
                canonicalFiles = [];
                canonicalDirs = [];
                entryMembers = [];
            }
            else
            {
                // 去重:reparse(symlink/junction)丢弃 + hardlink 按 FileId 归并
                canonicalFiles = DedupCanonicalFiles(files);
                canonicalDirs = DedupCanonicalDirectories(dirs);
                entryMembers = CollectDirectoryMembers(canonicalFiles, canonicalDirs);
                memberHash = ComputeMemberHash(canonicalFiles, canonicalDirs);
            }
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new SnapshotRefreshInvalidatedException($"构建文件树快照时目录消失: {node.RelativePath}", ex);
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
        {
            // 占据分级：旧子树内无关键文件 → 容错（复用旧子树 + 容错路径标记）；
            // 无法判定（无旧子树）或含关键文件 → 现状受阻。
            if (previousNode != null && !SubtreeContainsCriticalFile(previousNode))
            {
                ReuseSubtree(node, previousNode);
                EnterToleratedPath(node.RelativePath);
                return;
            }

            WorkspaceFileBlockedException blockedEx = new(
                node.RelativePath,
                $"目录正在被外部进程占用，无法构建文件树快照: {node.RelativePath}。{ex.Message}",
                ex);
            EnterBlockedState(blockedEx);
            throw blockedEx;
        }
        catch (UnauthorizedAccessException ex)
        {
            if (previousNode != null && !SubtreeContainsCriticalFile(previousNode))
            {
                ReuseSubtree(node, previousNode);
                EnterToleratedPath(node.RelativePath);
                return;
            }

            WorkspaceFileBlockedException blockedEx = new(
                node.RelativePath,
                $"目录访问被拒绝，无法构建文件树快照(可能被外部进程独占): {node.RelativePath}。{ex.Message}",
                ex);
            EnterBlockedState(blockedEx);
            throw blockedEx;
        }

        // ── 处理文件：仅比较时间戳(canonicalFiles 已去重+过滤) ──
        foreach (FileInfo fileInfo in canonicalFiles)
        {
            string fileName = fileInfo.Name;
            string fileRelativePath = node.GetFileRelativePath(fileName);
            FileSignature fileSig;
            try
            {
                FileSignature fileBefore = new(
                    fileInfo.CreationTimeUtc.Ticks,
                    fileInfo.LastWriteTimeUtc.Ticks);
                // 未来时间戳（时钟跳变/元数据异常）是数据异常：时间戳固定，采样窗口内必然一致。
                // 豁免整轮作废，避免单个异常文件永久卡死整棵快照树（watcher 健康 + 工程静止也救不回）。
                // 仍走下方 ProbeFileReadable + fileBefore==fileAfter 采样窗口校验，正常文件的一致性保护零损失。
                long nowTicks = DateTime.UtcNow.Ticks;
                bool isFutureTimestamp = fileBefore.CreationTimeUtcTicks > nowTicks
                                      || fileBefore.LastWriteTimeUtcTicks > nowTicks;
                if (!isFutureTimestamp &&
                    (fileBefore.CreationTimeUtcTicks > snapshotStartTicks ||
                     fileBefore.LastWriteTimeUtcTicks > snapshotStartTicks))
                    throw new SnapshotRefreshInvalidatedException($"文件时间戳晚于快照开始时间: {fileRelativePath}");

                ProbeFileReadable(fileRelativePath);

                fileInfo.Refresh();
                if (!fileInfo.Exists)
                    throw new SnapshotRefreshInvalidatedException($"构建文件树快照时文件消失: {fileRelativePath}");

                FileSignature fileAfter = new(
                    fileInfo.CreationTimeUtc.Ticks,
                    fileInfo.LastWriteTimeUtc.Ticks);
                if (!fileBefore.Equals(fileAfter))
                    throw new SnapshotRefreshInvalidatedException($"构建文件树快照期间文件时间戳变化: {fileRelativePath}");

                fileSig = fileBefore;
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
            {
                // 占据分级：非关键文件 → 容错（复用旧签名收录，无旧节点则不收录=占位不存在）；
                // 关键文件 → 现状受阻（前置阻断）。
                if (!CriticalFilePolicy.IsCriticalPath(fileRelativePath))
                {
                    if (previousNode != null && previousNode.Files.Contains(fileName))
                        node.AddFile(fileName, previousNode.GetFileSignature(fileName));
                    EnterToleratedPath(fileRelativePath);
                    continue;
                }

                WorkspaceFileBlockedException blockedEx = new(
                    fileRelativePath,
                    $"文件正在被外部进程占用: {fileRelativePath}。{ex.Message}",
                    ex);
                EnterBlockedState(blockedEx);
                throw blockedEx;
            }
            catch (FileNotFoundException ex)
            {
                throw new SnapshotRefreshInvalidatedException($"构建文件树快照时文件消失: {fileRelativePath}", ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                throw new SnapshotRefreshInvalidatedException($"构建文件树快照时文件父目录消失: {fileRelativePath}", ex);
            }
            node.AddFile(fileName, fileSig);
        }

        // ── 处理子目录（递归） ──
        // 成员复用路径：以旧树子目录为递归源（每层独立做复用判定）；全量路径：canonicalDirs 已去重+过滤。
        TreeNodeHash childrenXor = TreeNodeHash.Zero;
        if (membersReused)
        {
            foreach (FileTreeNode previousChild in previousChildren.Values)
            {
                string childRelativePath = node.GetChildDirectoryRelativePath(previousChild.Name);
                if (visited.Contains(childRelativePath)) continue;
                visited.Add(childRelativePath);

                FileTreeNode child = node.AddChildDirectory(previousChild.Name);
                BuildTreeRecursive(child, previousChild, visited, snapshotStartTicks);
                childrenXor = childrenXor.Xor(child.SubtreeHash);
            }
        }
        else
        {
            foreach (DirectoryInfo subDirInfo in canonicalDirs)
            {
                string dirName = subDirInfo.Name;

                string childRelativePath = node.GetChildDirectoryRelativePath(dirName);
                if (visited.Contains(childRelativePath)) continue;
                visited.Add(childRelativePath);

                previousChildren.TryGetValue(dirName, out FileTreeNode? previousChild);
                FileTreeNode child = node.AddChildDirectory(dirName);
                BuildTreeRecursive(child, previousChild, visited, snapshotStartTicks);
                childrenXor = childrenXor.Xor(child.SubtreeHash);
            }
        }

        // ── 退出：比较修改时间戳 + 成员列表 ──
        try
        {
            dirInfo.Refresh();
            if (!dirInfo.Exists)
                throw new SnapshotRefreshInvalidatedException($"构建文件树快照时目录消失: {node.RelativePath}");

            long dirWriteTimeAfter = dirInfo.LastWriteTimeUtc.Ticks;
            FileInfo[] exitFiles = dirInfo.GetFiles();
            DirectoryInfo[] exitDirs = dirInfo.GetDirectories();
            if (membersReused)
            {
                // 复用路径：哈希重算比对（期间变化 → 整轮作废）。
                if (dirWriteTimeBefore != dirWriteTimeAfter
                    || !ComputeMemberHash(exitFiles, exitDirs).Equals(memberHash))
                    throw new SnapshotRefreshInvalidatedException($"构建文件树快照期间目录内容变化: {node.RelativePath}");
            }
            else
            {
                List<FileInfo> exitCanonicalFiles = DedupCanonicalFiles(exitFiles);
                List<DirectoryInfo> exitCanonicalDirs = DedupCanonicalDirectories(exitDirs);
                string[] exitMembers = CollectDirectoryMembers(exitCanonicalFiles, exitCanonicalDirs);

                if (dirWriteTimeBefore != dirWriteTimeAfter || !entryMembers.AsSpan().SequenceEqual(exitMembers))
                    throw new SnapshotRefreshInvalidatedException($"构建文件树快照期间目录内容变化: {node.RelativePath}");
            }

            node.SetDirectorySignature(new DirectorySignature(dirInfo.CreationTimeUtc.Ticks, dirWriteTimeBefore));
            node.SetHashes(memberHash, TreeNodeHash.Combine(memberHash, childrenXor));
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new SnapshotRefreshInvalidatedException($"构建文件树快照时目录消失: {node.RelativePath}", ex);
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
        {
            // 占据分级（退出校验无法完成）：非关键目录放弃退出校验继续（快照数据来自进入段枚举）；
            // 关键目录维持现状受阻。
            if (!CriticalFilePolicy.IsCriticalPath(node.RelativePath))
            {
                EnterToleratedPath(node.RelativePath);
                return;
            }

            WorkspaceFileBlockedException blockedEx = new(
                node.RelativePath,
                $"目录正在被外部进程占用: {node.RelativePath}。{ex.Message}",
                ex);
            EnterBlockedState(blockedEx);
            throw blockedEx;
        }
        catch (UnauthorizedAccessException ex)
        {
            if (!CriticalFilePolicy.IsCriticalPath(node.RelativePath))
            {
                EnterToleratedPath(node.RelativePath);
                return;
            }

            WorkspaceFileBlockedException blockedEx = new(
                node.RelativePath,
                $"目录访问被拒绝(可能被外部进程独占): {node.RelativePath}。{ex.Message}",
                ex);
            EnterBlockedState(blockedEx);
            throw blockedEx;
        }
    }

    /// <summary>
    /// 子树内是否存在关键文件（占据分级：目录级容错判定）。
    /// </summary>
    private static bool SubtreeContainsCriticalFile(FileTreeNode node)
    {
        foreach (string fileName in node.Files)
        {
            if (CriticalFilePolicy.IsCriticalPath(node.GetFileRelativePath(fileName)))
                return true;
        }

        foreach (FileTreeNode child in node.Children)
        {
            if (SubtreeContainsCriticalFile(child))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 容错复用旧子树：整体克隆旧节点（文件签名/子节点/哈希），不触碰磁盘。
    /// </summary>
    private static void ReuseSubtree(FileTreeNode node, FileTreeNode previousNode)
    {
        node.SetDirectorySignature(previousNode.DirectorySignature);
        foreach (string fileName in previousNode.Files)
        {
            node.AddFile(fileName, previousNode.GetFileSignature(fileName));
        }

        foreach (FileTreeNode child in previousNode.Children)
        {
            node.Children.Add(CloneNode(child, child.Name, node.Depth + 1));
        }

        node.SetHashes(previousNode.MemberHash, previousNode.SubtreeHash);
    }

    /// <summary>
    /// 计算本层直接成员的哈希（ignored 过滤与建树一致）。
    /// 调用方决定输入集：复用判定用原始枚举，建树存储用归并后成员——
    /// 含 reparse/hardlink 的目录两种输入必然不等，自然失去复用资格。
    /// </summary>
    private static TreeNodeHash ComputeMemberHash(IReadOnlyList<FileInfo> files, IReadOnlyList<DirectoryInfo> dirs)
    {
        TreeNodeHash hash = TreeNodeHash.Zero;
        foreach (FileInfo f in files)
        {
            if (WorkspacePath.IsIgnoredFileName(f.Name)) continue;
            hash = hash.Xor(TreeNodeHash.OfFile(f.Name, new FileSignature(f.CreationTimeUtc.Ticks, f.LastWriteTimeUtc.Ticks)));
        }
        foreach (DirectoryInfo d in dirs)
        {
            if (WorkspacePath.IsIgnoredSegment(d.Name)) continue;
            hash = hash.Xor(TreeNodeHash.OfDirectory(d.Name));
        }
        return hash;
    }

    private static string[] CollectDirectoryMembers(IReadOnlyList<FileInfo> files, IReadOnlyList<DirectoryInfo> dirs)
    {
        List<string> members = new(files.Count + dirs.Count);
        foreach (FileInfo f in files)
            if (!WorkspacePath.IsIgnoredFileName(f.Name))
                members.Add(f.Name);
        foreach (DirectoryInfo d in dirs)
            if (!WorkspacePath.IsIgnoredSegment(d.Name))
                members.Add(d.Name);
        members.Sort(StringComparer.Ordinal);
        return members.ToArray();
    }

    /// <summary>
    /// 文件去重:过滤 ignored + 丢弃 reparse(symlink/junction) + hardlink 按 FileId 归并。
    /// 同一物理文件只保留第一个 FileInfo。开句柄算 FileId 遇共享冲突 → 受阻。
    /// 占据分级：非关键文件被占据 → 跳过（不收录，占位不存在）；关键 → 受阻。
    /// </summary>
    private List<FileInfo> DedupCanonicalFiles(FileInfo[] files)
    {
        Dictionary<FileId, FileInfo> byId = new();
        foreach (FileInfo f in files)
        {
            if (WorkspacePath.IsIgnoredFileName(f.Name)) continue;
            if (_fileIdentity.IsReparsePoint(f.FullName)) continue; // symlink/junction 丢弃

            FileId id;
            try
            {
                id = ReadFileId(f.FullName);
            }
            catch (WorkspaceFileBlockedException)
            {
                string relativePath = ConvertToRelativePath(f.FullName);
                if (CriticalFilePolicy.IsCriticalPath(relativePath))
                {
                    throw;
                }

                EnterToleratedPath(relativePath);
                continue;
            }
            byId.TryAdd(id, f); // 同 id 已存在 → 丢弃(hardlink 去重)
        }
        return byId.Values.ToList();
    }

    /// <summary>
    /// 目录去重:过滤 ignored + 丢弃 reparse(junction/symlink)。
    /// 目录 hardlink 在游戏工程中不存在,不做 FileId 归并。
    /// </summary>
    private List<DirectoryInfo> DedupCanonicalDirectories(DirectoryInfo[] dirs)
    {
        List<DirectoryInfo> result = new(dirs.Length);
        foreach (DirectoryInfo d in dirs)
        {
            if (WorkspacePath.IsIgnoredSegment(d.Name)) continue;
            if (_fileIdentity.IsReparsePoint(d.FullName)) continue; // junction/symlink 丢弃
            result.Add(d);
        }
        return result;
    }

    /// <summary>
    /// 计算路径的物理 FileId(follow 链接)。遇共享冲突(32/33)：
    /// 非关键文件仅记录容错路径并抛（调用方跳过该文件）；关键文件进入受阻状态并抛。
    /// </summary>
    private FileId ReadFileId(string fullPath)
    {
        try
        {
            return _fileIdentity.GetFileId(fullPath);
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
        {
            string relativePath = ConvertToRelativePath(fullPath);
            if (!CriticalFilePolicy.IsCriticalPath(relativePath))
            {
                EnterToleratedPath(relativePath);
                throw new WorkspaceFileBlockedException(
                    relativePath,
                    $"文件正在被外部进程占用: {relativePath}。{ex.Message}",
                    ex);
            }

            WorkspaceFileBlockedException blockedEx = new(
                relativePath,
                $"文件正在被外部进程占用: {relativePath}。{ex.Message}",
                ex);
            EnterBlockedState(blockedEx);
            throw blockedEx;
        }
        catch (UnauthorizedAccessException ex)
        {
            string relativePath = ConvertToRelativePath(fullPath);
            if (!CriticalFilePolicy.IsCriticalPath(relativePath))
            {
                EnterToleratedPath(relativePath);
                throw new WorkspaceFileBlockedException(
                    relativePath,
                    $"文件访问被拒绝(可能被外部进程独占): {relativePath}。{ex.Message}",
                    ex);
            }

            WorkspaceFileBlockedException blockedEx = new(
                relativePath,
                $"文件访问被拒绝(可能被外部进程独占): {relativePath}。{ex.Message}",
                ex);
            EnterBlockedState(blockedEx);
            throw blockedEx;
        }
        catch (FileNotFoundException ex)
        {
            string relativePath = ConvertToRelativePath(fullPath);
            throw new SnapshotRefreshInvalidatedException($"构建文件树快照时文件消失: {relativePath}", ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            string relativePath = ConvertToRelativePath(fullPath);
            throw new SnapshotRefreshInvalidatedException($"构建文件树快照时文件父目录消失: {relativePath}", ex);
        }
    }

    /// <summary>
    /// 调度本地写删移后的统一计时器刷新。
    /// 本地变更仅通过 ScheduleRefresh→计时器→ProcessPendingInvalidations 路径驱动快照刷新。
    /// 快照刷新的唯一路径是计时器回调，绝对禁止在此处同步刷新分裂出第二条路径！
    /// </summary>
    internal long NotifyLocalMutationCompleted()
    {
        return ScheduleRefresh();
    }

    public void ProbeFileReadable(string relativePath)
    {
        string validatedPath = WorkspacePath.RequireRelativePath(relativePath);
        string fullPath = ResolveFullPath(validatedPath);
        try
        {
            using FileStream _ = SystemIOWrapper.OpenFileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
        {
            WorkspaceFileBlockedException blockedEx = new(
                validatedPath,
                $"文件正在被外部进程占用: {validatedPath}。{ex.Message}",
                ex);
            EnterBlockedState(blockedEx);
            throw blockedEx;
        }
        catch (UnauthorizedAccessException ex)
        {
            WorkspaceFileBlockedException blockedEx = new(
                validatedPath,
                $"文件访问被拒绝(可能被外部进程独占): {validatedPath}。{ex.Message}",
                ex);
            EnterBlockedState(blockedEx);
            throw blockedEx;
        }
    }

    #endregion

    #region File/Directory Existence

    // =========================================================================
    // 文件存在性检查
    // =========================================================================

    public bool FileExists(string relativePath)
    {
        InMemoryWorkspaceOverlay? overlay = _overlay;
        if (overlay != null)
        {
            return overlay.FileExists(relativePath);
        }

        string fullPath = ResolveFullPath(relativePath);
        return SystemIOWrapper.FileExists(fullPath);
    }

    public bool DirectoryExists(string relativePath)
    {
        InMemoryWorkspaceOverlay? overlay = _overlay;
        if (overlay != null)
        {
            return overlay.DirectoryExists(relativePath);
        }

        string fullPath = ResolveFullPath(relativePath);
        return SystemIOWrapper.DirectoryExists(fullPath);
    }

    #endregion

    // =========================================================================
    // 内部类型
    // =========================================================================

    private sealed class FileContentEntry
    {
        public bool Exists;
        public long LastWriteTimeUtcTicks;
        public byte[] Bytes = Array.Empty<byte>();
        public byte[] Hash = Array.Empty<byte>();
    }

    private sealed class DirectoryScanEntry
    {
        public IReadOnlyList<string> Entries = Array.Empty<string>();
        public volatile bool IsDirty;
    }

    private sealed class SnapshotRefreshInvalidatedException : Exception
    {
        public SnapshotRefreshInvalidatedException(string message) : base(message)
        {
        }

        public SnapshotRefreshInvalidatedException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}

// =============================================================================
// 文件/目录时间戳签名
// =============================================================================

internal readonly record struct FileSignature(long CreationTimeUtcTicks, long LastWriteTimeUtcTicks);

/// <summary>
/// 128 位结构哈希（双路 FNV-1a 异源种子 + splitmix64 收官）。仅用于变更检测：碰撞即漏检，故取 128 位。
/// 成员级用 XOR 累加（顺序无关），同一目录下成员名唯一，无异名同哈希抵消问题。
/// </summary>
internal readonly record struct TreeNodeHash(ulong A, ulong B)
{
    private const ulong OffsetBasisA = 0xcbf29ce484222325UL;
    private const ulong OffsetBasisB = 0x9e3779b97f4a7c15UL;
    private const ulong FnvPrime = 0x100000001b3UL;

    internal static TreeNodeHash Zero => new(0, 0);

    internal TreeNodeHash Xor(TreeNodeHash other) => new(A ^ other.A, B ^ other.B);

    /// <summary>文件成员：类型标签 + 名字 + 创建/修改时间戳。</summary>
    internal static TreeNodeHash OfFile(string name, FileSignature signature)
    {
        (ulong a, ulong b) = HashText('f' + name);
        a = Mix(a ^ (ulong)signature.CreationTimeUtcTicks);
        b = Mix(b ^ (ulong)signature.LastWriteTimeUtcTicks);
        return new TreeNodeHash(a, b);
    }

    /// <summary>子目录成员：类型标签 + 名字。</summary>
    internal static TreeNodeHash OfDirectory(string name)
    {
        (ulong a, ulong b) = HashText('d' + name);
        return new TreeNodeHash(Mix(a), Mix(b));
    }

    /// <summary>由本层成员哈希与子层递归哈希合成子树哈希。</summary>
    internal static TreeNodeHash Combine(TreeNodeHash memberHash, TreeNodeHash childrenXor)
    {
        return new TreeNodeHash(Mix(memberHash.A ^ childrenXor.A), Mix(memberHash.B ^ childrenXor.B));
    }

    private static (ulong A, ulong B) HashText(string text)
    {
        ulong a = OffsetBasisA;
        ulong b = OffsetBasisB;
        foreach (char c in text)
        {
            a = (a ^ c) * FnvPrime;
            b = (b ^ c) * FnvPrime;
        }
        return (a, b);
    }

    private static ulong Mix(ulong x)
    {
        x ^= x >> 30;
        x *= 0xbf58476d1ce4e5b9UL;
        x ^= x >> 27;
        x *= 0x94d049bb133111ebUL;
        x ^= x >> 31;
        return x;
    }
}

internal readonly record struct DirectorySignature(long CreationTimeUtcTicks, long LastWriteTimeUtcTicks);

internal readonly record struct StableSnapshotRefresh(FileTreeSnapshot Snapshot, long SnapshotStartTicks);

internal sealed class FileTreeChangedEventArgs : EventArgs
{
    public long SnapshotStartTicks { get; }

    public FileTreeChangedEventArgs(long snapshotStartTicks)
    {
        SnapshotStartTicks = snapshotStartTicks;
    }
}
