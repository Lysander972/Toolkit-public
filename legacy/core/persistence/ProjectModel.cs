// Persistence 层 - 工程模型入口
// 职责：文件 ↔ 数据模型的投射
// 详见 README.md

using System.Security.Cryptography;
using System.Text;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

// =============================================================================
// 工程持久化器 - 内部服务
// 封装 SaveChanges 流程
// =============================================================================

/// <summary>
/// 工程持久化器。
/// 封装 SaveChanges 流程：期望树收集 → 快照 → Diff → 原子执行。
/// </summary>
internal sealed class ProjectPersister
{
    private readonly Func<string, ProjectEntity?> _getByPath;
    private readonly IPersistenceOperationApplier _operationApplier;
    private readonly object _queueLock = new();
    private readonly Queue<PersistenceTransaction> _pendingTransactions = [];

    public ProjectPersister(Func<string, ProjectEntity?> getByPath, IPersistenceOperationApplier operationApplier)
    {
        _getByPath = getByPath;
        _operationApplier = operationApplier;
    }

    /// <returns>实际受影响的相对路径列表（包含所有实际执行的文件和目录操作）</returns>
    public IReadOnlyList<string> Persist(ProjectEntity entity, IReadOnlySet<string> dirtyPaths)
    {
        string entityPath = WorkspacePath.RequireRelativePath(entity.实体_完整相对路径标识);

        // 版本号验证
        ProjectEntity? readOnly = _getByPath(entityPath);
        if (readOnly != null && entity.Version != readOnly.Version)
        {
            throw new InvalidOperationException(
                $"版本号冲突: 可写实体版本 '{entity.Version}' 与当前只读实体版本 '{readOnly.Version}' 不匹配。" +
                $"路径: {entityPath}。可能原因：可写副本已过期。");
        }

        entity.Validate();

        // 1) 收集期望树 B（实体声明的覆盖范围）
        ExpectedTreeBuffer expected = new();
        ExpectedTreeCollector.Collect(entity, expected, dirtyPaths);

        // 2) 从已有文件树筛选起点范围 A（不扫描文件系统）
        FileTreeSnapshot fileTree = FileIO.GetFileTreeSnapshot();
        LocalFileSystemSnapshot currentSnapshot = LocalFileSystemSnapshot.FilterFromTree(fileTree, entityPath);
        if (entityPath != WorkspacePath.RootDirectory &&
            currentSnapshot.Files.Count == 0 &&
            currentSnapshot.Directories.Count == 0)
        {
            throw new InvalidOperationException($"持久化覆盖面不存在: {entityPath}");
        }

        // 3) 计算 Diff
        LocalDiffResult diff = LocalDiffCalculator.Calculate(expected, currentSnapshot);
        if (diff.Operations.Count == 0) return Array.Empty<string>();

        Log.For(LogModule.Core).Info(
            $"持久化 Diff [起点: {entityPath}] 目录(+{diff.Statistics.DirectoriesToCreate}/-{diff.Statistics.DirectoriesToDelete}) " +
            $"文件(写{diff.Statistics.FilesToWrite}/删{diff.Statistics.FilesToDelete}) 操作数={diff.Operations.Count}");

        PersistenceCoverage coverage = PersistenceCoverage.From(currentSnapshot, expected, diff);
        coverage.AssertExists(entityPath);
        HashSet<string> skippedPaths = new(StringComparer.Ordinal);
        byte[] initialStateHash = ComputeStateHash(coverage, new Dictionary<string, FileStream>(StringComparer.Ordinal), skippedPaths, collectSkips: true);

        PersistenceTransaction transaction = new(entityPath, diff, coverage, initialStateHash, skippedPaths);
        lock (_queueLock)
        {
            _pendingTransactions.Enqueue(transaction);
        }

        return DrainQueue(transaction);
    }

    public IReadOnlyList<string> DrainPendingTransactions()
        => DrainQueue(null);

    private IReadOnlyList<string> DrainQueue(PersistenceTransaction? targetTransaction)
    {
        while (true)
        {
            PersistenceTransaction transaction;
            lock (_queueLock)
            {
                if (_pendingTransactions.Count == 0)
                {
                    return Array.Empty<string>();
                }

                transaction = _pendingTransactions.Peek();
            }

            try
            {
                IReadOnlyList<string> affectedPaths = ExecuteTransaction(transaction);
                lock (_queueLock)
                {
                    if (ReferenceEquals(_pendingTransactions.Peek(), transaction))
                    {
                        _pendingTransactions.Dequeue();
                    }
                }

                if (ReferenceEquals(transaction, targetTransaction))
                {
                    return affectedPaths;
                }
                if (targetTransaction == null)
                {
                    return affectedPaths;
                }
            }
            catch (WorkspaceFileBlockedException)
            {
                // 后台排空（targetTransaction == null）：受阻静默跳过，事务留队列，
                // 等受阻解除后由 OnWorkspaceBlockStateChanged 自动重试。这是后台尽力而为。
                if (targetTransaction is null)
                    return Array.Empty<string>();

                // 同步 Persist：调用者明确期待自己的事务落盘。受阻必须作为失败冒泡——
                // 空列表与"合法无操作"(diff.Operations.Count == 0) 无法区分，下游会按成功处理（假成功）。
                // 事务仍留在队列（未 Dequeue），受阻解除后由 OnWorkspaceBlockStateChanged 自动排空。
                throw;
            }
            catch
            {
                lock (_queueLock)
                {
                    if (_pendingTransactions.Count > 0 && ReferenceEquals(_pendingTransactions.Peek(), transaction))
                    {
                        _pendingTransactions.Dequeue();
                    }
                }
                throw;
            }
        }
    }

