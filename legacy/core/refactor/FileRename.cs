using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Refactor;

/// <summary>
/// 可重命名标记接口（只读实体）。
/// 表示该实体类型有对应的可写版本支持重命名操作。
/// </summary>
public interface IRenameable
{
}

/// <summary>
/// 文件重命名结果
/// </summary>
public sealed record FileRenameData(
    string OldRelativePath,
    string NewRelativePath,
    IReadOnlyList<string> AffectedRelativePaths
);

/// <summary>
/// 文件重命名/移动工具。
/// 通过可写工程根进行操作，确保整体状态一致性。
///
/// 冲突判定规则（固定按 Windows 行为处理，无论当前运行平台）：
/// - 游戏主要运行在 Windows 环境，Windows 文件系统大小写不敏感
/// - 因此同一目录下，文件名或目录名忽略大小写后相同即视为冲突
/// - 此规则在所有平台上强制执行，不随运行环境变化
/// </summary>
public sealed class FileRename
{
    public RefactorResult<FileRenameData> Rename(string path, string newPath, ProjectModel model)
    {
        string sourcePath = WorkspacePath.RequireRelativePath(path);
        string targetPath = WorkspacePath.RequireRelativePath(newPath);

        if (sourcePath.Equals(targetPath, StringComparison.Ordinal))
            return RefactorResult<FileRenameData>.Fail("新路径与原路径相同");

        string sourceExt = RelativePathUtils.GetExtension(sourcePath);
        string targetExt = RelativePathUtils.GetExtension(targetPath);
        if (!sourceExt.Equals(targetExt, StringComparison.Ordinal))
            return RefactorResult<FileRenameData>.Fail($"禁止修改文件扩展名：{sourceExt} → {targetExt}");

        if (model.Find(sourcePath) is not ProjectEntity sourceEntity)
            return RefactorResult<FileRenameData>.Fail($"文件不存在: {sourcePath}");

        if (sourceEntity is not IRenameable)
            return RefactorResult<FileRenameData>.Fail($"该文件类型不支持重命名/移动: {sourceEntity.Kind}");

        string sourceParentDir = RelativePathUtils.GetParentDirectory(sourcePath);
        string targetParentDir = RelativePathUtils.GetParentDirectory(targetPath);
        string newName = RelativePathUtils.GetLastSegmentWithoutExtension(targetPath);
        string oldName = RelativePathUtils.GetLastSegmentWithoutExtension(sourcePath);
        string targetLastSegment = RelativePathUtils.GetLastSegment(targetPath);

        bool needRename = !oldName.Equals(newName, StringComparison.Ordinal);
        bool needMove = !sourceParentDir.Equals(targetParentDir, StringComparison.Ordinal);

        if (needRename && sourceExt.Equals(".code", StringComparison.Ordinal))
            return RefactorResult<FileRenameData>.Fail("对 .code 仅支持移动不支持改名，请使用 refactor_rename_script");

        if (!needRename && !needMove)
            return RefactorResult<FileRenameData>.Fail("无需修改");

        // 冲突检查：目标父级下是否已有同名实体（忽略大小写，Windows 行为）
        // 排除源实体自身，以允许纯大小写变更的重命名
        if (model.Find(targetParentDir) is ProjectEntity targetParentEntity &&
            targetParentEntity is IHasChildren<ProjectEntity> targetParentWithChildren)
        {
            foreach (ProjectEntity sibling in targetParentWithChildren.Children)
            {
                if (!string.Equals(sibling.实体_完整相对路径标识, sourcePath, StringComparison.Ordinal) &&
                    string.Equals(sibling.实体_无相对路径标识符, targetLastSegment, StringComparison.OrdinalIgnoreCase))
                {
                    if (sibling is DirectoryEntityBase)
                        return RefactorResult<FileRenameData>.Fail(
                            $"目标位置已存在同名目录'{sibling.实体_完整相对路径标识}'，当前不支持通过移动进行目录合并或覆盖目录");
                    return RefactorResult<FileRenameData>.Fail(
                        $"目标位置已存在同名文件'{sibling.实体_完整相对路径标识}'，当前不支持通过移动进行文件覆盖");
                }
            }
        }

        return ApplyRename(model, sourcePath, oldName, newName, sourceParentDir, targetParentDir, needMove);
    }

