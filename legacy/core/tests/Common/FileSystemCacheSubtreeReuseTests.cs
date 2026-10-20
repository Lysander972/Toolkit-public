using System.Reflection;
using Toolkit.Core.Infra;
using Xunit;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Common;

// =============================================================================
// 成员哈希复用（子树递归哈希）回归套件
//
// 钉死三条契约：
// 1. 无变化重扫：逐文件 GetFileId 系统调用必须为零（成员哈希短路）；
// 2. 深层文件变化：仅该文件所在目录失去复用资格（GetFileId 仅针对该层成员），
//    父级/兄弟目录照常复用——父级判定绝不替代子级真实变更；
// 3. 子树递归哈希：无变化重扫相等，深层变化必不等。
// =============================================================================

[Collection("FileIO")]
public sealed class FileSystemCacheSubtreeReuseTests : TempDirectoryTestFixture
{
    public FileSystemCacheSubtreeReuseTests() : base("Toolkit.Core.Tests.FileSystemCacheSubtreeReuse")
    {
    }

    private sealed class CountingFileIdentity : IFileIdentity
    {
        private int _getFileIdCalls;

        public int GetFileIdCalls => _getFileIdCalls;

        public FileId GetFileId(string fullPath)
        {
            Interlocked.Increment(ref _getFileIdCalls);
            return RealFileIdentity.Instance.GetFileId(fullPath);
        }

        public bool IsReparsePoint(string fullPath) => RealFileIdentity.Instance.IsReparsePoint(fullPath);
    }

    private static void InvokeRefreshStableSnapshot(string source)
    {
        MethodInfo method = typeof(FileSystemCache).GetMethod("RefreshStableSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("未找到方法 RefreshStableSnapshot");
        method.Invoke(FileSystemCache.Instance, new object?[] { source });
    }

    [Fact]
    public void NoChangeRescan_SkipsFileIdSyscalls_DeepChange_RescansOnlyThatDirectory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        WriteFile("dirA/sub/a1.txt", "a1");
        WriteFile("dirB/b1.txt", "b1");
        CountingFileIdentity identity = new();
        FileSystemCache.InitializeForTesting(TempDir, identity);

        FileTreeSnapshot first = FileSystemCache.Instance.GetFileTreeSnapshot(WorkspacePath.RootDirectory);
        // 全量路径每个文件在进入 + 退出两次 DedupCanonicalFiles 中各算一次 FileId（2 文件 × 2 = 4）。
        int baseline = identity.GetFileIdCalls;
        Assert.Equal(4, baseline);

        // 无变化重扫：逐文件 GetFileId 必须为零（成员哈希复用）；子树哈希相等。
        InvokeRefreshStableSnapshot("test-无变化重扫");
        FileTreeSnapshot second = FileSystemCache.Instance.GetFileTreeSnapshot(WorkspacePath.RootDirectory);
        Assert.Equal(baseline, identity.GetFileIdCalls);
        Assert.Equal(first.Root.SubtreeHash, second.Root.SubtreeHash);

        // 摘掉 watcher：隔绝 FSW 事件 → 防抖计时器的后台刷新，保证 GetFileId 计数断言确定。
        FieldInfo watcherField = typeof(FileSystemCache).GetField("_globalWatcher", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("未找到字段 _globalWatcher");
        if (watcherField.GetValue(FileSystemCache.Instance) is FileSystemWatcher watcher)
        {
            watcher.Dispose();
        }
        watcherField.SetValue(FileSystemCache.Instance, null);

        // 深层文件变化（dirA/sub/a1.txt）：仅 dirA/sub 失去复用资格 → 进入+退出恰好 2 次 GetFileId；
        // 根、dirA、dirB 全部复用；子树哈希必须变化。
        WriteFile("dirA/sub/a1.txt", "changed");
        InvokeRefreshStableSnapshot("test-深层单文件变化");
        FileTreeSnapshot third = FileSystemCache.Instance.GetFileTreeSnapshot(WorkspacePath.RootDirectory);
        Assert.Equal(baseline + 2, identity.GetFileIdCalls);
        Assert.NotEqual(first.Root.SubtreeHash, third.Root.SubtreeHash);
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
}
