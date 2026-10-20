
using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

/// <summary>
/// 文件树快照管理器 - 维护文件路径集合，提供变更分析。
///
/// 实体覆盖范围由实体自身的 GetAllCoverage() 方法计算，每次重建时直接返回。
/// 不在此处持续维护绑定关系。
/// </summary>
internal sealed class FileTreeBinding
{
    /// <summary>全局单例</summary>
    public static FileTreeBinding Instance { get; } = new();

    // 根节点引用（用于实体查询目录下的文件列表）
    private FileTreeNode? _root;

    // 当前覆盖映射（从 ProjectRootEntity.GetAllCoverage() 计算）
    private EntityCoverage? _coverage;

    private FileTreeBinding() { }

    // ============================================================================
    // 文件树快照管理
    // ============================================================================

    /// <summary>
    /// 刷新文件树快照。
    /// </summary>
    public void RefreshFilePaths(FileTreeNode root)
    {
        _root = root;
    }

    /// <summary>
    /// 设置当前覆盖映射（从 ProjectRootEntity.GetAllCoverage() 计算）。
    /// </summary>
    public void SetCoverage(EntityCoverage coverage)
    {
        _coverage = coverage;
    }

    // ============================================================================
    // 查询方法
    // ============================================================================

    /// <summary>是否已绑定文件树快照（内存构造的实体树场景为 false）。</summary>
    public bool IsBound => _root != null;

    /// <summary>
    /// 根据相对路径查找文件树节点。
    /// </summary>
    public FileTreeNode? FindNode(string relativePath)
    {
        if (_root == null)
            throw new InvalidOperationException("FileTreeBinding 未绑定到文件树");

        return FindNodeRecursive(_root, relativePath);
    }

    private static FileTreeNode? FindNodeRecursive(FileTreeNode node, string relativePath)
    {
        if (node.RelativePath.Equals(relativePath, StringComparison.Ordinal))
            return node;

        string prefix = node.RelativePath == WorkspacePath.RootDirectory ? string.Empty : node.RelativePath + "/";
        if (!relativePath.StartsWith(prefix, StringComparison.Ordinal) && node.RelativePath != WorkspacePath.RootDirectory)
            return null;

        foreach (FileTreeNode child in node.Children)
        {
            FileTreeNode? result = FindNodeRecursive(child, relativePath);
            if (result != null)
                return result;
        }

        return null;
    }

    /// <summary>
    /// 根据路径查找绑定的实体（精确匹配）。
    /// </summary>
    public ProjectEntity? GetDirectEntity(string targetRelativePath)
    {
        if (_coverage == null)
            return null;
        if (_coverage.Files.ContainsKey(targetRelativePath))
            return _coverage.Files[targetRelativePath];
        if (_coverage.Directories.ContainsKey(targetRelativePath))
            return _coverage.Directories[targetRelativePath];
        return null;
    }

    /// <summary>
    /// 根据路径查找绑定的实体（支持向上查找父实体）。
    /// </summary>
    public ProjectEntity? GetEntity(string targetRelativePath)
    {
        if (_coverage == null)
            return null;

        // 精确匹配
        if (_coverage.Files.ContainsKey(targetRelativePath))
            return _coverage.Files[targetRelativePath];
        if (_coverage.Directories.ContainsKey(targetRelativePath))
            return _coverage.Directories[targetRelativePath];

        // 向上查找：找到最近的上级绑定实体
        string currentRelativePath = targetRelativePath;
        while (currentRelativePath.Length > 0 && currentRelativePath != WorkspacePath.RootDirectory)
        {
            if (_coverage.Files.ContainsKey(currentRelativePath))
                return _coverage.Files[currentRelativePath];
            if (_coverage.Directories.ContainsKey(currentRelativePath))
                return _coverage.Directories[currentRelativePath];

            string parentRelativePath = RelativePathUtils.GetParentDirectory(currentRelativePath);
            if (parentRelativePath == currentRelativePath) break;
            currentRelativePath = parentRelativePath;
        }

        return null;
    }

