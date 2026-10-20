
using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

// =============================================================================
// 核心设计警告 / CORE DESIGN WARNING
// =============================================================================
//
// 本文件包含内部文件操作工具。这些类型是 internal 的，因为工作区
// 应通过实体模型（ProjectEntity.Load/Save/Delete）操作文件，
// 而不是直接使用这些底层工具。
//
// 禁止：
// 1. 将这些类型改为 public
// 2. 添加绕过实体模型的文件操作方法
// 3. 在其他模块直接使用 FileIO
//
// 正确做法：
//   var model = ProjectModel.Load(...);
//   model.SaveChanges("某个起点实体的相对路径");
//
// 如果其他模块需要文件操作，应通过实体模型，而不是直接使用 FileIO。
//
// 缓存分层架构：
// - FileSystemCache（Internal/FileSystemCache.cs）：唯一文件系统缓存层
// - FileIO：委托给 FileSystemCache，自身不再维护缓存
// =============================================================================

/// <summary>
/// 文件读写操作封装。
/// 委托给 FileSystemCache，统一缓存管理。
/// 仅接受工程根目录的相对路径，不提供任何路径转换服务。
///
/// 警告：这是内部工具，工作区应通过实体模型操作文件。
/// </summary>
internal static class FileIO
{
    internal static void SetProjectRoot(string projectRootFullPath)
    {
        if (!WorkspacePath.IsAbsolutePath(projectRootFullPath))
        {
            throw new ArgumentException("projectRootFullPath 必须为绝对路径", nameof(projectRootFullPath));
        }

#pragma warning disable TK010 // 路径规范化：去除末尾斜杠，防止拼接时产生双斜杠
        FileSystemCache.Initialize(WorkspacePath.ToUnixSeparator(projectRootFullPath).TrimEnd('/'));
#pragma warning restore TK010
    }

    internal static string GetProjectRoot() => Cache.ProjectRootFullPath;

    private static FileSystemCache Cache => FileSystemCache.Instance;

    // =========================================================================
    // 文件存在性检查
    // =========================================================================

    internal static bool FileExists(string relativePath)
    {
        return Cache.FileExists(relativePath);
    }

    internal static bool DirectoryExists(string relativePath)
    {
        return Cache.DirectoryExists(relativePath);
    }

    // =========================================================================
    // 文件读取
    // =========================================================================

    /// <summary>
    /// 读取文件全部文本。
    /// 内部固定使用 UTF-8 编码（自动检测 BOM）。
    /// </summary>
    internal static string ReadAllText(string relativePath)
    {
        return Cache.ReadAllText(relativePath);
    }

    internal static byte[] ReadAllBytes(string relativePath)
    {
        return Cache.ReadAllBytes(relativePath);
    }

    /// <summary>
    /// 获取文件内容的 SHA256 哈希。
    /// 与 ReadAllBytes 共享缓存条目，文件未变化时零开销。
    /// 返回 32 字节原始哈希（不做 HexString 转换）。
    /// </summary>
    internal static byte[] GetFileHash(string relativePath)
    {
        return Cache.GetFileHash(relativePath);
    }

    /// <summary>
    /// 读取文件全部行（按 LF 分割）。
    /// 内部固定使用 UTF-8 编码（自动检测 BOM）。
    /// </summary>
    internal static string[] ReadAllLines(string relativePath)
    {
        string text = Cache.ReadAllText(relativePath);
        return text.Split(['\n'], StringSplitOptions.None);
    }

    // =========================================================================
    // 文件写入
    // =========================================================================

    internal static void WriteAllText(string relativePath, string text)
    {
        Cache.WriteAllText(relativePath, text);
    }

    internal static void WriteAllBytes(string relativePath, byte[] bytes)
    {
        Cache.WriteAllBytes(relativePath, bytes);
    }

    // =========================================================================
    // 文件操作
    // =========================================================================

    // 所有真实写删移结束时只排期统一 timer，禁止夹带缓存失效。
    // 越界篡改缓存就是在侵蚀歪曲架构! 制造污染和分裂!!!!
    internal static void DeleteFile(string relativePath)
    {
        if (Cache.TryApplyOverlayOperation(new AtomicOperation(AtomicOperationType.DeleteFile, relativePath)))
        {
            return;
        }

        try
        {
            string fullPath = Cache.ResolveFullPath(relativePath);
            SystemIOWrapper.DeleteFile(fullPath);
        }
        finally
        {
            Cache.NotifyLocalMutationCompleted();
        }
    }

