// Core_Persistence_Diff_Spec.md - 第 6 节实现
// 期望缓冲区 - 收集起点实体在本次保存中声明的最终状态

using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

/// <summary>
/// 期望缓冲区。
///
/// 核心职责（规范第 6 节）：
/// 1. 收集起点实体在本次保存中声明的最终状态
/// 2. 只描述目录与文件内容，收集阶段禁止触碰文件系统
/// 3. 级联写写入缓冲区（而非直接落盘）
/// </summary>
internal sealed class ExpectedTreeBuffer
{
    // null = 剪枝声明（期望内容与磁盘一致，diff 跳过比较）
    private readonly Dictionary<string, byte[]?> _expectedFiles = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expectedDirectories = new(StringComparer.Ordinal);

    /// <summary>起点实体路径（用于日志和调试）</summary>
    public string? StartingEntityPath { get; set; }

    /// <summary>清空缓冲区</summary>
    public void Clear()
    {
        _expectedFiles.Clear();
        _expectedDirectories.Clear();
    }

    /// <summary>
    /// 声明目录存在。
    /// 仅记录目录路径本身，不做任何衍生推断。
    /// </summary>
    public void DeclareDirectory(string relativePath)
    {
        string path = WorkspacePath.RequireRelativePath(relativePath);
        if (path == WorkspacePath.RootDirectory)
        {
            return;
        }

        _expectedDirectories.Add(path);
    }

    /// <summary>
    /// 声明文件存在（带内容）。
    /// 只记录文件本身，不处理父目录节点。
    /// 禁止强行塞什么"自动"和"智能"补全路径!!!!禁止接着和用户对抗!!!!
    /// 用户已经说了很多次了不要这么写!!!!!!!!!!!!!!
    /// </summary>
    public void DeclareFile(string relativePath, byte[] content)
    {
        string path = WorkspacePath.RequireRelativePath(relativePath);
        _expectedFiles[path] = content;
    }

    /// <summary>
    /// 声明文件存在（字符串内容）。
    /// 内部固定使用 UTF-8 无 BOM 编码 + LF 换行。
    /// </summary>
    public void DeclareFile(string relativePath, string content)
    {
        string path = WorkspacePath.RequireRelativePath(relativePath);
        _expectedFiles[path] = TextEncoding.GetBytesLfOnly(content);
    }

    /// <summary>
    /// 剪枝声明：文件存在且期望内容与磁盘一致（脏标记剪枝跳过实体）。
    /// diff 对这类文件跳过内容比较（视为未变），同时参与覆盖声明防止误删。
    /// </summary>
    public void DeclareFileUnchanged(string relativePath)
    {
        string path = WorkspacePath.RequireRelativePath(relativePath);
        _expectedFiles[path] = null;
    }

    /// <summary>
    /// 获取已声明的所有文件路径。
    /// 用于 diff 计算。Content 为 null 表示剪枝声明（跳过比较）。
    /// </summary>
    public IEnumerable<(string RelativePath, byte[]? Content)> GetAllFiles()
    {
        foreach ((string path, byte[]? data) in _expectedFiles)
        {
            yield return (path, data);
        }
    }

    /// <summary>
    /// 获取已声明的所有目录路径。
    /// </summary>
    public IEnumerable<string> GetAllDirectories()
    {
        foreach (string path in _expectedDirectories)
        {
            yield return path;
        }
    }
}
