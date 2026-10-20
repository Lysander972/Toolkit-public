using System.Diagnostics;
using System.Reflection;
using Toolkit.Core.Infra;
using Xunit;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Common;

// =============================================================================
// ProcessPendingInvalidations 防重入回归套件
//
// 背景（线上事故）：ScheduleRefresh→RescheduleUnifiedTimer 可在上一轮刷新仍在执行时
// 再次触发计时器，无串行化守卫时多个 tick 并发执行全量树扫描并排队模型写锁——
// 观测到 30+ 线程堆积在 ReaderWriterLockSlim 写锁、14 个 Parallel 读方被护送效应饿死，
// 表现为主机 CPU 打满、MCP 全量请求挂起（"永久死锁"）。
// =============================================================================

[Collection("FileIO")]
public sealed class FileSystemCacheRefreshSerializationTests : TempDirectoryTestFixture
{
    public FileSystemCacheRefreshSerializationTests() : base("Toolkit.Core.Tests.FileSystemCacheRefreshSerialization")
    {
    }

    private static void InvokeProcessPendingInvalidations(string source)
    {
        MethodInfo method = typeof(FileSystemCache).GetMethod("ProcessPendingInvalidations", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("未找到方法 ProcessPendingInvalidations");
        method.Invoke(FileSystemCache.Instance, new object?[] { source });
    }



    /// <summary>
    /// 慢速文件身份：每次 GetFileId 固定 Sleep，让全量扫描耗时确定（不受机器负载影响）。
    /// </summary>
    private sealed class SlowFileIdentity(int delayMs) : IFileIdentity
    {
        public FileId GetFileId(string fullPath)
        {
            Thread.Sleep(delayMs);
            return RealFileIdentity.Instance.GetFileId(fullPath);
        }

        public bool IsReparsePoint(string fullPath) => RealFileIdentity.Instance.IsReparsePoint(fullPath);
    }

    /// <summary>
    /// 并发第二轮刷新必须被防重入守护反弹（立即返回并保持 pending），
    /// 而不是再跑一轮秒级全量扫描。修复前：第二轮并发执行完整扫描，耗时与第一轮同量级。
    /// </summary>
    [Fact]
    public async Task ConcurrentInvalidations_MustBounceOffReentrancyGuard()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // 50 文件 × 20ms = 每轮扫描约 1 秒，远超守护反弹开销（毫秒级）。
        for (int i = 0; i < 50; i++)
        {
            WriteFile($"dir/file{i}.txt", "content");
        }
        FileSystemCache.InitializeForTesting(TempDir, new SlowFileIdentity(20));

        // 后台线程占住第一轮刷新（持有 _refreshProcessing）。
        FieldInfo refreshProcessingField = typeof(FileSystemCache).GetField("_refreshProcessing", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("未找到字段 _refreshProcessing");
        Task first = Task.Run(() => InvokeProcessPendingInvalidations("测试-第一轮"));
        bool entered = SpinWait.SpinUntil(() => refreshProcessingField.GetValue(FileSystemCache.Instance) is 1, TimeSpan.FromSeconds(10));
        Assert.True(entered, "第一轮刷新未在预期时间内进入处理段");

        // 并发第二轮：守护反弹必须立即返回，且保持 pending 让在跑的一轮覆盖本轮工作。
        Stopwatch sw = Stopwatch.StartNew();
        InvokeProcessPendingInvalidations("测试-并发第二轮");
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 500,
            $"并发刷新未被防重入守护反弹（实际耗时 {sw.ElapsedMilliseconds}ms，疑似并发执行了全量扫描）");
        FieldInfo refreshPendingField = typeof(FileSystemCache).GetField("_refreshPending", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("未找到字段 _refreshPending");
        Assert.True(refreshPendingField.GetValue(FileSystemCache.Instance) is true, "守护反弹后必须保持 pending 标记，由在跑的一轮覆盖本轮工作");

        await first.WaitAsync(TimeSpan.FromSeconds(60));
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