    internal static void MoveFile(string sourcePath, string destinationPath)
    {
        try
        {
            string sourceFullPath = Cache.ResolveFullPath(sourcePath);
            string destinationFullPath = Cache.ResolveFullPath(destinationPath);
            SystemIOWrapper.MoveFile(sourceFullPath, destinationFullPath);
        }
        finally
        {
            Cache.NotifyLocalMutationCompleted();
        }
    }

    internal static FileStream OpenRead(string relativePath)
    {
        string fullPath = Cache.ResolveFullPath(relativePath);
        return SystemIOWrapper.OpenRead(fullPath);
    }

    internal static FileStream OpenFileStream(string relativePath, FileMode mode, FileAccess access, FileShare share)
    {
        string fullPath = Cache.ResolveFullPath(relativePath);
        return SystemIOWrapper.OpenFileStream(fullPath, mode, access, share);
    }

    // =========================================================================
    // 目录操作
    // =========================================================================

    internal static void CreateDirectory(string relativePath)
    {
        if (Cache.TryApplyOverlayOperation(new AtomicOperation(AtomicOperationType.CreateDirectory, relativePath)))
        {
            return;
        }

        try
        {
            string fullPath = Cache.ResolveFullPath(relativePath);
            SystemIOWrapper.CreateDirectory(fullPath);
        }
        finally
        {
            Cache.NotifyLocalMutationCompleted();
        }
    }

    internal static void DeleteDirectory(string relativePath)
    {
        if (Cache.TryApplyOverlayOperation(new AtomicOperation(AtomicOperationType.DeleteDirectory, relativePath)))
        {
            return;
        }

        try
        {
            string fullPath = Cache.ResolveFullPath(relativePath);
            SystemIOWrapper.DeleteDirectory(fullPath);
        }
        finally
        {
            Cache.NotifyLocalMutationCompleted();
        }
    }

    internal static void DeleteDirectory(string relativePath, bool recursive)
    {
        if (Cache.TryApplyOverlayOperation(new AtomicOperation(AtomicOperationType.DeleteDirectory, relativePath)))
        {
            return;
        }

        try
        {
            string fullPath = Cache.ResolveFullPath(relativePath);
            Directory.Delete(fullPath, recursive);
        }
        finally
        {
            Cache.NotifyLocalMutationCompleted();
        }
    }

    internal static void MoveDirectory(string sourcePath, string destinationPath)
    {
        try
        {
            string sourceFullPath = Cache.ResolveFullPath(sourcePath);
            string destinationFullPath = Cache.ResolveFullPath(destinationPath);
            SystemIOWrapper.MoveDirectory(sourceFullPath, destinationFullPath);
        }
        finally
        {
            Cache.NotifyLocalMutationCompleted();
        }
    }

    // =========================================================================
    // 目录枚举
    // =========================================================================

    internal static string[] GetFiles(string relativePath, string pattern, SearchOption option)
    {
        if (option == SearchOption.TopDirectoryOnly)
        {
            return Cache.GetFiles(relativePath, pattern).ToArray();
        }

        // AllDirectories: 递归获取
        List<string> result = [];
        Stack<string> pending = new();
        pending.Push(relativePath);
        while (pending.Count > 0)
        {
            string relativeDir = pending.Pop();
            result.AddRange(Cache.GetFiles(relativeDir, pattern));
            foreach (string subDir in Cache.GetDirectories(relativeDir, "*"))
            {
                pending.Push(subDir);
            }
        }
        return result.ToArray();
    }

    internal static string[] GetDirectories(string relativePath, string pattern, SearchOption option)
    {
        if (option == SearchOption.TopDirectoryOnly)
        {
            return Cache.GetDirectories(relativePath, pattern).ToArray();
        }

        // AllDirectories: 递归获取
        List<string> result = [];
        Stack<string> pending = new();
        pending.Push(relativePath);
        while (pending.Count > 0)
        {
            string relativeDir = pending.Pop();
            result.AddRange(Cache.GetDirectories(relativeDir, pattern));
            foreach (string subDir in Cache.GetDirectories(relativeDir, "*"))
            {
                pending.Push(subDir);
            }
        }
        return result.ToArray();
    }

    internal static IEnumerable<string> EnumerateFiles(string relativePath, string pattern, SearchOption option)
    {
        return GetFiles(relativePath, pattern, option);
    }

