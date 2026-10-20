// Core_Persistence_Diff_Spec.md - 第 7 节实现
// 局部 Diff 计算 - 对比期望树与当前状态快照

using System.Security.Cryptography;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

// =============================================================================
// Diff 计算结果
// =============================================================================

/// <summary>
/// 局部 Diff 计算结果。
/// 包含将当前状态转换为期望状态所需的所有原子操作。
/// </summary>
internal sealed class LocalDiffResult
{
    /// <summary>需要执行的原子操作（已按规范顺序排序）</summary>
    public IReadOnlyList<AtomicOperation> Operations { get; }

    /// <summary>统计信息</summary>
    public DiffStatistics Statistics { get; }

    /// <summary>起点实体路径</summary>
    public string StartingEntityPath { get; }

    internal LocalDiffResult(IReadOnlyList<AtomicOperation> operations, DiffStatistics stats, string startingPath)
    {
        Operations = operations;
        Statistics = stats;
        StartingEntityPath = startingPath;
    }
}

/// <summary>
/// Diff 统计信息。
/// </summary>
internal readonly record struct DiffStatistics(
    int DirectoriesToCreate,
    int DirectoriesToDelete,
    int FilesToWrite,
    int FilesToDelete,
    int FilesUnchanged
);

// =============================================================================
// 局部 Diff 计算器
// =============================================================================
//
// 【核心概念】
//
// 集合 A = 从实体路径递归收集的文件系统内容（起点范围的粗略估算）
// 集合 B = 实体声明的期望树覆盖范围（精确的期望状态）
//
// 【删除操作】A - B
// - 含义：起点范围内，当前存在但期望没声明的
// - 用途：清理子实体范围内不再需要的文件
//
// 【写入操作】B 与文件系统比较（与 A 无关）
// - 文件存在：比较内容，变化则写入，相同则跳过
// - 文件不存在：新建
//
// 【为什么删除依赖 A 而写入不依赖 A】
// - 删除需要明确"管辖范围"，A 定义了这个范围
// - 写入是声明式的，期望树 B 声明了什么就写入什么，不管是否在 A 范围内
// - 这样处理像 Map/Mech 等实体在起点路径外有额外文件的情况
//
// =============================================================================

