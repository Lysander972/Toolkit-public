using System.Reflection;
using Toolkit.Core.Infra;
using Xunit;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Common;

/// <summary>
/// 文件树构建的链接归并测试。用 FakeFileIdentity 模拟链接语义,不依赖真实文件系统链接能力。
/// </summary>
[Collection("FileIO")]
public sealed class FileSystemCacheLinkDedupTests : TempDirectoryTestFixture
{
    public FileSystemCacheLinkDedupTests() : base("Toolkit.Core.Tests.FileSystemCacheLinkDedup")
    {
    }

    /// <summary>
    /// 两条路径解析到同一物理 FileId(hardlink 语义)→ 文件树去重为单节点。
    /// </summary>
    [Fact]
    public void SameFileId_Should_Dedup_To_Single_Node()
    {
        WriteFile("CLAUDE.md", "content");
        WriteFile("AGENTS.md", "content");

        FileId sameId = new(0x12345678u, 100UL);
        Dictionary<string, FileId> ids = new(StringComparer.Ordinal)
        {
            [FullPath("CLAUDE.md")] = sameId,
            [FullPath("AGENTS.md")] = sameId,
        };

        FileSystemCache.InitializeForTesting(TempDir, new FakeFileIdentity(ids, new HashSet<string>(StringComparer.Ordinal)));

        FileTreeSnapshot snapshot = FileSystemCache.Instance.GetFileTreeSnapshot(WorkspacePath.RootDirectory);

        Assert.Single(snapshot.Root.Files);
    }

    /// <summary>
    /// reparse point(symlink/junction)→ 从文件树排除;非 reparse 目标保留。
    /// </summary>
    [Fact]
    public void ReparsePoint_Should_Be_Excluded()
    {
        WriteFile("CLAUDE.md", "content");
        WriteFile("AGENTS.md", "content");

        HashSet<string> reparse = new(StringComparer.Ordinal) { FullPath("AGENTS.md") };
        Dictionary<string, FileId> ids = new(StringComparer.Ordinal)
        {
            [FullPath("CLAUDE.md")] = new FileId(1u, 1UL),
        };

        FileSystemCache.InitializeForTesting(TempDir, new FakeFileIdentity(ids, reparse));

        FileTreeSnapshot snapshot = FileSystemCache.Instance.GetFileTreeSnapshot(WorkspacePath.RootDirectory);

        Assert.Contains("CLAUDE.md", snapshot.Root.Files);
        Assert.DoesNotContain("AGENTS.md", snapshot.Root.Files);
    }

    [Fact]
    public void FileDeleted_While_Reading_FileId_Should_Invalidate_Snapshot()
    {
        WriteFile("temp/7aa53d15574c46d8b966d60c34ac4e10.webp", "content");

        string vanishedFullPath = FullPath("temp/7aa53d15574c46d8b966d60c34ac4e10.webp");
        FakeFileIdentity fileIdentity = new(
            new Dictionary<string, FileId>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal) { vanishedFullPath });
        FileSystemCache.InitializeForTesting(TempDir, fileIdentity);

        FileTreeSnapshot snapshot = FileSystemCache.Instance.GetFileTreeSnapshot(WorkspacePath.RootDirectory);