    /// <summary>
    /// 获取指定目录下所有已绑定的实体。
    /// </summary>
    public IEnumerable<ProjectEntity> GetEntitiesUnderPath(string dirRelativePath)
    {
        if (_coverage == null)
            return Enumerable.Empty<ProjectEntity>();

        string prefix = string.Empty;
        if (dirRelativePath != WorkspacePath.RootDirectory)
            prefix = dirRelativePath + "/";

        HashSet<ProjectEntity> results = [];
        foreach (KeyValuePair<string, ProjectEntity> kvp in _coverage.Files)
        {
            string path = kvp.Key;
            if (dirRelativePath == WorkspacePath.RootDirectory ||
                path.Equals(dirRelativePath, StringComparison.Ordinal) ||
                path.StartsWith(prefix, StringComparison.Ordinal))
            {
                results.Add(kvp.Value);
            }
        }
        foreach (KeyValuePair<string, ProjectEntity> kvp in _coverage.Directories)
        {
            string path = kvp.Key;
            if (dirRelativePath == WorkspacePath.RootDirectory ||
                path.Equals(dirRelativePath, StringComparison.Ordinal) ||
                path.StartsWith(prefix, StringComparison.Ordinal))
            {
                results.Add(kvp.Value);
            }
        }

        return results;
    }

    // ============================================================================
    // 清理
    // ============================================================================

    /// <summary>
    /// 清空所有状态。
    /// </summary>
    public void Clear()
    {
        _root = null;
        _coverage = null;
    }

    // ============================================================================
    // 变更分析
    // ============================================================================

    /// <summary>
    /// 对比新旧文件树，确定所有受影响的实体。
    /// </summary>
    public FileTreeChangeImpact AnalyzeChanges(FileTreeSnapshot oldTree, FileTreeSnapshot newTree)
    {
        FileTreeChangeImpact impact = new();
        CompareNodes(oldTree.Root, newTree.Root, impact);
        return impact;
    }

    private void CompareNodes(FileTreeNode? oldNode, FileTreeNode? newNode, FileTreeChangeImpact impact)
    {
        if (oldNode != null && newNode == null)
        {
            foreach (ProjectEntity entity in GetEntitiesUnderPath(oldNode.RelativePath))
                impact.EntitiesToUnload.Add(entity);
            return;
        }

        if (oldNode == null && newNode != null)
        {
            if (GetEntity(newNode.RelativePath) is { } entity)
            {
                impact.EntitiesToReload.Add(entity);
                return;
            }

            impact.NewRelativePaths.Add(newNode.RelativePath);
            return;
        }

        if (oldNode != null && newNode != null)
        {
            // 子树递归哈希相等 ⟺ 子树全部元数据一致：整层剪枝，无影响刷新时 diff 为 O(1)。
            // Zero = 未携带哈希（手工构建的树）：回落全量比较，禁止把"无哈希"当成"无变化"。
            if (!oldNode.SubtreeHash.Equals(TreeNodeHash.Zero)
                && oldNode.SubtreeHash.Equals(newNode.SubtreeHash))
            {
                return;
            }
            CompareFiles(oldNode, newNode, impact);
            CompareChildren(oldNode, newNode, impact);
        }
    }

    private void CompareFiles(FileTreeNode oldNode, FileTreeNode newNode, FileTreeChangeImpact impact)
    {
        HashSet<string> oldFiles = new(oldNode.Files, StringComparer.Ordinal);
        HashSet<string> newFiles = new(newNode.Files, StringComparer.Ordinal);

        foreach (string? file in newFiles.Where(f => !oldFiles.Contains(f)))
            HandleFileAdded(newNode, file, impact);

        foreach (string? file in oldFiles.Where(f => !newFiles.Contains(f)))
            HandleFileRemoved(oldNode, file, impact);

        foreach (string? file in oldFiles.Where(f => newFiles.Contains(f)))
        {
            if (IsFileChanged(oldNode, newNode, file))
                HandleFileModified(newNode, file, impact);
        }
    }

    private static bool IsFileChanged(FileTreeNode oldNode, FileTreeNode newNode, string fileName) =>
        !oldNode.GetFileSignature(fileName).Equals(newNode.GetFileSignature(fileName));

