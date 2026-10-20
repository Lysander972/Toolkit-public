using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

internal sealed class InMemoryWorkspaceOverlay
{
    private readonly object _lock = new();
    private readonly Dictionary<string, OverlayFile> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal) { WorkspacePath.RootDirectory };
    private long _version;

    public void InitializeFromSnapshot(FileTreeSnapshot snapshot)
    {
        lock (_lock)
        {
            _files.Clear();
            _directories.Clear();
            _directories.Add(WorkspacePath.RootDirectory);
            CollectSnapshotNode(snapshot.Root);
            _version++;
        }
    }

    public void Apply(IReadOnlyList<AtomicOperation> operations)
    {
        if (operations.Count == 0)
        {
            return;
        }

        AtomicOperation[] ordered = operations
            .OrderBy(static op => AtomicOperationOrder.GetExecutionOrder(op.Type))
            .ThenBy(static op => AtomicOperationOrder.GetSecondaryOrder(op))
            .ThenBy(static op => op.RelativePath, StringComparer.Ordinal)
            .ToArray();

        lock (_lock)
        {
            foreach (AtomicOperation op in ordered)
            {
                ApplySingle(op);
            }
            _version++;
        }
    }

    public bool FileExists(string relativePath)
    {
        string path = WorkspacePath.RequireRelativePath(relativePath);
        lock (_lock)
        {
            return _files.ContainsKey(path);
        }
    }

    public bool DirectoryExists(string relativePath)
    {
        string path = WorkspacePath.RequireRelativePath(relativePath);
        lock (_lock)
        {
            return _directories.Contains(path);
        }
    }

    public byte[] ReadAllBytes(string relativePath)
    {
        string path = WorkspacePath.RequireRelativePath(relativePath);
        lock (_lock)
        {
            if (!_files.TryGetValue(path, out OverlayFile file))
            {
                throw new FileNotFoundException($"文件不存在: {relativePath}", relativePath);
            }
            return file.Content.ToArray();
        }
    }

    public FileTreeSnapshot GetFileTreeSnapshot(string relativeRoot)
    {
        string rootPath = WorkspacePath.RequireRelativePath(relativeRoot);
        lock (_lock)
        {
            FileTreeNode root = new(rootPath, rootPath == WorkspacePath.RootDirectory ? string.Empty : RelativePathUtils.GetLastSegment(rootPath), 0);
            PopulateSnapshotNode(root, rootPath);
            return new FileTreeSnapshot { Root = root };
        }
    }

    public IReadOnlyList<string> GetFiles(string relativeDir, string pattern)
    {
        string dir = WorkspacePath.RequireRelativePath(relativeDir);
        lock (_lock)
        {
            return _files.Keys
                .Where(path => string.Equals(RelativePathUtils.GetParentDirectory(path), dir, StringComparison.Ordinal))
                .Where(path => MatchesPattern(RelativePathUtils.GetLastSegment(path), pattern))
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public IReadOnlyList<string> GetDirectories(string relativeDir, string pattern)
    {
        string dir = WorkspacePath.RequireRelativePath(relativeDir);
        lock (_lock)
        {
            return _directories
                .Where(path => path != WorkspacePath.RootDirectory)
                .Where(path => string.Equals(RelativePathUtils.GetParentDirectory(path), dir, StringComparison.Ordinal))
                .Where(path => MatchesPattern(RelativePathUtils.GetLastSegment(path), pattern))
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray();
        }
    }

    private void CollectSnapshotNode(FileTreeNode node)
    {
        _directories.Add(node.RelativePath);
        foreach (string fileName in node.Files)
        {
            string relativePath = node.GetFileRelativePath(fileName);
            byte[] content = FileIO.ReadAllBytes(relativePath);
            _files[relativePath] = new OverlayFile(content, node.GetFileSignature(fileName));
        }

        foreach (FileTreeNode child in node.Children)
        {
            CollectSnapshotNode(child);
        }
    }

    private void ApplySingle(AtomicOperation op)
    {
        string path = WorkspacePath.RequireRelativePath(op.RelativePath);
        switch (op.Type)
        {
            case AtomicOperationType.CreateDirectory:
                _directories.Add(path);
                break;
            case AtomicOperationType.DeleteDirectory:
                if (path == WorkspacePath.RootDirectory)
                {
                    throw new InvalidOperationException("禁止删除工程根目录");
                }
                foreach (string file in _files.Keys.Where(p => p.StartsWith(path + "/", StringComparison.Ordinal)).ToArray())
                {
                    _files.Remove(file);
                }
                foreach (string dir in _directories.Where(p => p == path || p.StartsWith(path + "/", StringComparison.Ordinal)).ToArray())
                {
                    _directories.Remove(dir);
                }
                break;
            case AtomicOperationType.WriteFile:
                if (op.Content == null)
                {
                    throw new InvalidOperationException($"WriteFile 操作必须提供内容: {path}");
                }
                EnsureParentDirectories(path);
                long ticks = DateTime.UtcNow.Ticks;
                _files[path] = new OverlayFile(op.Content.ToArray(), new FileSignature(ticks, ticks));
                break;
            case AtomicOperationType.DeleteFile:
                _files.Remove(path);
                break;
            default:
                throw new InvalidOperationException($"未知的原子操作类型: {op.Type}");
        }
    }

    private void EnsureParentDirectories(string fileRelativePath)
    {
        string parent = RelativePathUtils.GetParentDirectory(fileRelativePath);

        Stack<string> pending = new();
        string current = parent;
        while (current != WorkspacePath.RootDirectory && !_directories.Contains(current))
        {
            pending.Push(current);
            current = RelativePathUtils.GetParentDirectory(current);
        }

        while (pending.Count > 0)
        {
            _directories.Add(pending.Pop());
        }
    }

    private void PopulateSnapshotNode(FileTreeNode node, string relativePath)
    {
        node.SetDirectorySignature(new DirectorySignature(_version, _version));

        foreach ((string path, OverlayFile file) in _files.OrderBy(static kvp => kvp.Key, StringComparer.Ordinal))
        {
            if (string.Equals(RelativePathUtils.GetParentDirectory(path), relativePath, StringComparison.Ordinal))
            {
                string fileName = RelativePathUtils.GetLastSegment(path);
                if (!WorkspacePath.IsIgnoredFileName(fileName))
                {
                    node.AddFile(fileName, file.Signature);
                }
            }
        }

        string[] childDirs = _directories
            .Where(path => path != WorkspacePath.RootDirectory)
            .Where(path => string.Equals(RelativePathUtils.GetParentDirectory(path), relativePath, StringComparison.Ordinal))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        foreach (string childDir in childDirs)
        {
            string name = RelativePathUtils.GetLastSegment(childDir);
            if (WorkspacePath.IsIgnoredSegment(name))
            {
                continue;
            }
            FileTreeNode child = node.AddChildDirectory(name);
            PopulateSnapshotNode(child, childDir);
        }
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        if (pattern == "*")
        {
            return true;
        }
        if (pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            return fileName.EndsWith(pattern[1..], StringComparison.Ordinal);
        }
        return string.Equals(fileName, pattern, StringComparison.Ordinal);
    }

    private readonly record struct OverlayFile(byte[] Content, FileSignature Signature);
}

internal sealed class RecordingPersistenceOperationApplier : IPersistenceOperationApplier
{
    private readonly InMemoryWorkspaceOverlay _overlay;
    private readonly List<LocalDiffResult> _diffs = [];

    public RecordingPersistenceOperationApplier(InMemoryWorkspaceOverlay overlay)
    {
        _overlay = overlay;
    }

    public IReadOnlyList<LocalDiffResult> Diffs => _diffs;

    public void Apply(
        LocalDiffResult diff,
        IReadOnlyList<AtomicOperation> operations,
        IReadOnlyCollection<string> exclusiveFilePaths,
        Action<IReadOnlyDictionary<string, FileStream>> beforeExecute)
    {
        beforeExecute(new Dictionary<string, FileStream>(StringComparer.Ordinal));
        _diffs.Add(diff);
        _overlay.Apply(operations);
    }
}
