namespace Toolkit.Core.Persistence;
// =============================================================================
// 文件树快照类型
// =============================================================================
//
// 这些类型从 Internal/FileSystemCache.cs 迁移至此，因为它们被 ProjectModel
// （公共 API）使用，需要是 public 的。
// =============================================================================

/// <summary>
/// 文件树快照 - 供实体构建使用的文件树视图
///
/// 【重要】本快照不基于时间失效。
/// 缓存失效只能由 FileSystemCache 统一 timer 整体刷新触发，禁止外部单点失效。
/// </summary>
public sealed class FileTreeSnapshot
{
    /// <summary>根节点</summary>
    public required FileTreeNode Root { get; init; }
}

/// <summary>
/// 文件树节点
/// </summary>
public sealed class FileTreeNode
{
    /// <summary>相对工程根的路径（统一不带尾部斜杠）</summary>
    public string RelativePath { get; }

    /// <summary>节点名称（不含路径）</summary>
    public string Name { get; }

    /// <summary>是否为目录</summary>
    public bool IsDirectory => true;

    /// <summary>子目录列表</summary>
    public List<FileTreeNode> Children { get; } = [];

    /// <summary>目录下的文件名列表（仅文件名，不含路径）</summary>
    public List<string> Files { get; } = [];


    private readonly Dictionary<string, FileSignature> _fileSignatures = new(StringComparer.Ordinal);
    internal DirectorySignature DirectorySignature { get; private set; }

    /// <summary>本层直接成员哈希（文件名+创建/修改时间戳、子目录名），不含子层内容。</summary>
    internal TreeNodeHash MemberHash { get; private set; }

    /// <summary>子树递归哈希（本层成员哈希 + 全部子层子树哈希）。相等 ⟺ 子树元数据完全一致，可用于整层剪枝。</summary>
    internal TreeNodeHash SubtreeHash { get; private set; }

    internal void SetHashes(TreeNodeHash memberHash, TreeNodeHash subtreeHash)
    {
        MemberHash = memberHash;
        SubtreeHash = subtreeHash;
    }

    /// <summary>深度（工程根为0）</summary>
    public int Depth { get; }

    public FileTreeNode(string relativePath, string name, int depth)
    {
        RelativePath = relativePath;
        Name = name;
        Depth = depth;
    }

    /// <summary>添加子目录</summary>
    public FileTreeNode AddChildDirectory(string name)
    {
        string childPath = PathCombiner.CombineRelativePath(RelativePath, name);
        FileTreeNode child = new(childPath, name, Depth + 1);
        Children.Add(child);
        return child;
    }

    /// <summary>获取文件相对路径</summary>
    public string GetFileRelativePath(string fileName) =>
        PathCombiner.CombineRelativePath(RelativePath, fileName);

    /// <summary>获取子目录相对路径</summary>
    public string GetChildDirectoryRelativePath(string dirName) =>
        PathCombiner.CombineRelativePath(RelativePath, dirName);

    internal void AddFile(string fileName, FileSignature signature)
    {
        Files.Add(fileName);
        _fileSignatures[fileName] = signature;
    }

    internal void SetDirectorySignature(DirectorySignature signature) =>
        DirectorySignature = signature;

    internal FileSignature GetFileSignature(string fileName) =>
        _fileSignatures[fileName];

    internal string GetDirectoryRelativePath() => RelativePath;
}
