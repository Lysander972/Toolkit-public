// Core_Persistence_Diff_Spec.md - 第 7.2 节 + 第 9 节实现
// 局部当前状态快照 + 并发冲突校验

using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

internal readonly record struct LocalFileSignature(long CreationTimeUtcTicks, long LastWriteTimeUtcTicks);

/// <summary>
/// 局部文件系统快照。
///
/// 用于计算删除操作的"起点范围"（集合 A）。
/// 从实体路径开始递归收集，代表子实体必然处于的范围。
///
/// 注意：这只是粗略估算，实体的完整覆盖范围可能超出此集合（如 Map/Mech 的额外文件）。
/// 但对于删除操作来说，只需要确保不误删起点范围外的文件即可。
/// </summary>
internal sealed class LocalFileSystemSnapshot
{
    public Dictionary<string, LocalFileSignature> Files { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Directories { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 从已有文件树快照中筛选起点范围内的内容（集合 A）。
    ///
    /// 规则：
    /// 1. 如果起点是文件，只收集该文件
    /// 2. 如果起点是目录，递归收集目录和文件
    /// </summary>
    public static LocalFileSystemSnapshot FilterFromTree(FileTreeSnapshot tree, string entityRelativePath)
    {
        LocalFileSystemSnapshot snapshot = new();
        string normalized = WorkspacePath.RequireRelativePath(entityRelativePath);

        // 尝试作为目录查找
        FileTreeNode? dirNode = FindNode(tree.Root, normalized);
        if (dirNode != null)
        {
            // 是目录，递归收集
            CollectRecursive(dirNode, snapshot);
            return snapshot;
        }

        // 不是目录，尝试作为文件收集
        CollectFile(tree, normalized, snapshot);
        return snapshot;
    }

    /// <summary>
    /// 收集单个文件到快照
    /// </summary>
    private static void CollectFile(FileTreeSnapshot tree, string filePath, LocalFileSystemSnapshot snapshot)
    {
        string parentDir = RelativePathUtils.GetParentDirectory(filePath);
        string fileName = RelativePathUtils.GetLastSegment(filePath);

#pragma warning disable TK011 // 文件路径可能无父目录或无文件名，跳过无效条目
        if (parentDir == string.Empty || fileName == string.Empty)
#pragma warning restore TK011
            return;

        FileTreeNode? parentNode = FindNode(tree.Root, parentDir);
        if (parentNode == null)
            return;

        // 检查文件是否存在
        if (parentNode.Files.Contains(fileName))
        {
            FileSignature sig = parentNode.GetFileSignature(fileName);
            snapshot.Files[filePath] = new LocalFileSignature(sig.CreationTimeUtcTicks, sig.LastWriteTimeUtcTicks);
        }
    }

    /// <summary>
    /// 在文件树中查找指定路径的节点。
    /// </summary>
    private static FileTreeNode? FindNode(FileTreeNode current, string targetPath)
    {
        // 精确匹配
        if (current.RelativePath == targetPath)
            return current;

        // 检查是否是子路径
        string prefix = string.Empty;
        if (current.RelativePath != WorkspacePath.RootDirectory)
            prefix = current.RelativePath + "/";
        if (!targetPath.StartsWith(prefix, StringComparison.Ordinal) && current.RelativePath != WorkspacePath.RootDirectory)
            return null;

        // 递归查找子节点
        foreach (FileTreeNode child in current.Children)
        {
            FileTreeNode? found = FindNode(child, targetPath);
            if (found != null) return found;
        }

        return null;
    }

    /// <summary>
    /// 递归收集节点下的所有文件和目录。
    /// </summary>
    private static void CollectRecursive(FileTreeNode node, LocalFileSystemSnapshot snapshot)
    {
        // 收集目录
        if (node.RelativePath != WorkspacePath.RootDirectory)
            snapshot.Directories.Add(node.RelativePath);

        // 收集文件
        foreach (string fileName in node.Files)
        {
            FileSignature sig = node.GetFileSignature(fileName);
            string fileRelativePath = node.GetFileRelativePath(fileName);
            snapshot.Files[fileRelativePath] = new LocalFileSignature(sig.CreationTimeUtcTicks, sig.LastWriteTimeUtcTicks);
        }

        // 递归子目录
        foreach (FileTreeNode child in node.Children)
            CollectRecursive(child, snapshot);
    }
}