    private IReadOnlyList<string> ExecuteTransaction(PersistenceTransaction transaction)
    {
        _operationApplier.Apply(
            transaction.Diff,
            transaction.Diff.Operations,
            transaction.Coverage.Files,
            heldHandles =>
            {
                byte[] currentStateHash = ComputeStateHash(transaction.Coverage, heldHandles, transaction.SkippedPaths, collectSkips: false);
                if (!transaction.InitialStateHash.AsSpan().SequenceEqual(currentStateHash))
                {
                    string message = "当前工作区有外部进程进行写操作，已进入受阻状态，请稍后重试。";
                    WorkspaceFileBlockedException blockedEx = new(
                        transaction.RootRelativePath,
                        $"持久化覆盖面状态已变化，拒绝落盘: {transaction.RootRelativePath}",
                        message);
                    FileSystemCache.Instance.ReportBlockedException(blockedEx);
                    throw new WorkspaceStateChangedException(message);
                }
            });

        return transaction.Diff.Operations
            .Select(static op => op.RelativePath)
            .ToList();
    }

    /// <summary>
    /// 计算持久化覆盖面状态哈希。
    /// 非关键文件被外部占据时容错跳过（collectSkips=true 时收集到 skippedPaths，
    /// 复算用同一集合保证初态/复算一致）；关键文件占据维持现状受阻。
    /// </summary>
    private static byte[] ComputeStateHash(
        PersistenceCoverage coverage,
        IReadOnlyDictionary<string, FileStream> heldHandles,
        HashSet<string> skippedPaths,
        bool collectSkips)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (string dir in coverage.Directories.OrderBy(static path => path, StringComparer.Ordinal))
        {
            if (skippedPaths.Contains(dir))
            {
                continue;
            }

            AppendString(hash, "D");
            AppendString(hash, dir);
            string[] entries;
            try
            {
                entries = FileIO.GetFiles(dir, "*", SearchOption.TopDirectoryOnly)
                    .Concat(FileIO.GetDirectories(dir, "*", SearchOption.TopDirectoryOnly))
                    .Select(SystemIOWrapper.GetLastSegment)
                    .Where(static name => !WorkspacePath.IsIgnoredSegment(name) && !WorkspacePath.IsIgnoredFileName(name))
                    .OrderBy(static name => name, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
            {
                if (!CriticalFilePolicy.IsCriticalPath(dir))
                {
                    if (collectSkips)
                    {
                        skippedPaths.Add(dir);
                    }
                    continue;
                }

                WorkspaceFileBlockedException blockedEx = new(
                    dir,
                    $"目录正在被外部进程占用，无法计算持久化状态哈希: {dir}。{ex.Message}",
                    ex);
                FileSystemCache.Instance.ReportBlockedException(blockedEx);
                throw blockedEx;
            }

            foreach (string entry in entries)
            {
                AppendString(hash, entry);
            }
        }

        foreach (string file in coverage.Files.OrderBy(static path => path, StringComparer.Ordinal))
        {
            if (skippedPaths.Contains(file))
            {
                continue;
            }

            AppendString(hash, "F");
            AppendString(hash, file);
            byte[] fileHash = ComputeFileHash(file, heldHandles, skippedPaths, collectSkips);
            hash.AppendData(fileHash);
        }

        return hash.GetHashAndReset();
    }

    private static byte[] ComputeFileHash(
        string relativePath,
        IReadOnlyDictionary<string, FileStream> heldHandles,
        HashSet<string> skippedPaths,
        bool collectSkips)
    {
        if (heldHandles.ContainsKey(relativePath))
        {
            FileStream handle = heldHandles[relativePath];
            long position = handle.Position;
            handle.Position = 0;
            using SHA256 sha = SHA256.Create();
            byte[] result = sha.ComputeHash(handle);
            handle.Position = position;
            return result;
        }

        try
        {
            return SHA256.HashData(FileIO.ReadAllBytes(relativePath));
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
        {
            if (!CriticalFilePolicy.IsCriticalPath(relativePath))
            {
                if (collectSkips)
                {
                    skippedPaths.Add(relativePath);
                }
                // 复算模式且不在 skippedPaths：外部在窗口内开始占据，维持现状拒绝落盘
                return SHA256.HashData(Array.Empty<byte>());
            }

            WorkspaceFileBlockedException blockedEx = new(
                relativePath,
                $"文件正在被外部进程占用，无法计算持久化状态哈希: {relativePath}。{ex.Message}",
                ex);
            FileSystemCache.Instance.ReportBlockedException(blockedEx);
            throw blockedEx;
        }
    }

    private static void AppendString(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private sealed record PersistenceTransaction(
        string RootRelativePath,
        LocalDiffResult Diff,
        PersistenceCoverage Coverage,
        byte[] InitialStateHash,
        HashSet<string> SkippedPaths);

    private sealed class PersistenceCoverage
    {
        public HashSet<string> Files { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Directories { get; } = new(StringComparer.Ordinal);

        public static PersistenceCoverage From(
            LocalFileSystemSnapshot currentSnapshot,
            ExpectedTreeBuffer expected,
            LocalDiffResult diff)
        {
            PersistenceCoverage coverage = new();
            foreach (string file in currentSnapshot.Files.Keys)
            {
                coverage.Files.Add(file);
                string parent = RelativePathUtils.GetParentDirectory(file);
                if (parent.Length > 0) coverage.Directories.Add(parent);
            }
            foreach (string dir in currentSnapshot.Directories)
            {
                coverage.Directories.Add(dir);
            }
            foreach ((string file, _) in expected.GetAllFiles())
            {
                if (currentSnapshot.Files.ContainsKey(file))
                {
                    coverage.Files.Add(file);
                }

                string parent = RelativePathUtils.GetParentDirectory(file);
                if (parent.Length > 0 && currentSnapshot.Directories.Contains(parent))
                {
                    coverage.Directories.Add(parent);
                }
            }
            foreach (string dir in expected.GetAllDirectories())
            {
                if (currentSnapshot.Directories.Contains(dir))
                {
                    coverage.Directories.Add(dir);
                }
            }
            foreach (AtomicOperation op in diff.Operations)
            {
                string parent = RelativePathUtils.GetParentDirectory(op.RelativePath);
                if (parent.Length > 0 && currentSnapshot.Directories.Contains(parent))
                {
                    coverage.Directories.Add(parent);
                }
                if (op.Type is AtomicOperationType.WriteFile or AtomicOperationType.DeleteFile
                    && currentSnapshot.Files.ContainsKey(op.RelativePath))
                {
                    coverage.Files.Add(op.RelativePath);
                }
                if (op.Type is AtomicOperationType.DeleteDirectory or AtomicOperationType.CreateDirectory
                    && currentSnapshot.Directories.Contains(op.RelativePath))
                {
                    coverage.Directories.Add(op.RelativePath);
                }
            }

            return coverage;
        }

        public void AssertExists(string rootRelativePath)
        {
            foreach (string dir in Directories)
            {
                if (!FileIO.DirectoryExists(dir))
                {
                    throw new InvalidOperationException($"持久化覆盖目录不存在: {dir}，根: {rootRelativePath}");
                }
            }
            foreach (string file in Files)
            {
                if (!FileIO.FileExists(file))
                {
                    throw new InvalidOperationException($"持久化覆盖文件不存在: {file}，根: {rootRelativePath}");
                }
            }
        }
    }
}

internal interface IPersistenceOperationApplier
{
    void Apply(
        LocalDiffResult diff,
        IReadOnlyList<AtomicOperation> operations,
        IReadOnlyCollection<string> exclusiveFilePaths,
        Action<IReadOnlyDictionary<string, FileStream>> beforeExecute);
}

internal sealed class PhysicalPersistenceOperationApplier : IPersistenceOperationApplier
{
    public void Apply(
        LocalDiffResult diff,
        IReadOnlyList<AtomicOperation> operations,
        IReadOnlyCollection<string> exclusiveFilePaths,
        Action<IReadOnlyDictionary<string, FileStream>> beforeExecute)
        => AtomicFileOperationExecutor.ExecuteBatch(operations, exclusiveFilePaths, beforeExecute);
}

// =============================================================================
// 工程模型 - 外观层
// =============================================================================

/// <summary>
/// 工程模型 - Persistence 层入口。
///
/// 职责：
/// 1. 从文件系统加载 → 内存实体（Load）
/// 2. 统一持久化（SaveChanges）
/// 3. 文件系统变更监听（RefreshFromFileSystem）
/// 4. 查询入口（TryGet/List/Enumerate）
///
/// 绝对禁止：
/// - 代码分析（Logic 层职责）
/// - 引用 analysis/ 层的任何类型
/// </summary>
public sealed class ProjectModel : IDisposable
{
    // ═══════════════════════════════════════════════════════════════
    // 不变量（Invariant）—— 违反这些前提下的任何"防御"代码都是垃圾：
    //   I1. 构造即加载完成，不存在"未加载"状态，Root 永远非空
    //   I3. Dispose() 之后只有 Timer/事件回调可能进入，公共 API 不应被调用
    //   I5. RefreshFromFileSystemCore 执行时 Root 必然非 null（由 I1 保证）
    // ═══════════════════════════════════════════════════════════════

    // 统一缓存：路径索引 + GUID 索引
    // 生命周期完全一致，仅在实体树构建/重建后全量重建
    private Dictionary<string, ProjectEntity> _byPath = new(StringComparer.Ordinal);
    private Dictionary<string, ProjectEntity> _byGuid = new(StringComparer.Ordinal);
    private readonly object _cacheLock = new();

    private readonly ReaderWriterLockSlim _refreshLock = new();

    // 剪枝会话脏路径：业务修改实体后经 MarkDirtyRecursive 传播（自底向上），
    // 下一次 SaveChanges 消费并清空。未标记实体在期望树收集时按剪枝声明处理。
    private readonly object _dirtyLock = new();
    private HashSet<string> _dirtyPaths = new(StringComparer.Ordinal);

    private readonly ProjectPersister _persister;
    private FileTreeSnapshot _lastSnapshot;
    private bool _disposed;
    private long _refreshVersion; // 每次刷新完成后递增（无论是否有文件变更）
    private long _lastModelRefreshSnapshotStartTicks;
    private bool _fileTreeChangedSubscribed;
    private bool _workspaceBlockStateSubscribed;
    private IReadOnlyList<GuidDuplicateIssue> _guidDuplicateIssues = [];

    /// <summary>模型刷新完成事件</summary>
    public event EventHandler<EventArgs>? ModelRefreshed;
    /// <summary>检测到文件变化事件（用于业务层撤销同步标记）</summary>
    public event EventHandler<EventArgs>? FileChangeDetected;
    /// <summary>工作区阻塞状态变更事件</summary>
    public event EventHandler<WorkspaceBlockStateChangedEventArgs>? WorkspaceBlockStateChanged;
    /// <summary>当前模型刷新版本号（每轮刷新完成后递增）</summary>
    public long RefreshVersion => Interlocked.Read(ref _refreshVersion);

    /// <summary>当前工作区是否被外部占用阻塞</summary>
    public bool IsWorkspaceBlocked => FileSystemCache.Instance.IsWorkspaceBlocked;

    public WorkspaceBlockStateSnapshot GetWorkspaceBlockState() => FileSystemCache.Instance.GetWorkspaceBlockState();

    /// <summary>工程根实体（由 I1 保证：构造即加载完成，永远非空）</summary>
    public ProjectRootEntity Root { get; private set; } = new();

    /// <summary>
    /// 设置根实体（仅供测试使用）。
    /// 正常流程应使用 ProjectModel.Load() 加载工程。
    /// </summary>
    internal void SetRootForTesting(ProjectRootEntity root)
    {
        lock (_cacheLock)
        {
            Root = root;
            RebuildCacheFromRoot(root);
        }
    }

    /// <summary>加载错误收集器</summary>
    public EntityLoadErrorCollector LoadErrors { get; }

    /// <summary>按路径索引的实体（只读）</summary>
    public IReadOnlyDictionary<string, ProjectEntity> EntitiesByPath
    {
        get
        {
            lock (_cacheLock)
            {
                return _byPath;
            }
        }
    }

    /// <summary>按 GUID 索引的实体（只读）</summary>
    public IReadOnlyDictionary<string, ProjectEntity> EntitiesByGuid
    {
        get
        {
            lock (_cacheLock)
            {
                return _byGuid;
            }
        }
    }

    public IReadOnlyList<GuidDuplicateIssue> GuidDuplicateIssues
    {
        get
        {
            lock (_cacheLock)
            {
                return _guidDuplicateIssues;
            }
        }
    }

    private ProjectModel(bool skipLoad, IPersistenceOperationApplier operationApplier)
    {
        _persister = new ProjectPersister(GetByPathFromCache, operationApplier);
        LoadErrors = new EntityLoadErrorCollector();
        _lastSnapshot = new FileTreeSnapshot { Root = new FileTreeNode(WorkspacePath.RootDirectory, string.Empty, 0) };

        if (!skipLoad)
        {
            FileSystemCache.FileTreeChanged += OnFileTreeChanged;
            _fileTreeChangedSubscribed = true;
            FileSystemCache.WorkspaceBlockStateChanged += OnWorkspaceBlockStateChanged;
            _workspaceBlockStateSubscribed = true;

            // 构造即加载（I1：不存在"未加载"状态）
            try
            {
                while (!LoadRootEntity())
                {
                    WaitForModelRefresh(RefreshVersion);
                }
            }
            catch (WorkspaceFileBlockedException ex)
            {
                Root = new();
                Log.For(LogModule.Core).Warning($"工程加载遇到受阻路径: {ex.RelativePath}");
            }
        }
        else
        {
            // 测试路径：先占位，后续 SetRootForTesting 会替换
            Root = new();
        }
    }

    /// <summary>加载工程（唯一公开构造入口，构造即完成文件系统加载）</summary>
    public static ProjectModel Load() => new(false, new PhysicalPersistenceOperationApplier());

    internal static ProjectModel LoadForTesting(IPersistenceOperationApplier operationApplier)
        => new(false, operationApplier);

    /// <summary>创建未加载的模型实例（仅供需要注入假根实体的单元测试使用）</summary>
    internal static ProjectModel CreateForTesting() => new(true, new PhysicalPersistenceOperationApplier());

    /// <summary>初始化工程根目录（绝对路径）</summary>
    public static void SetProjectRootFullPath(string projectRootFullPath)
        => FileIO.SetProjectRoot(projectRootFullPath);

    /// <summary>当前工程根完整路径（与 SetProjectRootFullPath 对称；工程根唯一持有者 = ProjectModel）</summary>
    public static string GetProjectRootFullPath()
        => FileSystemCache.Instance.ProjectRootFullPath;

    // =========================================================================
    // 加载实现
    // =========================================================================

    private bool LoadRootEntity()
    {
        ProjectRootEntity root = new();
        Root = root;
        if (!root.Rebuild(LoadErrors))
            return false;

        if (LoadErrors.HasFatal)
        {
            List<ToolError> fatalErrors = LoadErrors.Errors.Where(static e => e.Severity == ToolDiagnosticSeverity.Fatal).ToList();
            string errorDetails = string.Join("\n", fatalErrors.Select(static e => $"  - {e.Path}: {e.Message}"));
            throw new InvalidOperationException($"工程加载失败（{fatalErrors.Count} 个致命错误）：\n{errorDetails}");
        }

        _lastSnapshot = FileIO.GetFileTreeSnapshot();

        // 实体树构建完成后，全量重建缓存
        RebuildCacheFromRoot(root);

        // 扫描未被实体覆盖的文件和目录，生成警告
        ScanUncoveredPaths();
        return true;

    }

    // =========================================================================
    // 统一缓存重建
    // =========================================================================

    /// <summary>
    /// 全量重建缓存。
    /// 调用时机：实体树首次构建 或 局部重建完毕后。
    /// </summary>
    /// <param name="currentRoot">必须与被索引的实体树一致；测试入口传入显式引用以避免与 <see cref="Root"/> 字段的可见性竞态。</param>
    private void RebuildCacheFromRoot(ProjectRootEntity currentRoot)
    {

        lock (_cacheLock)
        {
            EntityCoverage newCoverage = currentRoot.GetAllCoverage();
            Dictionary<string, ProjectEntity> newByPath = new(StringComparer.Ordinal);
            Dictionary<string, ProjectEntity> newByGuid = new(StringComparer.Ordinal);

            foreach (KeyValuePair<string, ProjectEntity> kvp in newCoverage.Files.Concat(newCoverage.Directories))
            {
                string path = kvp.Key;
                ProjectEntity entity = kvp.Value;
                if (path == entity.实体_完整相对路径标识)
                    newByPath[path] = entity;
            }

            foreach (ProjectEntity entity in newByPath.Values.OrderBy(static e => e.实体_完整相对路径标识, StringComparer.Ordinal))
            {
                if (entity is not IHasGuid hasGuid)
                    continue;
                newByGuid.TryAdd(hasGuid.Guid, entity);
            }

            _byPath = newByPath;
            _byGuid = newByGuid;
            _guidDuplicateIssues = currentRoot.GuidDuplicateIssues;
            FileTreeBinding.Instance.SetCoverage(newCoverage);
        }
    }

    /// <summary>
    /// 扫描未被实体覆盖的文件和目录，生成警告。
    /// 调用时机：实体树首次构建完成后。
    /// </summary>
    private void ScanUncoveredPaths()
    {
        FileTreeDiagnosticScanner scanner = new(this);
        FileTreeDiagnostic diagnostic = scanner.Scan();

        // 为未覆盖的文件生成警告
        foreach (string file in diagnostic.UncoveredFiles)
        {
            LoadErrors.AddError(file, "文件未被任何实体覆盖", ToolDiagnosticSeverity.Warning);
        }

        // 为未覆盖的目录生成警告
        foreach (string dir in diagnostic.UncoveredDirectories)
        {
            LoadErrors.AddError(dir, "目录未被任何实体覆盖", ToolDiagnosticSeverity.Warning);
        }
    }

    // =========================================================================
    // 持久化入口
    // =========================================================================

    /// <summary>
    /// 统一持久化入口。
    ///
    /// 约束：
    /// - 单起点：每次保存指定一个起点实体
    /// - 业务调用方应先合并修改，原则上每轮业务只调用一次；需要修改多处时应上提到共同父实体
    /// - SaveChanges 不机械禁止同一业务多次调用，但每次都会落盘并通过统一 timer 排期刷新
    /// - 级联写：起点实体及其受影响子树写入期望树
    /// - Diff 落地：只允许四类原子操作，串行执行，无重试无回退
    ///
    /// 时序设计：
    /// - 写完只排期缓存层统一 timer
    /// - 随后等待下一次 ModelRefreshed；模型仍只能由 FileTreeChanged 事件驱动刷新
    ///
    /// 业务层绝对禁止自行调用 RefreshFromFileSystem 或自行包裹写作用域造成分裂刷新路径。
    /// </summary>
    /// <returns>实际受影响的相对路径列表（包含所有实际执行的文件和目录操作）</returns>
    public IReadOnlyList<string> SaveChanges<T>(T startingEntity) where T : ProjectEntity, IWritableEntity
        => SaveChanges((ProjectEntity)startingEntity);

    /// <summary>
    /// 统一持久化入口（非泛型重载）。
    /// 供内部使用，运行时验证实体实现了 IWritableEntity。
    /// 保存后等待下一次模型刷新；实际刷新由缓存层统一 timer 触发 FileTreeChanged。
    /// </summary>
    internal IReadOnlyList<string> SaveChanges(ProjectEntity startingEntity)
    {
        if (startingEntity is not IWritableEntity)
            throw new InvalidOperationException($"实体未实现 IWritableEntity: {startingEntity.GetType().Name}");

        // SaveChanges 是唯一写作用域入口；业务层禁止自行包裹写作用域或分散刷新模型。
        // 缓存在 NotifyLocalMutationCompleted→ScheduleRefresh 链路自治刷新快照，禁止外界驱动。
        // 模型层只消费 FileTreeChanged，不干预缓存快照生命周期。
        // =====================================================================
        // 架构规则 —— 数据单向流动：缓存层 → 实体模型层。绝对禁止反向驱驶！
        //   缓存层唯一入口：统一计时器 → ProcessPendingInvalidations →
        //                   RefreshStableSnapshot → FileTreeChanged（触发事件）。
        //   模型层唯一入口：OnFileTreeChanged（响应 FileTreeChanged 事件）→ 全量重建。
        //   SaveChanges 只等待 ModelRefreshed，禁止同步直刷模型。
        //   绝对禁止 SaveChanges 内调用 RefreshFromFileSystemCore/Inner 分裂出第二条路径！
        //   绝对禁止局部修补式重建！绝对禁止越级篡改缓存快照生命周期！
        // =====================================================================
        return PersistAndWaitForRefresh(startingEntity, RefreshVersion, versionGuard: null)
            ?? throw new InvalidOperationException("PersistAndWaitForRefresh 在无版本守卫时不应返回 null");
    }

    /// <summary>
    /// 带版本守卫的保存。在 _refreshLock 内同时校验 RefreshVersion 和同路径实体版本，
    /// 任一不匹配则跳过持久化并返回 false。用于视图重建等场景：writable 创建到保存之间
    /// 模型可能被 FileSystemCache 定时器或 MCP 写操作刷新，需要优雅跳过而非抛异常。
    /// </summary>
    public (IReadOnlyList<string> Affected, bool VersionMatched) SaveChangesIfVersionMatches(
        ProjectEntity startingEntity,
        long expectedRefreshVersion)
    {
        if (startingEntity is not IWritableEntity)
            throw new InvalidOperationException($"实体未实现 IWritableEntity: {startingEntity.GetType().Name}");

        string entityPath = WorkspacePath.RequireRelativePath(startingEntity.实体_完整相对路径标识);

        IReadOnlyList<string>? result = PersistAndWaitForRefresh(startingEntity, expectedRefreshVersion,
            versionGuard: () =>
            {
                if (Interlocked.Read(ref _refreshVersion) != expectedRefreshVersion)
                    return false;
                ProjectEntity? readOnly = Find(entityPath);
                return readOnly == null || startingEntity.Version == readOnly.Version;
            });

        return result != null ? (result, true) : (Array.Empty<string>(), false);
    }

    /// <summary>
    /// 持久化核心流程：获取写锁 → DrainPendingTransactions + Validate + Persist → 等待模型刷新。
    /// versionGuard 在写锁内、持久化前执行；返回 false 时跳过持久化，方法返回 null。
    /// </summary>
    private IReadOnlyList<string>? PersistAndWaitForRefresh(
        ProjectEntity startingEntity,
        long refreshVersionBeforeSave,
        Func<bool>? versionGuard)
    {
        IReadOnlyList<string> result = Array.Empty<string>();
        bool wroteToFileSystem = false;

        _refreshLock.EnterWriteLock();
        try
        {
            if (versionGuard != null && !versionGuard())
                return null;

            IReadOnlyList<string> drained = _persister.DrainPendingTransactions();
            wroteToFileSystem = drained.Count > 0;
            ValidateWritableEntityChain(startingEntity);
            result = _persister.Persist(startingEntity, ConsumeDirtyPaths());
            wroteToFileSystem |= result.Count > 0;
        }
        finally
        {
            _refreshLock.ExitWriteLock();
        }

        if (wroteToFileSystem)
            WaitForModelRefresh(refreshVersionBeforeSave);

        return result;
    }

    /// <summary>
    /// 业务修改实体后自底向上传播剪枝脏标记：
    /// 实体自身 + 全部子孙路径（修改点的子树必然被修改）+ 全部祖先路径（剪枝判定沿树递归，祖先必须可见）。
    /// 必须在本轮 SaveChanges 之前调用（同步顺序）。
    /// 剪枝语义：标记路径对应的实体在期望树收集时全量序列化，未标记实体按剪枝声明处理。
    /// </summary>
    internal void MarkDirtyRecursive(ProjectEntity entity)
    {
        // 修改点自身 + 全部子孙路径（修改点的子树必然被修改）
        void CollectSubtreePaths(ProjectEntity node, List<string> nodePaths)
        {
            nodePaths.Add(node.实体_完整相对路径标识);
            if (node is not IHasChildren<ProjectEntity> hasChildren)
                return;

            foreach (ProjectEntity childNode in hasChildren.Children)
                CollectSubtreePaths(childNode, nodePaths);
        }

        List<string> paths = new(capacity: 16);
        CollectSubtreePaths(entity, paths);

        // 全部祖先路径（剪枝判定沿树递归，祖先必须可见）
        ProjectEntity current = entity;
        while (current is ChildProjectEntity child)
        {
            paths.Add(child.Parent.实体_完整相对路径标识);
            current = child.Parent;
        }

        lock (_dirtyLock)
        {
            _dirtyPaths.UnionWith(paths);
        }
    }

    /// <summary>消费本次 SaveChanges 的剪枝脏路径（取出并清空会话）。</summary>
#pragma warning disable TK006 // 锁内原子"取出并清空"，内联会破坏原子性
    private HashSet<string> ConsumeDirtyPaths()
#pragma warning restore TK006
    {
        lock (_dirtyLock)
        {
            HashSet<string> session = _dirtyPaths;
            _dirtyPaths = new HashSet<string>(StringComparer.Ordinal);
            return session;
        }
    }

    /// <summary>
    /// 保存后等待模型刷新：通知缓存层 → 等待 ModelRefreshed 事件。
    /// </summary>
    private void WaitForModelRefresh(long refreshVersionBeforeSave)
    {
        object waitLock = new();
        void OnModelRefreshed(object? sender, EventArgs e)
        {
            lock (waitLock) { Monitor.PulseAll(waitLock); }
        }

        ModelRefreshed += OnModelRefreshed;
        try
        {
            long saveNotificationTicks = FileSystemCache.Instance.NotifyLocalMutationCompleted();
            lock (waitLock)
            {
                while (Interlocked.Read(ref _refreshVersion) <= refreshVersionBeforeSave ||
                       Interlocked.Read(ref _lastModelRefreshSnapshotStartTicks) < saveNotificationTicks)
                {
                    // 受阻态下模型刷新被 OnUnifiedTimerTick 的 `if (!_isWorkspaceBlocked)` 门控挂起，
                    // FileTreeChanged → ModelRefreshed 永不触发，Monitor.Wait 会永久阻塞（曾致 MCP 卡死）。
                    // 与 McpViewWorkspace.WaitUntilSynchronizedLocked 对称：受阻即抛，禁止永久挂起。
                    if (FileSystemCache.Instance.IsWorkspaceBlocked)
                    {
                        throw new WorkspaceFileBlockedException(
                            WorkspacePath.RootDirectory,
                            "等待模型刷新期间工作区进入受阻状态",
                            WorkspaceFileBlockedException.DefaultBlockMessage);
                    }
                    Monitor.Wait(waitLock, 1000);
                }
            }
        }
        finally
        {
            ModelRefreshed -= OnModelRefreshed;
        }
    }

    /// <summary>
    /// 验证可写实体的父级链条。
    /// 检查1：必须是可写工程根，或者有父级
    /// 检查2：父级如果存在，必须是只读实体（保存必须保存紧挨着只读主干的那一层）
    /// </summary>
    private static void ValidateWritableEntityChain(ProjectEntity entity)
    {
        // 根实体（无可写父级）必须是可写工程根
        if (entity is WritableProjectRootEntity)
            return;

        // 非根实体必须有父级，且父级必须是只读实体
        if (entity is not ChildProjectEntity child)
        {
            throw new InvalidOperationException($"可写实体必须有父级，或必须是可写工程根。当前实体: {entity.实体_完整相对路径标识}");
        }

        if (child.Parent is IWritableEntity)
        {
            throw new InvalidOperationException(
                $"保存必须保存紧挨着只读主干的那一层，禁止在父级也可写的状态下保存。" +
                $"当前实体: {entity.实体_完整相对路径标识}, 父级: {child.Parent.实体_完整相对路径标识}");
        }
    }

    // =========================================================================
    // 查询操作
    // =========================================================================

    /// <summary>检查指定路径是否存在实体</summary>
    public bool EntityExists(string relativePath) => Find(relativePath) != null;

    /// <summary>按路径获取实体</summary>
    public ProjectEntity? Find(string relativePath)
    {
        string path = WorkspacePath.RequireRelativePath(relativePath);
        lock (_cacheLock)
        {
            return _byPath.GetValueOrDefault(path);
        }
    }

    /// <summary>按路径获取指定类型实体</summary>
    public T? Find<T>(string relativePath) where T : ProjectEntity
    {
        return Find(relativePath) is T t ? t : null;
    }

    /// <summary>按 GUID 获取实体</summary>
    public ProjectEntity? FindByGuid(string guid)
    {
        lock (_cacheLock)
        {
            return _byGuid.GetValueOrDefault(guid);
        }
    }

    /// <summary>按 GUID 获取指定类型实体</summary>
    public T? FindByGuid<T>(string guid) where T : ProjectEntity
    {
        return FindByGuid(guid) is T t ? t : null;
    }

    /// <summary>按类型枚举所有实体</summary>
    public IReadOnlyList<T> List<T>() where T : ProjectEntity
    {
        lock (_cacheLock)
        {
            return _byPath.Values.OfType<T>().ToArray();
        }
    }

    public IReadOnlyList<MapEntity> ListMaps() => List<MapEntity>();
    public IReadOnlyList<MechEntity> ListMechs() => List<MechEntity>();
    public IReadOnlyList<DeviceEntity> ListDevices() => List<DeviceEntity>();
    public IReadOnlyList<CodeRepoScriptEntity> ListCodeRepoScripts() => List<CodeRepoScriptEntity>();
    public IReadOnlyList<CanvasEntity> ListCanvases() => List<CanvasEntity>();

    /// <summary>枚举指定路径前缀下的所有实体</summary>
    public IReadOnlyList<T> EnumerateEntitiesUnderPath<T>(string pathPrefix) where T : ProjectEntity
    {
        lock (_cacheLock)
        {
            return _byPath.Values.OfType<T>()
                       .Where(e => e.实体_完整相对路径标识.StartsWith(pathPrefix, StringComparison.Ordinal))
                       .ToArray();
        }
    }

    /// <summary>枚举所有零件（扁平列表）</summary>
    public IEnumerable<DeviceEntityBase> EnumerateAllDevices()
    {
        foreach (MechEntity mech in ListMechs())
            foreach (DeviceEntityBase device in mech.Devices)
                yield return device;

        foreach (MapEntity map in ListMaps())
            foreach (DeviceEntityBase device in map.Devices)
                yield return device;
    }

    // =========================================================================
    // 文件系统刷新
    // =========================================================================

    /// <summary>
    /// 异步模型刷新入口。仅由缓存层 FileTreeChanged 事件触发。
    /// 数据单向流动：缓存快照刷新 → FileTreeChanged → 模型全量重建。
    /// 绝对禁止业务层直接调用本方法驱驶模型刷新。
    /// </summary>
    private void RefreshFromFileSystem(long snapshotStartTicks)
    {
        try
        {
            RefreshFromFileSystemCore(snapshotStartTicks);
        }
        catch (WorkspaceFileBlockedException ex)
        {
            FileSystemCache.Instance.ReportBlockedException(ex);
        }
    }

    /// <summary>
    /// 模型全量重建核心。
    /// 只读取缓存层当前稳定快照，基于快照 diff 全量重建实体树。
    /// 不做局部修补，不接持久化泄露路径。
    /// </summary>
    private void RefreshFromFileSystemCore(long snapshotStartTicks)
    {
        _refreshLock.EnterWriteLock();
        try
        {
            RefreshFromFileSystemCoreInner(snapshotStartTicks);
        }
        finally
        {
            _refreshLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 从缓存层稳定快照全量重建实体树。
    /// 快照来源只能由缓存层统一 timer 决定。
    /// 模型层不干预快照生命周期，只消费快照。
    /// 绝对禁止 knownChangedPaths 式的局部修补 —— 模型全量由缓存快照决定。
    /// </summary>
    private void RefreshFromFileSystemCoreInner(long snapshotStartTicks)
    {
        FileTreeSnapshot newSnapshot = FileIO.GetFileTreeSnapshot();
        bool needsInitialLoad;
        lock (_cacheLock)
        {
            needsInitialLoad = _byPath.Count == 0;
        }

        if (needsInitialLoad)
        {
            if (!LoadRootEntity())
                return;
            UpdateCriticalReferences();
            Interlocked.Exchange(ref _lastModelRefreshSnapshotStartTicks, snapshotStartTicks);
            Interlocked.Increment(ref _refreshVersion);
            ModelRefreshed?.Invoke(this, EventArgs.Empty);
            return;
        }

        FileTreeChangeImpact impact = FileTreeBinding.Instance.AnalyzeChanges(_lastSnapshot, newSnapshot);

        if (!impact.HasImpact)
        {
            _lastSnapshot = newSnapshot;
            Interlocked.Exchange(ref _lastModelRefreshSnapshotStartTicks, snapshotStartTicks);
            // 无实体变化：不递增 RefreshVersion——版本号是内容版本，空转刷新制造伪变更会让
            // 下游（视图重建/索引重建/Lint/树推送）做无消费者的全量重建，并引发版本守卫伪失败。
            // ModelRefreshed 照发：SaveChanges 的等待唤醒依赖它（且其保存必然产生 HasImpact 的刷新）。
            ModelRefreshed?.Invoke(this, EventArgs.Empty);
            return;
        }

        FileTreeBinding.Instance.RefreshFilePaths(newSnapshot.Root);

        HashSet<ProjectEntity> rebuildTargets = [];
        if (impact.NewRelativePaths.Count > 0 || impact.ModifiedRelativePaths.Any(IsGuidFileRelativePath))
            rebuildTargets.Add(Root);
        foreach (ProjectEntity entity in impact.EntitiesToUnload)
            rebuildTargets.Add(RequireRebuildTarget(entity));
        foreach (ProjectEntity entity in impact.EntitiesToReload)
            rebuildTargets.Add(RequireRebuildTarget(entity));

        foreach (ProjectEntity target in rebuildTargets)
        {
            if (target is not IReadOnlyEntity readOnly)
            {
                throw new InvalidOperationException($"刷新重建目标必须是只读实体: {target.GetType().Name} 路径={target.实体_完整相对路径标识}");
            }
            if (!readOnly.Rebuild(LoadErrors))
                return;
        }

        // 局部重建完毕后，全量重建缓存
        RebuildCacheFromRoot(Root);

        _lastSnapshot = newSnapshot;
        Interlocked.Exchange(ref _lastModelRefreshSnapshotStartTicks, snapshotStartTicks);
        Interlocked.Increment(ref _refreshVersion);

        // 触发刷新完成事件，通知所有订阅者（Logic 层等）
        ModelRefreshed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 模型刷新后更新关键文件引用集合（setting.json 指向的表格资源路径）。
    /// internal：供 tests 白盒直接验证"guid 断裂查找无资源不崩溃"的业务约束。
    /// </summary>
    internal void UpdateCriticalReferences()
    {
        List<string> paths = new(capacity: 3);
        if (Root.SettingJson is { } setting)
        {
            AddReferenced(setting.PaletteExcel, paths);
            AddReferenced(setting.LanguageExcel, paths);
            AddReferenced(setting.CoverPic, paths);
        }

        CriticalFilePolicy.SetReferencedCriticalPaths(paths);
    }

    private void AddReferenced(string? guid, List<string> paths)
    {
        // guid 合法但资源可能已删除（引用断裂）：GetValueOrDefault 查不到即跳过而非抛异常，
        // 见 CriticalFileTieringTests.SettingJson_ReferencedGuid_Missing_Should_Not_Crash
        if (GuidText.Validate(guid) && _byGuid.GetValueOrDefault(guid) is { } entity)
            paths.Add(entity.实体_完整相对路径标识);
    }

    private static bool IsGuidFileRelativePath(string relativePath)
    {
        string fileName = RelativePathUtils.GetLastSegment(relativePath);
        return fileName.EndsWith(ProjectEntityLayout.GuidExt, StringComparison.Ordinal);
    }

    private static ProjectEntity RequireRebuildTarget(ProjectEntity entity)
    {
        if (entity is ProjectRootEntity)
        {
            return entity;
        }

        if (entity is not ChildProjectEntity child)
        {
            throw new InvalidOperationException($"非根实体缺少父级，无法重建: {entity.GetType().Name} 路径={entity.实体_完整相对路径标识}");
        }

        ProjectEntity parent = child.Parent;
        if (parent is not IReadOnlyEntity)
        {
            throw new InvalidOperationException($"重建父级必须是只读实体: {parent.GetType().Name} 路径={parent.实体_完整相对路径标识}");
        }

        return parent;
    }

    /// <summary>
    /// 缓存层自治刷新后的回调 —— 模型重建的唯一入口。
    /// 数据单向流动：缓存 FileTreeChanged 事件 → 模型重建。
    /// 绝对禁止 SaveChanges、受阻解除、视图读取绕过本入口直刷模型。
    /// </summary>
    private void OnFileTreeChanged(object? sender, FileTreeChangedEventArgs e)
    {
        if (_disposed) return;
        try
        {
            FileChangeDetected?.Invoke(this, EventArgs.Empty);
            RefreshFromFileSystem(e.SnapshotStartTicks);
        }
        catch (Exception ex)
        {
            Log.For(LogModule.Core).Exception(ex, "模型刷新失败");
        }
    }

    private void OnWorkspaceBlockStateChanged(object? sender, WorkspaceBlockStateChangedEventArgs e)
    {
        WorkspaceBlockStateChanged?.Invoke(this, e);
        if (e.State.IsBlocked) return;

        // 受阻解除后只重试持久化队列；重试产生的文件变化仍通过统一 timer 触发模型刷新。
        // 绝对禁止在这里调用 RefreshFromFileSystemCore 形成受阻解除直刷路径。
        try
        {
            IReadOnlyList<string> affected = _persister.DrainPendingTransactions();
            if (affected.Count > 0)
            {
                FileSystemCache.Instance.NotifyLocalMutationCompleted();
            }
        }
        catch (WorkspaceFileBlockedException)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.For(LogModule.Core).Exception(ex, "持久化队列重试失败");
        }
    }

    // =========================================================================
    // 清空与释放
    // =========================================================================

    /// <summary>清空模型</summary>
    public void Clear()
    {
        lock (_cacheLock)
        {
            _byPath = new Dictionary<string, ProjectEntity>(StringComparer.Ordinal);
            _byGuid = new Dictionary<string, ProjectEntity>(StringComparer.Ordinal);
        }
        _lastSnapshot = new FileTreeSnapshot { Root = new FileTreeNode(WorkspacePath.RootDirectory, string.Empty, 0) };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_fileTreeChangedSubscribed)
        {
            FileSystemCache.FileTreeChanged -= OnFileTreeChanged;
            _fileTreeChangedSubscribed = false;
        }
        if (_workspaceBlockStateSubscribed)
        {
            FileSystemCache.WorkspaceBlockStateChanged -= OnWorkspaceBlockStateChanged;
            _workspaceBlockStateSubscribed = false;
        }
        _refreshLock.Dispose();
    }

    public void EnterReadLock() => _refreshLock.EnterReadLock();
    public void ExitReadLock() => _refreshLock.ExitReadLock();

    private ProjectEntity? GetByPathFromCache(string relativePath)
    {
        string path = WorkspacePath.RequireRelativePath(relativePath);
        lock (_cacheLock)
        {
            return _byPath.GetValueOrDefault(path);
        }
    }
}