    private void HandleFileAdded(FileTreeNode node, string fileName, FileTreeChangeImpact impact)
    {
        string fileRelativePath = node.GetFileRelativePath(fileName);
        impact.ModifiedRelativePaths.Add(fileRelativePath);

        if (GetEntity(fileRelativePath) is { } entity)
        {
            impact.EntitiesToReload.Add(entity);
            return;
        }

        // 文件路径不在覆盖范围内，向上搜索父目录
        if (FindAncestorEntity(fileRelativePath) is { } ancestor)
        {
            impact.EntitiesToReload.Add(ancestor);
            return;
        }

        impact.NewRelativePaths.Add(fileRelativePath);
    }

    private void HandleFileRemoved(FileTreeNode node, string fileName, FileTreeChangeImpact impact)
    {
        string fileRelativePath = node.GetFileRelativePath(fileName);
        impact.ModifiedRelativePaths.Add(fileRelativePath);

        if (GetDirectEntity(fileRelativePath) is { } direct)
        {
            impact.EntitiesToUnload.Add(direct);
            return;
        }

        if (GetEntity(fileRelativePath) is { } entity)
        {
            impact.EntitiesToReload.Add(entity);
            return;
        }

        // 文件路径不在覆盖范围内，向上搜索父目录
        if (FindAncestorEntity(fileRelativePath) is { } ancestor)
            impact.EntitiesToReload.Add(ancestor);
    }

    private void HandleFileModified(FileTreeNode node, string fileName, FileTreeChangeImpact impact)
    {
        string fileRelativePath = node.GetFileRelativePath(fileName);
        impact.ModifiedRelativePaths.Add(fileRelativePath);

        if (GetEntity(fileRelativePath) is { } entity)
        {
            impact.EntitiesToReload.Add(entity);
            return;
        }

        // 文件路径不在覆盖范围内，向上搜索父目录
        if (FindAncestorEntity(fileRelativePath) is { } ancestor)
            impact.EntitiesToReload.Add(ancestor);
    }

    /// <summary>
    /// 向上搜索父目录，找到最近的在覆盖范围内的实体。
    /// </summary>
    private ProjectEntity? FindAncestorEntity(string relativePath)
    {
        string currentPath = RelativePathUtils.GetParentDirectory(relativePath);
        while (currentPath.Length > 0)
        {
            if (GetEntity(currentPath) is { } entity)
                return entity;

            string parentPath = RelativePathUtils.GetParentDirectory(currentPath);
            if (parentPath == currentPath)
                break;
            currentPath = parentPath;
        }
        return null;
    }

    private void CompareChildren(FileTreeNode oldNode, FileTreeNode newNode, FileTreeChangeImpact impact)
    {
        Dictionary<string, FileTreeNode> oldChildren = oldNode.Children.ToDictionary(static c => c.Name, StringComparer.Ordinal);
        Dictionary<string, FileTreeNode> newChildren = newNode.Children.ToDictionary(static c => c.Name, StringComparer.Ordinal);
        IEnumerable<string> allNames = oldChildren.Keys.Union(newChildren.Keys, StringComparer.Ordinal);

        foreach (string? name in allNames)
        {
            FileTreeNode? oldChild = oldChildren.GetValueOrDefault(name);
            FileTreeNode? newChild = newChildren.GetValueOrDefault(name);
            CompareNodes(oldChild, newChild, impact);
        }
    }
}

/// <summary>
/// 文件树变更影响分析结果。
/// </summary>
internal sealed class FileTreeChangeImpact
{
    /// <summary>需要卸载的实体</summary>
    public HashSet<ProjectEntity> EntitiesToUnload { get; } = [];

    /// <summary>需要重载的实体</summary>
    public HashSet<ProjectEntity> EntitiesToReload { get; } = [];

    /// <summary>新增的相对路径</summary>
    public HashSet<string> NewRelativePaths { get; } = new(StringComparer.Ordinal);

    /// <summary>发生变更的相对路径</summary>
    public HashSet<string> ModifiedRelativePaths { get; } = new(StringComparer.Ordinal);

    /// <summary>是否存在任何影响</summary>
    public bool HasImpact => EntitiesToUnload.Count > 0 || EntitiesToReload.Count > 0 || NewRelativePaths.Count > 0 || ModifiedRelativePaths.Count > 0;
}
