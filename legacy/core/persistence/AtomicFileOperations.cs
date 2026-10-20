// Core_Persistence_Diff_Spec.md - 第 7.3 节 + 第 8 节实现
// 四类原子操作封装 + 执行顺序保证 + 全局文件操作锁

using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

// =============================================================================
// 原子操作类型定义 - Core_Persistence_Diff_Spec.md 第 7.3 节
// =============================================================================

/// <summary>
/// 原子操作类型。
/// Diff 的落地结果只允许这四类原子操作，任何更高层语义必须编译成这四类。
/// </summary>
public enum AtomicOperationType
{
    /// <summary>新建目录</summary>
    CreateDirectory,

    /// <summary>删除目录</summary>
    DeleteDirectory,

    /// <summary>新建或覆盖文件</summary>
    WriteFile,

    /// <summary>删除文件</summary>
    DeleteFile
}

// =============================================================================
// 原子操作记录 - 单个文件系统操作的原子描述
// =============================================================================

/// <summary>
/// 原子操作记录。
/// 描述一个对文件系统的原子变更意图。
/// </summary>
/// <param name="Type">操作类型</param>
/// <param name="RelativePath">工程根相对路径（必须经过 WorkspacePath.RequireRelativePath 校验）</param>
/// <param name="Content">文件内容，仅 WriteFile 操作使用</param>
public readonly record struct AtomicOperation(
    AtomicOperationType Type,
    string RelativePath,
    byte[]? Content = null
);

// =============================================================================
// 原子操作排序辅助 - 规范第 8.1 节执行顺序
// =============================================================================

/// <summary>
/// 原子操作排序辅助方法。
/// 统一的排序逻辑，确保所有原子操作按规范第 8.1 节要求的顺序执行。
/// </summary>
public static class AtomicOperationOrder
{
    /// <summary>
    /// 获取操作类型的执行顺序优先级（值越小越先执行）。
    /// 顺序：删文件 → 删目录 → 建目录 → 写文件
    /// </summary>
    public static int GetExecutionOrder(AtomicOperationType type) => type switch
    {
        AtomicOperationType.DeleteFile => 0,
        AtomicOperationType.DeleteDirectory => 1,
        AtomicOperationType.CreateDirectory => 2,
        AtomicOperationType.WriteFile => 3,
        _ => throw new InvalidOperationException($"未知的原子操作类型: {type}")
    };

    /// <summary>
    /// 获取次级排序键（目录深度顺序）。
    /// 删除目录：深到浅
    /// 创建目录：浅到深
    /// 其他操作：无次级排序
    /// </summary>
    public static int GetSecondaryOrder(AtomicOperation op)
    {
        int depth = op.RelativePath.Count(static c => c == '/');
        return op.Type switch
        {
            AtomicOperationType.DeleteDirectory => -depth, // 深到浅
            AtomicOperationType.CreateDirectory => depth,  // 浅到深
            _ => 0
        };
    }
}

// =============================================================================
// 原子操作执行器 - Core_Persistence_Diff_Spec.md 第 8 节
// =============================================================================

/// <summary>
/// 原子操作执行器。
///
/// 核心保证：
/// 1. 执行顺序固定（第 8.1 节）：删文件 → 删目录 → 建目录 → 写文件
/// 2. 全局文件操作锁（第 8.2 节）：串行执行，禁止并发
/// 3. 无重试：任意失败直接抛错中止
/// 4. 无回滚：禁止任何形式的回退方案
///
/// 警告：这是内部工具，业务代码应通过 ProjectModel.SaveChanges() 间接使用。
/// </summary>
public static class AtomicFileOperationExecutor
{
    // 全局文件操作锁 - 规范第 8.2 节
    // 单一全局锁，串行执行所有文件操作
    private static readonly Lock FileOperationLock = new();

