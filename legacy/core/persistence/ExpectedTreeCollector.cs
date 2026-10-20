// Core_Persistence_Diff_Spec.md - 第 6 节实现
// SaveChanges 运行时检查确保父级链条完整：
//   - 检查1：必须是可写工程根，或者有父级
//   - 检查2：父级如果存在，必须是只读实体
//
// 第 6.3 节剪枝实现：脏标记驱动。
// 本次会话中显式传播了脏标记（MarkDirtyRecursive）的实体收集全量内容；
// 未标记实体按"剪枝声明"处理：仅声明其覆盖范围路径（防误删），
// 跳过内容序列化与 diff 内容比较（期望内容与磁盘一致）。

namespace Toolkit.Core.Persistence;

internal static class ExpectedTreeCollector
{
    public static void Collect(ProjectEntity entity, ExpectedTreeBuffer buffer, IReadOnlySet<string> dirtyPaths)
    {
        buffer.Clear();
        buffer.StartingEntityPath = entity.实体_完整相对路径标识;

        if (dirtyPaths.Count == 0)
        {
            // 无传播记录：全量收集（现状行为）。剪枝只在本会话显式传播了脏标记时启用。
            entity.DeclareAllFileContents(buffer);
            return;
        }

        Stack<ProjectEntity> pending = new();
        pending.Push(entity);
        while (pending.Count > 0)
        {
            ProjectEntity current = pending.Pop();
            if (!ReferenceEquals(current, entity) && !dirtyPaths.Contains(current.实体_完整相对路径标识))
            {
                DeclareUnchangedSubtree(current, buffer);
                continue;
            }

            current.DeclareFileContents(buffer);
            if (current is IHasChildren<ProjectEntity> hasChildren)
            {
                foreach (ProjectEntity child in hasChildren.Children.Reverse())
                    pending.Push(child);
            }
        }
    }

    /// <summary>
    /// 剪枝声明：按实体的 GetSelfCoverage 声明目录与文件路径（Unchanged），
    /// 跳过 DeclareFileContents 的内容序列化。
    /// DeclareFileContents 与 GetSelfCoverage 的一致性由实体实现强制保证，
    /// 因此路径声明结果与全量收集等价，仅内容标记为跳过。
    /// </summary>
    private static void DeclareUnchangedSubtree(ProjectEntity entity, ExpectedTreeBuffer buffer)
    {
        EntityCoverage coverage = entity.GetSelfCoverage();
        foreach (string dir in coverage.Directories.Keys)
            buffer.DeclareDirectory(dir);
        foreach (string file in coverage.Files.Keys)
            buffer.DeclareFileUnchanged(file);

        if (entity is IHasChildren<ProjectEntity> hasChildren)
        {
            foreach (ProjectEntity child in hasChildren.Children)
                DeclareUnchangedSubtree(child, buffer);
        }
    }
}