    private static RefactorResult<FileRenameData> ApplyRename(
        ProjectModel model,
        string sourcePath,
        string oldName,
        string newName,
        string sourceParentDir,
        string targetParentDir,
        bool needMove)
    {
        try
        {
            ProjectRootEntity? root = model.Root;
            WritableProjectRootEntity writableRoot = root.ToWritable();

            string? error = FindAndModifyInRoot(writableRoot, sourcePath, oldName, newName, sourceParentDir, targetParentDir, needMove);
            if (error != null)
                return RefactorResult<FileRenameData>.Fail(error);

            model.SaveChanges(writableRoot);

            string targetPath = RelativePathUtils.Combine(targetParentDir, newName + RelativePathUtils.GetExtension(sourcePath));
            return RefactorResult<FileRenameData>.Success(new FileRenameData(sourcePath, targetPath, [targetPath]));
        }
        catch (Exception ex) when (ex is not WorkspaceFileBlockedException and not WorkspaceStateChangedException)
        {
            return RefactorResult<FileRenameData>.Fail($"操作失败: {ex.Message}");
        }
    }

    private static string? FindAndModifyInRoot(
        WritableProjectRootEntity writableRoot,
        string sourcePath,
        string oldName,
        string newName,
        string sourceParentDir,
        string targetParentDir,
        bool needMove)
    {
        ProjectEntity? writableOldParent = FindEntityByPath(writableRoot, sourceParentDir);
        if (writableOldParent == null)
            return $"源父级不存在: {sourceParentDir}";

        ProjectEntity writableTargetParent;
        if (needMove)
        {
            ProjectEntity? found = FindEntityByPath(writableRoot, targetParentDir);
            if (found == null)
                return $"目标目录不存在: {targetParentDir}，当前不支持自动补全目录";
            writableTargetParent = found;
        }
        else
        {
            writableTargetParent = writableOldParent;
        }

        if (writableOldParent is not IHasChildren<ProjectEntity> oldParentWithChildren)
            return $"旧父级不支持子实体操作: {writableOldParent.GetType().Name}";

        if (writableTargetParent is not IHasChildren<ProjectEntity>)
            return $"目标父级不支持子实体操作: {writableTargetParent.GetType().Name}";

        ProjectEntity? child = oldParentWithChildren.Children
            .FirstOrDefault(c => string.Equals(c.实体_完整相对路径标识, sourcePath, StringComparison.Ordinal));

        if (child == null)
            return $"在父级中未找到实体: {sourcePath}";

        if (child is not IWritableEntity writableChild)
            return $"目标子实体不是可写实体: {child.GetType().Name}";

        // 内联脚本特殊处理
        if (child is WritableInlineScriptEntity && writableOldParent is WritableScriptGroupEntity scriptGroup)
        {
            if (needMove && !ReferenceEquals(writableOldParent, writableTargetParent))
                return "内联脚本不支持跨脚本组移动";

            UpdateScriptEntries(scriptGroup, oldName, newName);
        }

        writableChild.Rename(newName);

        if (needMove && !ReferenceEquals(writableOldParent, writableTargetParent) && child is ChildProjectEntity childEntity)
            MoveChild(writableOldParent, writableTargetParent, childEntity);

        return null;
    }

    /// <summary>
    /// 在可写工程根下按路径查找实体。
    /// 供 FileRename 和 ScriptRename 共用。
    /// </summary>
    internal static ProjectEntity? FindEntityByPath(ProjectEntity root, string path)
    {
        if (path == ".")
            return root;

        string[] segments = path.Split('/');
        ProjectEntity current = root;

        foreach (string segment in segments)
        {
            if (current is not IHasChildren<ProjectEntity> parentWithChildren)
                return null;

            ProjectEntity? next = parentWithChildren.Children
                .FirstOrDefault(c => string.Equals(c.实体_无相对路径标识符, segment, StringComparison.Ordinal));

            if (next == null)
                return null;

            current = next;
        }

        return current;
    }

