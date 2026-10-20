using System.Diagnostics;
using System.Reflection;
using Toolkit.Core.Infra;
using Xunit;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Common;

[Collection("FileIO")]
public sealed class FileSystemCacheSnapshotConsistencyTests : TempDirectoryTestFixture
{
    public FileSystemCacheSnapshotConsistencyTests() : base("Toolkit.Core.Tests.FileSystemCacheSnapshotConsistency")
    {
    }

    [Fact]
    public void LocalMutationNotification_Should_Refresh_Through_Unified_Timer()
    {
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(TempDir, "test.txt"), "test");
        ProjectModel.SetProjectRootFullPath(TempDir);

        FileTreeSnapshot cached = FileIO.GetFileTreeSnapshot();
        Assert.Contains("test.txt", cached.Root.Files);

        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(TempDir, "new.txt"), "new");
        FileSystemCache.Instance.NotifyLocalMutationCompleted();

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(3))
        {
            FileTreeSnapshot snapshot = FileIO.GetFileTreeSnapshot();
            if (snapshot.Root.Files.Contains("new.txt"))
            {
                return;
            }

            System.Threading.Thread.Sleep(50);
        }

        Assert.Contains("new.txt", FileIO.GetFileTreeSnapshot().Root.Files);
    }

    /// <summary>
    /// FileSystemWatcher 漏掉外部变化（缓冲区溢出/静默停止）时，空闲心跳分支应主动全量重扫自愈。
    /// 屏蔽真实 FSW 后确定性地模拟"漏事件"：直接写新文件、不通知缓存层、强制空闲态，
    /// 驱动一次 OnUnifiedTimerTick，断言空闲心跳无条件重扫已捕获磁盘变化。
    /// 屏蔽 FSW 是必须的：全量套件下真实 watcher/Timer 活跃，写文件会异步投递事件，
    /// 与反射驱动的 tick 交错竞态导致 flaky（重扫被打断或 _refreshPending 被设回 true）。
    /// </summary>
    [Fact]
    public void Heartbeat_Should_Self_Heal_Missed_External_Change()
    {
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(TempDir, "existing.txt"), "old");
        ProjectModel.SetProjectRootFullPath(TempDir);

        FileTreeSnapshot initial = FileIO.GetFileTreeSnapshot();
        Assert.Contains("existing.txt", initial.Root.Files);

        // 屏蔽真实 FSW：本测试确定性地验证空闲心跳逻辑，必须排除 FSW 异步事件竞态
        // （全量套件下真实 watcher/Timer 活跃，写文件异步投递事件会与反射驱动 tick 交错导致 flaky）
        if (typeof(FileSystemCache).GetField("_globalWatcher", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(FileSystemCache.Instance) is FileSystemWatcher watcher)
        {
            watcher.EnableRaisingEvents = false;
        }

        // 模拟 FSW 漏事件：写新文件但不通知缓存层（watcher 已屏蔽，也不会自行投递）
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(TempDir, "missed.txt"), "new");
        Assert.False(FileSystemCache.Instance.IsWorkspaceBlocked);

        // 强制空闲态：_refreshPending=false → OnUnifiedTimerTick 走空闲心跳分支
        FieldInfo refreshPendingField = typeof(FileSystemCache).GetField("_refreshPending", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("未找到字段 _refreshPending");
        refreshPendingField.SetValue(FileSystemCache.Instance, false);

        MethodInfo tick = typeof(FileSystemCache).GetMethod("OnUnifiedTimerTick", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("未找到方法 OnUnifiedTimerTick");
        tick.Invoke(FileSystemCache.Instance, new object?[] { null });

        // 空闲心跳无条件全量重扫 → 漏掉的外部变化被自愈
        Assert.Contains("missed.txt", FileIO.GetFileTreeSnapshot().Root.Files);
    }

    // 「重试全失败不误广播」不变量不再单测：SnapshotRefreshInvalidatedException 现无可行确定性持续
    // 触发源——未来时间戳已豁免（见下方 Future_Timestamp_File_Does_Not_Permanently_Block_Snapshot），
    // 独占走 WorkspaceFileBlockedException 不入此重试循环。不变量由 ProcessPendingInvalidations
    // 循环结构承载：仅成功路径 return 前广播，全失败落到末尾 ScheduleRefresh 不广播。
    // 空闲心跳分支重试自愈已由上述未来时间戳用例确定性覆盖（无需后台线程时序）。

    /// <summary>
    /// 文件事件分支（_refreshPending=true）遇瞬时失效也必须当轮多重试自愈。
    /// 修复前(根因 B)：source="文件变化事件" maxAttempts=1，第 1 次失败即退出，等不到时间戳恢复 → 不广播；
    /// 失败循环中 _refreshPending 永久 true，空闲心跳的 4 次重试只在首次触发后永久退化为单次。
    /// 修复后：统一 maxAttempts，重试覆盖恢复窗口 → 广播。
    /// </summary>
    [Fact]
    public void FileEvent_Branch_Retries_On_Transient_Invalidation()
    {
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(TempDir, "stable.txt"), "s");
        ProjectModel.SetProjectRootFullPath(TempDir);
        FileIO.GetFileTreeSnapshot();

        if (typeof(FileSystemCache).GetField("_globalWatcher", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(FileSystemCache.Instance) is FileSystemWatcher watcher)
        {
            watcher.EnableRaisingEvents = false;
        }

        string flakyPath = SystemIOWrapper.Combine(TempDir, "flaky.txt");
        SystemIOWrapper.WriteAllText(flakyPath, "f");
        File.SetLastWriteTimeUtc(flakyPath, DateTime.UtcNow.AddHours(1));

        // 强制走"文件变化事件"分支：_refreshPending=true 且防抖已过期（lastEvent=0 → elapsed 巨大）
        typeof(FileSystemCache).GetField("_refreshPending", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.SetValue(FileSystemCache.Instance, true);
        typeof(FileSystemCache).GetField("_lastFileSystemEventTicks", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.SetValue(FileSystemCache.Instance, 0L);

        int broadcastCount = 0;
        EventHandler<FileTreeChangedEventArgs> handler = (_, _) => broadcastCount++;
        // 时序窗口设计：重试 attempt0≈0ms(fail)→15→30→45ms。后台恢复须晚于 attempt0 采样、早于
        // attempt2(≈30ms) 采样，取 t≈20ms 落在 attempt1/attempt2 之间，±10ms 调度抖动仍在窗口内。
        // 此测试本质依赖时序——SnapshotRefreshInvalidatedException 无确定性持续触发源（见类顶注释），
        // 已将恢复点置于窗口中段最大化鲁棒性。
        Thread recovery = new(() =>
        {
            Thread.Sleep(20);
            File.SetLastWriteTimeUtc(flakyPath, DateTime.UtcNow);
        });
        try
        {
            FileSystemCache.FileTreeChanged += handler;
            recovery.Start();

            MethodInfo tick = typeof(FileSystemCache)
                .GetMethod("OnUnifiedTimerTick", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("未找到方法 OnUnifiedTimerTick");
            tick.Invoke(FileSystemCache.Instance, new object?[] { null });
            recovery.Join(2000);

            Assert.True(broadcastCount >= 1, "文件事件分支也必须当轮多重试自愈并广播 FileTreeChanged");
            Assert.Contains("flaky.txt", FileIO.GetFileTreeSnapshot().Root.Files);
        }
        finally
        {
            FileSystemCache.FileTreeChanged -= handler;
        }
    }

    /// <summary>
    /// 未来时间戳文件（时钟跳变/元数据异常）不得永久卡死整棵快照树。
    /// 现状(根因 D，红)：BuildTreeRecursive 每轮因 fileBefore.LastWriteTimeUtc > snapshotStartTicks 抛
    /// SnapshotRefreshInvalidatedException 整轮作废，连驱多轮也不广播，连正常文件也不纳入 → 永久卡死
    /// （watcher 健康也救不回，是唯一能让"watcher 健康+工程静止"下仍永久卡死的因素）。
    /// 修复后(绿)：未来时间戳豁免整轮作废（仍走采样窗口校验），快照成功并广播，正常与异常文件均纳入。
    /// </summary>
    [Fact]
    public void Future_Timestamp_File_Does_Not_Permanently_Block_Snapshot()
    {
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(TempDir, "stable.txt"), "s");
        ProjectModel.SetProjectRootFullPath(TempDir);
        FileIO.GetFileTreeSnapshot();

        if (typeof(FileSystemCache).GetField("_globalWatcher", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(FileSystemCache.Instance) is FileSystemWatcher watcher)
        {
            watcher.EnableRaisingEvents = false;
        }

        string futurePath = SystemIOWrapper.Combine(TempDir, "future.txt");
        SystemIOWrapper.WriteAllText(futurePath, "f");
        File.SetLastWriteTimeUtc(futurePath, DateTime.UtcNow.AddHours(1));

        MethodInfo tick = typeof(FileSystemCache)
            .GetMethod("OnUnifiedTimerTick", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("未找到方法 OnUnifiedTimerTick");
        FieldInfo refreshPendingField = typeof(FileSystemCache)
            .GetField("_refreshPending", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("未找到字段 _refreshPending");

        int broadcastCount = 0;
        EventHandler<FileTreeChangedEventArgs> handler = (_, _) => broadcastCount++;
        try
        {
            FileSystemCache.FileTreeChanged += handler;

            // 连驱 3 轮空闲心跳：现状下每轮都整轮作废，永不广播
            for (int i = 0; i < 3; i++)
            {
                refreshPendingField.SetValue(FileSystemCache.Instance, false);
                tick.Invoke(FileSystemCache.Instance, new object?[] { null });
            }

            Assert.True(broadcastCount >= 1, "未来时间戳文件不得永久卡死整棵快照树");
            List<string> files = FileIO.GetFileTreeSnapshot().Root.Files;
            Assert.Contains("stable.txt", files);
            Assert.Contains("future.txt", files);
        }
        finally
        {
            FileSystemCache.FileTreeChanged -= handler;
            File.SetLastWriteTimeUtc(futurePath, DateTime.UtcNow); // 恢复时间戳，避免单例状态残留污染后续测试
        }
    }
}