internal static class LocalDiffCalculator
{
    /// <summary>
    /// 计算局部 Diff。
    ///
    /// 参数说明：
    /// - expectedBuffer: 期望树 B（实体声明的期望状态）
    /// - currentSnapshot: 当前快照 A（从实体路径递归收集的文件系统内容）
    /// </summary>
    internal static LocalDiffResult Calculate(
        ExpectedTreeBuffer expectedBuffer,
        LocalFileSystemSnapshot currentSnapshot)
    {
        List<AtomicOperation> operations = [];
        DiffStatsCollector stats = new();

        // 集合 A：起点范围内的当前文件系统状态（用于计算删除）
        HashSet<string> filesInScopeA = new(currentSnapshot.Files.Keys, StringComparer.Ordinal);
        HashSet<string> dirsInScopeA = new(currentSnapshot.Directories, StringComparer.Ordinal);
        // 工程根目录不允许被删除
        dirsInScopeA.Remove(WorkspacePath.RootDirectory);

        // 集合 B：期望状态
        Dictionary<string, byte[]?> expectedFiles = expectedBuffer.GetAllFiles()
            .ToDictionary(static f => f.RelativePath, static f => f.Content, StringComparer.Ordinal);
        HashSet<string> expectedDirectories = new(
            expectedBuffer.GetAllDirectories(),
            StringComparer.Ordinal);

        // =========================================================================
        // 写入操作：B 与文件系统比较（与 A 无关）
        // =========================================================================
        foreach ((string? path, byte[]? content) in expectedFiles)
        {
            if (content == null)
            {
                // 剪枝声明（脏标记剪枝跳过的实体）：期望内容与磁盘一致，跳过比较。
                // 仍从 A 移除，防止被误删。
                filesInScopeA.Remove(path);
                stats.FilesUnchanged++;
                continue;
            }

            if (currentSnapshot.Files.ContainsKey(path))
            {
                // 文件存在：比较内容
                if (HasContentChanged(path, content))
                {
                    operations.Add(new AtomicOperation(AtomicOperationType.WriteFile, path, content));
                    stats.FilesToWrite++;
                }
                else
                {
                    stats.FilesUnchanged++;
                }
            }
            else
            {
                // 文件不存在：新建
                operations.Add(new AtomicOperation(AtomicOperationType.WriteFile, path, content));
                stats.FilesToWrite++;
            }

            // 从 A 中移除已处理的文件（用于后续计算删除）
            filesInScopeA.Remove(path);
        }

        // =========================================================================
        // 删除操作：A - B（起点范围内，当前存在但期望没声明的）
        // =========================================================================
        foreach (string path in filesInScopeA)
        {
            operations.Add(new AtomicOperation(AtomicOperationType.DeleteFile, path));
            stats.FilesToDelete++;
        }

        // =========================================================================
        // 目录操作
        // =========================================================================
        // 创建：期望中存在但当前文件系统不存在的目录（与文件规则一致：B vs 当前全集）
        foreach (string dir in expectedDirectories)
        {
            if (!currentSnapshot.Directories.Contains(dir))
            {
                operations.Add(new AtomicOperation(AtomicOperationType.CreateDirectory, dir));
                stats.DirectoriesToCreate++;
            }

            // A-B 删除计算时，期望目录一律视为"保留"
            dirsInScopeA.Remove(dir);
        }

        // 删除：当前存在但期望中不存在的目录
        foreach (string dir in dirsInScopeA)
        {
            operations.Add(new AtomicOperation(AtomicOperationType.DeleteDirectory, dir));
            stats.DirectoriesToDelete++;
        }

        // =========================================================================
        // 排序并返回
        // =========================================================================
        AtomicOperation[] orderedOperations = operations
            .OrderBy(static op => AtomicOperationOrder.GetExecutionOrder(op.Type))
            .ThenBy(static op => AtomicOperationOrder.GetSecondaryOrder(op))
            .ThenBy(static op => op.RelativePath, StringComparer.Ordinal)
            .ToArray();

        DiffStatistics statistics = new(
            stats.DirectoriesToCreate,
            stats.DirectoriesToDelete,
            stats.FilesToWrite,
            stats.FilesToDelete,
            stats.FilesUnchanged
        );

        string? startingPath = expectedBuffer.StartingEntityPath;
        if (string.IsNullOrWhiteSpace(startingPath))
        {
            throw new InvalidOperationException("ExpectedTreeBuffer.StartingEntityPath 未设置");
        }

        return new LocalDiffResult(orderedOperations, statistics, startingPath);
    }

    /// <summary>
    /// 检查文件内容是否发生变化。
    /// 通过缓存哈希比较（FileSystemCache 按 LastWriteTime 自愈 + 锁探测），
    /// 未变文件零读盘。文件不存在视为有变化（需要创建）。
    /// </summary>
    private static bool HasContentChanged(string relativePath, byte[] expectedContent)
    {
        byte[] expectedHash = SHA256.HashData(expectedContent);

        try
        {
            byte[] currentHash = FileIO.GetFileHash(relativePath);
            return !currentHash.AsSpan().SequenceEqual(expectedHash);
        }
        catch (FileNotFoundException)
        {
            return true; // 文件不存在，视为有变化（需要创建）
        }
    }

    /// <summary>
    /// 内部统计收集器。
    /// </summary>
    private sealed class DiffStatsCollector
    {
        public int DirectoriesToCreate;
        public int DirectoriesToDelete;
        public int FilesToWrite;
        public int FilesToDelete;
        public int FilesUnchanged;
    }
}

// =============================================================================
// Diff 计算结果扩展方法
// =============================================================================

/// <summary>
/// Diff 计算结果扩展方法。
/// </summary>
public static class DiffResultExtensions
{
    /// <summary>
    /// 检查 Diff 结果是否包含任何变更。
    /// </summary>
    internal static bool HasChanges(this LocalDiffResult result)
    {
        return result.Operations.Count > 0;
    }
}