    /// <summary>
    /// 在两个父级之间移动子实体。
    /// </summary>
    private static void MoveChild(ProjectEntity oldParent, ProjectEntity newParent, ChildProjectEntity child)
    {
        switch (oldParent, newParent, child)
        {
            // Device 移动
            case (WritableMapEntity oldMap, WritableMapEntity newMap, DeviceEntityBase device):
                oldMap.Devices.Remove(device);
                newMap.Devices.Add(device);
                break;
            case (WritableMechEntity oldMech, WritableMechEntity newMech, DeviceEntityBase device):
                oldMech.Devices.Remove(device);
                newMech.Devices.Add(device);
                break;
            case (WritableMapEntity oldMap, WritableMechEntity newMech, DeviceEntityBase device):
                oldMap.Devices.Remove(device);
                newMech.Devices.Add(device);
                break;
            case (WritableMechEntity oldMech, WritableMapEntity newMap, DeviceEntityBase device):
                oldMech.Devices.Remove(device);
                newMap.Devices.Add(device);
                break;

            // ScriptGroup 移动
            case (WritableDeviceEntity oldDevice, WritableDeviceEntity newDevice, ScriptGroupEntityBase scriptGroup):
                oldDevice.ScriptGroups.Remove(scriptGroup);
                newDevice.ScriptGroups.Add(scriptGroup);
                break;

            // Script 移动（已在前置检查中禁止跨组移动）
            case (WritableScriptGroupEntity oldGroup, WritableScriptGroupEntity newGroup, InlineScriptEntityBase script):
                oldGroup.Scripts.Remove(script);
                newGroup.Scripts.Add(script);
                break;

            // Root/Directory 之间的通用移动（目录、canvas、代码仓库脚本、资源等）
            default:
                List<ProjectEntity> oldChildren = RequireWritableChildren(oldParent);
                List<ProjectEntity> newChildren = RequireWritableChildren(newParent);
                oldChildren.Remove(child);
                newChildren.Add(child);
                break;
        }
        child.Parent = newParent;
    }

    private static List<ProjectEntity> RequireWritableChildren(ProjectEntity entity) => entity switch
    {
        WritableProjectRootEntity root => root.Children,
        WritableDirectoryEntity dir => dir.Children,
        _ => throw new InvalidOperationException($"不支持的父级类型: {entity.GetType().Name}")
    };

    /// <summary>
    /// 更新脚本组中匹配的条目名称。
    /// guid 为 null 时按名称匹配，否则按 guid+名称 匹配。
    /// </summary>
    internal static void UpdateScriptEntries(WritableScriptGroupEntity scriptGroup, string oldName, string newName, string? guid = null)
    {
        for (int i = 0; i < scriptGroup.ScriptEntries.Count; i++)
        {
            ScriptEntryInfo entry = scriptGroup.ScriptEntries[i];
            bool matches;
            if (guid != null)
                matches = string.Equals(entry.Guid, guid, StringComparison.Ordinal) && string.Equals(entry.Name, oldName, StringComparison.Ordinal);
            else
                matches = entry.Name == oldName;
            if (matches)
                scriptGroup.ScriptEntries[i] = entry with { Name = newName };
        }
    }

    /// <summary>
    /// 递归遍历可写树，更新所有 ScriptGroup 中匹配的条目名称。
    /// </summary>
    internal static void UpdateScriptEntriesRecursive(ProjectEntity entity, string guid, string oldName, string newName)
    {
        if (entity is WritableScriptGroupEntity sg)
            UpdateScriptEntries(sg, oldName, newName, guid);
        if (entity is IHasChildren<ProjectEntity> hasChildren)
        {
            foreach (ProjectEntity child in hasChildren.Children)
                UpdateScriptEntriesRecursive(child, guid, oldName, newName);
        }
    }
}