    /// <summary>
    /// 批量执行原子操作。
    ///
    /// 执行保证：
    /// - 操作会按规范要求的固定顺序重新排序
    /// - 整个批次在全局锁内串行执行
    /// - 写入/删除目标独占（FileShare.None），coverage-only 文件共享读锁（FileShare.Read），持有至写入完成后统一释放
    /// - 独占失败抛 WorkspaceFileBlockedException，终止整个批次
    /// - 任意操作失败立即抛错中止，无重试无回滚
    /// </summary>
    /// <param name="operations">操作列表（允许无序，会被重新排序）</param>
    /// <param name="exclusiveFilePaths">覆盖范围文件路径（写/删目标独占，其余共享读锁）</param>
    /// <param name="beforeExecute">执行前回调（持句柄状态校验，如状态哈希复算）</param>
    /// <exception cref="ArgumentNullException">operations 为 null</exception>
    /// <exception cref="WorkspaceFileBlockedException">写入/删除目标被外部进程锁定</exception>
    /// <exception cref="InvalidOperationException">文件操作失败</exception>
    public static void ExecuteBatch(
        IReadOnlyList<AtomicOperation> operations,
        IReadOnlyCollection<string> exclusiveFilePaths,
        Action<IReadOnlyDictionary<string, FileStream>> beforeExecute)
    {
        if (operations.Count == 0)
        {
            return;
        }

        // 按规范第 8.1 节排序：
        // 1. 删除文件
        // 2. 删除目录
        // 3. 创建目录
        // 4. 写入文件
        AtomicOperation[] ordered = operations
            .OrderBy(static op => AtomicOperationOrder.GetExecutionOrder(op.Type))
            .ThenBy(static op => AtomicOperationOrder.GetSecondaryOrder(op))
            .ThenBy(static op => op.RelativePath, StringComparer.Ordinal)
            .ToArray();

        // 规范第 8.2 节：全局文件操作锁，串行执行
        lock (FileOperationLock)
        {
            // 阶段一：独占覆盖面文件与操作目标，持有句柄不释放
            Dictionary<string, FileStream> heldHandles = AcquireExclusiveHandles(ordered, exclusiveFilePaths);

            try
            {
                beforeExecute(heldHandles);

                // 阶段二：在独占状态下执行全部操作（WriteFile 通过已持有句柄写入）
                foreach (AtomicOperation op in ordered)
                {
                    ExecuteSingle(op, heldHandles);
                }
            }
            finally
            {
                // 阶段三：统一释放所有句柄
                foreach (FileStream handle in heldHandles.Values)
                {
                    handle.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// 锁定 coverage 文件与操作目标：
    ///   写入/删除目标 → FileShare.None（通过持有句柄写入，必须独占）
    ///   仅参与哈希计算的 coverage-only 文件 → FileShare.Read（阻止外部写入，但允许并发读，避免与 ProbeFileReadable 冲突）
    /// 目标不存在则跳过（新建文件无需独占，删除不存在的文件本身幂等）。
    /// 任一失败 → 抛 WorkspaceFileBlockedException，终止整个批次（句柄由调用方 finally 统一释放）。
    /// </summary>
    private static Dictionary<string, FileStream> AcquireExclusiveHandles(
        AtomicOperation[] ordered,
        IReadOnlyCollection<string> exclusiveFilePaths)
    {
        Dictionary<string, FileStream> handles = new(ordered.Length + exclusiveFilePaths.Count);

        // 需要 FileShare.None 独占的文件：被写入或删除的操作目标
        HashSet<string> writeTargets = new(StringComparer.Ordinal);
        foreach (AtomicOperation op in ordered)
        {
            if (op.Type is AtomicOperationType.WriteFile or AtomicOperationType.DeleteFile)
                writeTargets.Add(op.RelativePath);
        }

        foreach (string relativePath in exclusiveFilePaths.OrderBy(static path => path, StringComparer.Ordinal))
        {
            if (writeTargets.Contains(relativePath))
                AcquireExclusiveHandle(relativePath, handles);
            else
                AcquireReadHandle(relativePath, handles);
        }

        // 写入/删除目标不在 coverage 内时（如删除不在快照中的文件），单独独占
        HashSet<string> existingExactFiles = new(exclusiveFilePaths, StringComparer.Ordinal);
        foreach (AtomicOperation op in ordered)
        {
            if (op.Type is not (AtomicOperationType.WriteFile or AtomicOperationType.DeleteFile)) continue;
            if (op.Type == AtomicOperationType.WriteFile && !existingExactFiles.Contains(op.RelativePath)) continue;
            AcquireExclusiveHandle(op.RelativePath, handles);
        }

        return handles;
    }

    private static void AcquireReadHandle(string relativePath, Dictionary<string, FileStream> handles)
    {
        string path = WorkspacePath.RequireRelativePath(relativePath);
        if (handles.ContainsKey(path)) return;

        try
        {
            FileStream handle = FileIO.OpenFileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            handles[path] = handle;
        }
        catch (FileNotFoundException) { }   // 文件不存在，无需读锁
        catch (DirectoryNotFoundException) { } // 父目录不存在，同上
        catch (IOException ex)
        {
            // 非关键文件被外部占据：容错跳过（该文件不参与锁保护与状态哈希，见 ComputeStateHash skippedPaths）。
            // 写/删目标不走本方法（AcquireExclusiveHandle 严格避让）。
            if (!CriticalFilePolicy.IsCriticalPath(path))
            {
                return;
            }

            foreach (FileStream h in handles.Values) h.Dispose();
            WorkspaceFileBlockedException blockedEx = new(
                path,
                $"文件被外部进程锁定，无法读取: {path}",
                ex);
            FileSystemCache.Instance.ReportBlockedException(blockedEx);
            throw blockedEx;
        }
    }

    private static void AcquireExclusiveHandle(string relativePath, Dictionary<string, FileStream> handles)
    {
        string path = WorkspacePath.RequireRelativePath(relativePath);
        if (handles.ContainsKey(path)) return; // 同一文件只独占一次

        try
        {
            FileStream handle = FileIO.OpenFileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            handles[path] = handle;
        }
        catch (FileNotFoundException) { }   // 新建文件，无需独占
        catch (DirectoryNotFoundException) { } // 父目录尚不存在（CreateDirectory 会在本批次内创建）
        catch (IOException ex)
        {
            // 独占失败：释放已获取的所有句柄后抛出
            foreach (FileStream h in handles.Values) h.Dispose();
            WorkspaceFileBlockedException blockedEx = new(
                path,
                $"文件被外部进程锁定，无法独占: {path}",
                ex);
            FileSystemCache.Instance.ReportBlockedException(blockedEx);
            throw blockedEx;
        }
    }

    /// <summary>
    /// 执行单个原子操作。
    /// 无重试，失败直接抛错。
    /// WriteFile / DeleteFile 通过预持有的独占句柄操作。
    /// </summary>
    private static void ExecuteSingle(AtomicOperation op, Dictionary<string, FileStream> heldHandles)
    {
        // 统一校验路径（边界检查）
        string path = WorkspacePath.RequireRelativePath(op.RelativePath);

        switch (op.Type)
        {
            case AtomicOperationType.CreateDirectory:
                ExecuteCreateDirectory(path);
                break;

            case AtomicOperationType.DeleteDirectory:
                ExecuteDeleteDirectory(path);
                break;

            case AtomicOperationType.WriteFile:
                ExecuteWriteFile(path, op.Content, heldHandles);
                break;

            case AtomicOperationType.DeleteFile:
                ExecuteDeleteFile(path, heldHandles);
                break;

            default:
                throw new InvalidOperationException($"未知的原子操作类型: {op.Type}");
        }
    }

    // =========================================================================
    // 四类原子操作的具体实现 - Core_Persistence_Diff_Spec.md 第 7.3 节
    // =========================================================================

    /// <summary>
    /// 原子操作：新建目录。
    /// </summary>
#pragma warning disable TK006 // 原子操作系列方法之一，与 ExecuteDeleteDirectory 等对称
    private static void ExecuteCreateDirectory(string relativePath)
#pragma warning restore TK006
    {
        string fullPath = FileSystemCache.Instance.ResolveFullPath(relativePath);
        if (SystemIOWrapper.DirectoryExists(fullPath))
        {
            return;
        }

        SystemIOWrapper.CreateDirectory(fullPath);
    }

    /// <summary>
    /// 原子操作：删除目录。
    /// 递归删除目录及其所有内容。
    /// </summary>
    private static void ExecuteDeleteDirectory(string relativePath)
    {
        if (relativePath == WorkspacePath.RootDirectory)
        {
            throw new InvalidOperationException("禁止删除工程根目录");
        }

        string fullPath = FileSystemCache.Instance.ResolveFullPath(relativePath);
        if (!SystemIOWrapper.DirectoryExists(fullPath))
        {
            return;
        }

        // 不递归删除：隐藏目录/文件永远不参与 diff，也永远不被删除。
        // 如果目录因"仅隐藏内容"而无法删除，则跳过；若仍存在非隐藏内容，则视为并发/不一致直接抛错。
        try
        {
            SystemIOWrapper.DeleteDirectory(fullPath);
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
        {
            // 目录本身被外部进程占据（删除目标避让）：按受阻语义包装
            ThrowBlocked(relativePath, "目录被外部进程占用，无法删除", ex);
        }
        catch (IOException ex)
        {
            (bool blockedByHiddenOnly, string[] offenders) = AnalyzeDeleteDirectoryFailure(relativePath);
            if (blockedByHiddenOnly)
            {
                return;
            }

            string offenderText = string.Empty;
            if (offenders.Length > 0)
                offenderText = $"，非隐藏残留: {string.Join(", ", offenders)}";
            throw new InvalidOperationException($"删除目录失败（目录仍含非隐藏内容）: {relativePath}{offenderText}", ex);
        }
    }

#pragma warning disable TK006 // 删除失败分析，封装路径解析+核心判定+结果截断
    private static (bool BlockedByHiddenOnly, string[] Offenders) AnalyzeDeleteDirectoryFailure(string dirRelativePath)
    {
        string dirFullPath = FileSystemCache.Instance.ResolveFullPath(dirRelativePath);
        List<string> offenders = new(capacity: 8);

        bool blocked = IsBlockedByHiddenOnlyCore(dirRelativePath, dirFullPath, offenders);
        return (blocked, offenders.Take(10).ToArray());
    }
#pragma warning restore TK006

    private static bool IsBlockedByHiddenOnlyCore(string dirRelativePath, string dirFullPath, List<string> offenders)
    {
        bool hasHidden = false;
        bool anyEntries = false;

        IEnumerable<string> entries;
        try
        {
            entries = SystemIOWrapper.EnumerateFileSystemEntries(dirFullPath);
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
        {
            // 删除失败原因分析期间目录被外部占据：按受阻语义包装
            ThrowBlocked(dirRelativePath, "目录被外部进程占用，无法分析删除失败原因", ex);
            throw;
        }

        foreach (string entryFullPath in entries)
        {
            anyEntries = true;

            string name = SystemIOWrapper.GetLastSegment(entryFullPath);
            if (WorkspacePath.IsHiddenSegment(name) || WorkspacePath.IsIgnoredSegment(name))
            {
                hasHidden = true;
                continue;
            }

            // 非隐藏目录：仅当其内部最终也被"隐藏内容"阻塞时，才允许作为残留存在。
            if (SystemIOWrapper.DirectoryExists(entryFullPath))
            {
                string childRelativePath = RelativePathUtils.Combine(dirRelativePath, name);
                if (IsBlockedByHiddenOnlyCore(childRelativePath, entryFullPath, offenders))
                {
                    hasHidden = true;
                    continue;
                }
            }

            // 非隐藏文件，或"包含非隐藏内容"的非隐藏目录：不允许
            if (offenders.Count < 10)
            {
                offenders.Add(RelativePathUtils.Combine(dirRelativePath, name));
            }
            return false;
        }

        // 空目录不是"隐藏内容阻塞"的情况：如果删除失败却为空目录，视为错误而非跳过。
        if (!anyEntries)
        {
            return false;
        }

        return hasHidden;
    }

    /// <summary>
    /// 原子操作：新建或覆盖文件。
    /// 通过预持有的独占句柄写入，避免重新打开文件。
    /// </summary>
    private static void ExecuteWriteFile(string relativePath, byte[]? content, Dictionary<string, FileStream> heldHandles)
    {
        if (content == null)
        {
            throw new InvalidOperationException($"WriteFile 操作必须提供内容: {relativePath}");
        }

        if (heldHandles.ContainsKey(relativePath))
        {
            FileStream handle = heldHandles[relativePath];
            handle.SetLength(0);
            handle.Write(content, 0, content.Length);
            handle.Flush();
        }
        else
        {
            // 文件尚不存在（新建），正常创建,此处绝对明确地禁止自动"容错"乱创建目录!!!!! 应该真正定位异常而不是乱制造防御味垃圾掩盖逻辑的无能!
            string fullPath = FileSystemCache.Instance.ResolveFullPath(relativePath);
            try
            {
                SystemIOWrapper.WriteAllBytes(fullPath, content);
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
            {
                // diff 计算后窗口内外部创建并占据同名文件：写目标避让，按受阻语义包装
                ThrowBlocked(relativePath, "文件被外部进程占用，无法写入", ex);
            }
        }
    }

    /// <summary>
    /// 原子操作：删除文件。
    /// 先释放预持有的独占句柄，再执行删除。
    /// </summary>
    private static void ExecuteDeleteFile(string relativePath, Dictionary<string, FileStream> heldHandles)
    {
        string fullPath = FileSystemCache.Instance.ResolveFullPath(relativePath);
        if (!SystemIOWrapper.FileExists(fullPath))
        {
            return;
        }

        // 释放独占句柄（删除前必须关闭所有句柄）
        if (heldHandles.ContainsKey(relativePath))
        {
            FileStream handle = heldHandles[relativePath];
            handle.Dispose();
            heldHandles.Remove(relativePath);
        }

        try
        {
            SystemIOWrapper.DeleteFile(fullPath);
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
        {
            // 独占句柄释放后窗口内外部重新占据：删除目标避让，按受阻语义包装
            ThrowBlocked(relativePath, "文件被外部进程占用，无法删除", ex);
        }
    }

    /// <summary>
    /// 写/删目标被外部占据的统一受阻包装（系统边界防御：写入/删除失败必须按受阻语义呈现，
    /// 而非泄露原始 IOException，保证 MCP 门禁与提示信息正确）。
    /// </summary>
    private static void ThrowBlocked(string relativePath, string reason, IOException ex)
    {
        WorkspaceFileBlockedException blockedEx = new(relativePath, $"{reason}: {relativePath}", ex);
        FileSystemCache.Instance.ReportBlockedException(blockedEx);
        throw blockedEx;
    }
}