        Assert.Empty(snapshot.Root.Children);
        Assert.Empty(snapshot.Root.Files);
    }

    /// <summary>
    /// 读 FileId 时遇访问拒绝(文件被外部进程独占,OS 返回 ERROR_ACCESS_DENIED)→ 进入受阻状态而非崩溃。
    /// 对应 UnauthorizedAccessException 与 IOException(32/33) 在 Windows 上同为"文件被占用"的合法映射。
    /// </summary>
    [Fact]
    public void AccessDenied_While_Reading_FileId_Should_Enter_Blocked_State()
    {
        WriteFile("res/blocked.png", "content");

        string blockedFullPath = FullPath("res/blocked.png");
        HashSet<string> accessDenied = new(StringComparer.Ordinal) { blockedFullPath };
        FileSystemCache.InitializeForTesting(TempDir, new FakeFileIdentity(
            new Dictionary<string, FileId>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            accessDenied));

        Assert.Throws<WorkspaceFileBlockedException>(
            () => FileSystemCache.Instance.GetFileTreeSnapshot(WorkspacePath.RootDirectory));
        Assert.True(FileSystemCache.Instance.IsWorkspaceBlocked);
    }

    /// <summary>
    /// 统一计时器回调内出现未预见异常时,绝不能逃逸到线程池(否则 .NET 会终止进程)。
    /// 注入让 ReadFileId 抛未分类异常的 IFileIdentity,反射驱动 OnUnifiedTimerTick,断言不向调用者抛。
    /// </summary>
    [Fact]
    public void UnifiedTimerTick_Should_Swallow_Unexpected_Exception()
    {
        WriteFile("res/file.png", "content");
        FileSystemCache.InitializeForTesting(TempDir, new ThrowingFileIdentity());

        // 让 OnUnifiedTimerTick 走到 ProcessPendingInvalidations:_refreshPending=true 且防抖已过期
        FieldInfo refreshPendingField = typeof(FileSystemCache).GetField("_refreshPending", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("未找到字段 _refreshPending");
        refreshPendingField.SetValue(FileSystemCache.Instance, true);
        FieldInfo eventTicksField = typeof(FileSystemCache).GetField("_lastFileSystemEventTicks", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("未找到字段 _lastFileSystemEventTicks");
        eventTicksField.SetValue(FileSystemCache.Instance, 0L);

        MethodInfo tick = typeof(FileSystemCache).GetMethod("OnUnifiedTimerTick", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("未找到方法 OnUnifiedTimerTick");
        tick.Invoke(FileSystemCache.Instance, new object?[] { null });
    }

    private void WriteFile(string relativePath, string content)
    {
        string fullPath = SystemIOWrapper.Combine(TempDir, relativePath);
        string directoryFullPath = SystemIOWrapper.GetParentDirectory(fullPath);
        if (!SystemIOWrapper.DirectoryExists(directoryFullPath))
        {
            SystemIOWrapper.CreateDirectory(directoryFullPath);
        }

        SystemIOWrapper.WriteAllText(fullPath, content);
    }

    private string FullPath(string relativePath)
        => SystemIOWrapper.Combine(TempDir, relativePath);

    /// <summary>
    /// 假文件身份:按 fullPath 配置 FileId 与 reparse 标记(模拟链接,无需真实文件系统链接)。
    /// </summary>
    private sealed class FakeFileIdentity : IFileIdentity
    {
        private readonly Dictionary<string, FileId> _ids;
        private readonly HashSet<string> _reparse;
        private readonly HashSet<string> _deletedOnFileId;
        private readonly HashSet<string> _accessDeniedOnFileId;

        public FakeFileIdentity(Dictionary<string, FileId> ids, HashSet<string> reparse)
            : this(ids, reparse, new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal))
        {
        }

        public FakeFileIdentity(Dictionary<string, FileId> ids, HashSet<string> reparse, HashSet<string> deletedOnFileId)
            : this(ids, reparse, deletedOnFileId, new HashSet<string>(StringComparer.Ordinal))
        {
        }

        public FakeFileIdentity(Dictionary<string, FileId> ids, HashSet<string> reparse, HashSet<string> deletedOnFileId, HashSet<string> accessDeniedOnFileId)
        {
            _ids = ids;
            _reparse = reparse;
            _deletedOnFileId = deletedOnFileId;
            _accessDeniedOnFileId = accessDeniedOnFileId;
        }

        public FileId GetFileId(string fullPath)
        {
            string normalized = Normalize(fullPath);
            if (_accessDeniedOnFileId.Contains(normalized))
            {
                throw new UnauthorizedAccessException($"模拟文件被外部进程独占(拒绝只读访问): {normalized}");
            }
            if (_deletedOnFileId.Contains(normalized))
            {
                throw new FileNotFoundException("File disappeared while reading FileId.", normalized);
            }

            return _ids[normalized];
        }

        public bool IsReparsePoint(string fullPath) => _reparse.Contains(Normalize(fullPath));

        private static string Normalize(string path) => path.Replace('\\', '/');
    }

    /// <summary>
    /// 对所有 GetFileId 调用抛未分类异常,模拟刷新链路上未预见的运行时错误。
    /// </summary>
    private sealed class ThrowingFileIdentity : IFileIdentity
    {
        public FileId GetFileId(string fullPath) => throw new InvalidOperationException("模拟未预见异常");
        public bool IsReparsePoint(string fullPath) => false;
    }
}