    internal static IEnumerable<string> EnumerateFiles(
        string relativePath,
        string pattern,
        string[]? pathFilters,
        Func<string, bool>? acceptRelativePath = null,
        SearchOption option = SearchOption.AllDirectories)
    {
        string[] files = GetFiles(relativePath, pattern, option);

        foreach (string file in files)
        {
            if (acceptRelativePath != null && !acceptRelativePath(file))
            {
                continue;
            }

            if (pathFilters != null && pathFilters.Length > 0)
            {
                bool matched = pathFilters.Any(p => file.Contains(p, StringComparison.Ordinal));
                if (!matched)
                {
                    continue;
                }
            }

            yield return file;
        }
    }

    internal static IEnumerable<string> EnumerateDirectories(string relativePath, string pattern, SearchOption option)
    {
        return GetDirectories(relativePath, pattern, option);
    }

    internal static IEnumerable<string> EnumerateFileSystemEntries(string relativePath)
    {
        foreach (string dir in Cache.GetDirectories(relativePath, "*"))
        {
            yield return dir;
        }
        foreach (string file in Cache.GetFiles(relativePath, "*"))
        {
            yield return file;
        }
    }

    // =========================================================================
    // 文件树快照（委托给 FileSystemCache）
    // =========================================================================

    /// <summary>
    /// 获取文件树快照（供实体构建使用）。
    /// </summary>
    internal static FileTreeSnapshot GetFileTreeSnapshot()
    {
        return Cache.GetFileTreeSnapshot(WorkspacePath.RootDirectory);
    }

    internal static FileTreeSnapshot GetFileTreeSnapshot(string relativeRoot)
    {
        return Cache.GetFileTreeSnapshot(relativeRoot);
    }

    // =========================================================================
    // 时间戳查询
    // =========================================================================

    internal static long GetFileLastWriteTimeUtcTicks(string relativePath)
    {
        string fullPath = Cache.ResolveFullPath(relativePath);
        return SystemIOWrapper.GetFileLastWriteTimeUtc(fullPath).Ticks;
    }

    internal static long GetFileLength(string relativePath)
    {
        string fullPath = Cache.ResolveFullPath(relativePath);
        return SystemIOWrapper.GetFileLength(fullPath);
    }

    internal static long GetDirectoryLastWriteTimeUtcTicks(string relativePath)
    {
        string fullPath = Cache.ResolveFullPath(relativePath);
        return Directory.GetLastWriteTimeUtc(fullPath).Ticks;
    }

    internal static DirectoryInfo? GetParent(string relativePath)
    {
        string fullPath = Cache.ResolveFullPath(relativePath);
        return SystemIOWrapper.GetParent(fullPath);
    }
}

// =============================================================================
// 工程文件写入统一入口
// =============================================================================

/// <summary>
/// 工程文件写入统一入口。
/// 格式固定：UTF-8 无 BOM + LF 换行。
///
/// 警告：这是内部工具，工作区应通过实体模型（ProjectEntity.Save）
/// 写入文件，而不是直接使用此类。
/// </summary>
internal static class ProjectFileWriter
{
    /// <summary>文本文件写入根方法（仅接受工程根相对路径）</summary>
    internal static void WriteText(string relativePath, string text)
    {
        FileIO.WriteAllText(relativePath, text);
    }

    /// <summary>
    /// guid 文件写入（强制验证 32 位十六进制，保留原有格式，仅替换 guid 字段）
    ///
    /// 格式规则：
    /// 1. 无其他字段时：直接写入 32 位十六进制，如 "deadbeefdeadbeefdeadbeefdeadbeef"
    /// 2. 有其他字段时：写入 "guid:deadbeefdeadbeefdeadbeefdeadbeef"（冒号后无空格）
    ///
    /// 核心保证：如果 GUID 内容未变化，绝不会触发实际写入操作。
    /// </summary>
    internal static void WriteGuid(string relativePath, string guid)
    {
        if (!GuidText.Validate(guid)) throw new ArgumentException("GUID 格式无效", nameof(guid));

        string normalizedPath = WorkspacePath.RequireRelativePath(relativePath);

        // 读取原有内容（如果文件不存在，按新文件处理）
        bool guidFileExists;
        string existingText;
        try
        {
            existingText = FileIO.ReadAllText(normalizedPath);
            guidFileExists = true;
        }
        catch (FileNotFoundException)
        {
            existingText = string.Empty;
            guidFileExists = false;
        }

        GuidJsonRecord record = guidFileExists ? new GuidJsonRecord(existingText) : new GuidJsonRecord();
        record.SetGuid(guid);
        string updatedText = record.ToText();

        if (string.Equals(updatedText, existingText, StringComparison.Ordinal))
            return;

        WriteText(normalizedPath, updatedText);
    }
}
