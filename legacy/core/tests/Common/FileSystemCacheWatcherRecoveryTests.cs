using System.Reflection;
using Toolkit.Core.Infra;
using Xunit;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Common;

// =============================================================================
// FSW 错误自愈「永久卡死」回归套件
//
// 覆盖 FileSystemCache 的 watcher 生命周期健壮性（根因 A/C）：
// - A：watcher 死亡残留（半死/已 Dispose）后，RefreshStableSnapshot 必须能重建，不能"非 null 即健康"
// - C：OnWatcherError 必须忽略过时 sender、并发下保持"单个活实例"不变量
//
// FileSystemWatcher 是 sealed、无抽象 seam，只能反射操作 _globalWatcher 与私有方法。
// 每个测试通过 SetProjectRootFullPath 重建 FileSystemCache 单例获得干净初始态。
// =============================================================================

[Collection("FileIO")]
public sealed class FileSystemCacheWatcherRecoveryTests : TempDirectoryTestFixture
{
    public FileSystemCacheWatcherRecoveryTests() : base("Toolkit.Core.Tests.FileSystemCacheWatcherRecovery")
    {
    }

    // --------------------------------------------------------------------------
    // 反射 helper
    // --------------------------------------------------------------------------

    private static FileSystemWatcher? GetGlobalWatcher()
    {
        object? raw = typeof(FileSystemCache).GetField("_globalWatcher", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(FileSystemCache.Instance);
        return raw is FileSystemWatcher watcher ? watcher : null;
    }

    private static void SetGlobalWatcher(FileSystemWatcher? watcher)
        => typeof(FileSystemCache).GetField("_globalWatcher", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.SetValue(FileSystemCache.Instance, watcher);

    private static void InvokeOnWatcherError(object sender, ErrorEventArgs args)
        => typeof(FileSystemCache).GetMethod("OnWatcherError", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(FileSystemCache.Instance, new object?[] { sender, args });

    private static void InvokeRefreshStableSnapshot(string source)
        => typeof(FileSystemCache).GetMethod("RefreshStableSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(FileSystemCache.Instance, new object?[] { source });

    /// <summary>
    /// 建立全新 FileSystemCache 单例 + 首次 watcher，返回该活实例。
    /// </summary>
    private static FileSystemWatcher InitializeFreshWatcher(string tempDir)
    {
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(tempDir, "stable.txt"), "s");
        ProjectModel.SetProjectRootFullPath(tempDir);
        _ = FileIO.GetFileTreeSnapshot(); // 触发首次 RefreshStableSnapshot → InitializeGlobalWatcher
        return GetGlobalWatcher() ?? throw new InvalidOperationException("首次 watcher 未创建");
    }

    // --------------------------------------------------------------------------
    // 根因 A：watcher 死亡残留无恢复路径
    // --------------------------------------------------------------------------

    /// <summary>
    /// 半死实例（EnableRaisingEvents=false，模拟重建部分失败残留）必须被 RefreshStableSnapshot
    /// 健康探针重建。修复前：== null 检查看到非 null 不重建 → FSW 永久死亡。
    /// </summary>
    [Fact]
    public void DeadWatcher_HalfEnabled_MustBeRebuiltByHealthProbe()
    {
        InitializeFreshWatcher(TempDir);

        FileSystemWatcher deadWatcher = new FileSystemWatcher(TempDir) { EnableRaisingEvents = false };
        SetGlobalWatcher(deadWatcher);

        InvokeRefreshStableSnapshot("test");

        Assert.True(GetGlobalWatcher() is { EnableRaisingEvents: true }, "半死 watcher 必须被 RefreshStableSnapshot 健康探针重建为活实例");
    }

    /// <summary>
    /// 已 Dispose 实例（访问 EnableRaisingEvents 抛 ObjectDisposedException）必须被健康探针重建。
    /// 修复前：== null 检查看到非 null 不重建。
    /// </summary>
    [Fact]
    public void DeadWatcher_Disposed_MustBeRebuiltByHealthProbe()
    {
        InitializeFreshWatcher(TempDir);

        FileSystemWatcher disposedWatcher = new FileSystemWatcher(TempDir);
        disposedWatcher.Dispose();
        SetGlobalWatcher(disposedWatcher);

        InvokeRefreshStableSnapshot("test");

        FileSystemWatcher? current = GetGlobalWatcher();
        Assert.NotNull(current);
        Assert.NotSame(disposedWatcher, current);
        Assert.True(current is { EnableRaisingEvents: true }, "已 Dispose 的 watcher 必须被健康探针重建");
    }

    /// <summary>
    /// 正常 OnWatcherError 路径必须产出活实例（回归保护）。
    /// </summary>
    [Fact]
    public void OnWatcherError_NormalRebuild_ProducesAliveWatcher()
    {
        FileSystemWatcher before = InitializeFreshWatcher(TempDir);

        InvokeOnWatcherError(before, new ErrorEventArgs(new InternalBufferOverflowException("模拟缓冲区溢出")));

        FileSystemWatcher? after = GetGlobalWatcher();
        Assert.NotNull(after);
        Assert.True(after is { EnableRaisingEvents: true }, "正常重建必须产出活实例");
    }

    /// <summary>
    /// 工程根目录已删除时，OnWatcherError 必须静默停止（置 null，不重建）——正确行为对照。
    /// </summary>
    [Fact]
    public void OnWatcherError_SilentStop_WhenProjectRootDeleted()
    {
        FileSystemWatcher watcher = InitializeFreshWatcher(TempDir);

        Directory.Delete(TempDir, recursive: true);
        InvokeOnWatcherError(watcher, new ErrorEventArgs(new InternalBufferOverflowException()));

        Assert.Null(GetGlobalWatcher());
    }

    // --------------------------------------------------------------------------
    // 根因 C：OnWatcherError 重建竞态
    // --------------------------------------------------------------------------

    /// <summary>
    /// 过时 sender（泄露/已 Dispose 的旧实例残留 Error 回调）必须被忽略，不误 Dispose 当前活实例。
    /// 修复前：不检查 sender，直接 Dispose _globalWatcher 并重建。修复后：ReferenceEquals 校验忽略。
    /// </summary>
    [Fact]
    public void OnWatcherError_IgnoresStaleSender()
    {
        InitializeFreshWatcher(TempDir);

        FileSystemWatcher aliveWatcher = new FileSystemWatcher(TempDir) { EnableRaisingEvents = true };
        SetGlobalWatcher(aliveWatcher);

        FileSystemWatcher staleWatcher = new FileSystemWatcher(TempDir);
        staleWatcher.Dispose();

        InvokeOnWatcherError(staleWatcher, new ErrorEventArgs(new InternalBufferOverflowException()));

        Assert.Same(aliveWatcher, GetGlobalWatcher());
    }

    /// <summary>
    /// 并发 OnWatcherError 调用必须保持不变量：最终 _globalWatcher 是单个活实例（无字段中间态丢失）。
    /// </summary>
    [Fact]
    public void OnWatcherError_ConcurrentCalls_KeepSingleAliveWatcher()
    {
        FileSystemWatcher watcher = InitializeFreshWatcher(TempDir);

        ErrorEventArgs args = new ErrorEventArgs(new InternalBufferOverflowException("模拟并发溢出"));
        Parallel.For(0, 8, _ => InvokeOnWatcherError(watcher, args));

        FileSystemWatcher? current = GetGlobalWatcher();
        Assert.NotNull(current);
        Assert.True(current is { EnableRaisingEvents: true }, "并发重建后必须仍是单个活实例");
    }
}
