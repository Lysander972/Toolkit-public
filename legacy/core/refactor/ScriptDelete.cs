using Toolkit.Core.Analysis;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Refactor;

public sealed record ScriptDeleteData(
    string DeviceRelativePath,
    string ScriptName,
    bool WasLinked,
    IReadOnlyList<string> AffectedRelativePaths,
    bool Applied);

/// <summary>
/// 脚本删除：从零件脚本组中移除指定脚本。
/// 内联脚本：连同 .code 文件一起删除。
/// 链接脚本：仅断开 list.json 条目。
/// </summary>
public sealed class ScriptDelete
{
    private readonly ProjectModel _model;
    private readonly ProjectLogicContext _logic;

    public ScriptDelete(ProjectModel model, ProjectLogicContext logic)
    {
        _model = model;
        _logic = logic;
    }

    public (ScriptDeleteData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Delete(
        string deviceRelativePath,
        string scriptName,
        string scriptGroup,
        bool force = false)
    {
        (string resolvedPath, string? pathError) = WorkspacePath.TryRequireRelativePath(deviceRelativePath);
        if (pathError != null)
            return Fail(pathError);
        deviceRelativePath = resolvedPath;

        string deviceRelPath = WorkspacePath.RequireRelativePath(deviceRelativePath);
        string scriptDir = RefactorHelper.ResolveDeviceScriptDir(deviceRelPath, scriptGroup);

        if (_model.Find<DeviceEntity>(deviceRelPath) == null)
            return Fail($"未找到零件实体: {deviceRelPath}");

        if (_model.Find<ScriptGroupEntity>(scriptDir) is not ScriptGroupEntity scriptGroupEntity)
            return Fail($"未找到脚本组: {scriptDir}. scriptGroup 必须是完整脚本组名（scripts 或 event_XXX）");

        if (string.IsNullOrWhiteSpace(scriptGroupEntity.ListJsonText))
            return Fail($"脚本组 list.json 为空: {scriptDir}");

        // 解析 list.json
        List<ListJsonItem> listItems = HjsonText.ParseListItems(scriptGroupEntity.ListJsonText);

        // 查找目标条目
        ListJsonItem? targetItem = null;
        foreach (ListJsonItem item in listItems)
        {
            if (string.Equals(item.Name, scriptName, StringComparison.Ordinal))
            {
                targetItem = item;
                break;
            }
        }

        if (targetItem == null)
            return Fail($"脚本 '{scriptName}' 不存在于 {scriptDir}");

        bool isLinked = targetItem.IsCodeRepo;

        // 引用完整性检查（非 force 时）
        if (!force)
        {
            int refCount = 0;
            foreach (SymbolReference reference in _logic.FindSymbolReferences(scriptName))
            {
                if (reference.CallKind != BescriptCallKind.Act)
                    continue;

                string refPath = WorkspacePath.RequireRelativePath(reference.ScriptRelativePath);
                // 排除目标脚本自身
                string targetScriptPath = targetItem.IsCodeRepo
                    ? targetItem.Guid
                    : RelativePathUtils.Combine(scriptDir, scriptName + ProjectEntityLayout.CodeExt);
                if (string.Equals(refPath, targetScriptPath, StringComparison.Ordinal))
                    continue;

                // 排除同零件内的引用
                string refDeviceDir = RelativePathUtils.GetParentDirectory(RelativePathUtils.GetParentDirectory(refPath));
                if (string.Equals(refDeviceDir, deviceRelPath, StringComparison.Ordinal))
                    continue;

                refCount++;
            }

            if (refCount > 0)
                return Fail($"脚本 {scriptName} 仍有 {refCount} 处外部引用，不允许删除。设置 force=true 可跳过引用检查");
        }

        List<string> affected =
        [
            RelativePathUtils.Combine(scriptDir, ProjectEntityLayout.ListJsonName)
        ];
        if (!isLinked)
            affected.Add(RelativePathUtils.Combine(scriptDir, scriptName + ProjectEntityLayout.CodeExt));

        // 持久化：修改 list.json + 删除内联脚本实体
        EntityWriteScope.ApplyContentChange<ScriptGroupEntity, WritableScriptGroupEntity>(
            _model,
            scriptGroupEntity,
            group => group.ToWritable(),
            writableGroup =>
            {
                writableGroup.ScriptEntries = writableGroup.ScriptEntries
                    .Where(e => !string.Equals(e.Name, scriptName, StringComparison.Ordinal))
                    .ToList();
                if (!isLinked)
                {
                    writableGroup.Scripts.RemoveAll(s => string.Equals(s.ScriptName, scriptName, StringComparison.Ordinal));
                }
            });

        return (new ScriptDeleteData(deviceRelPath, scriptName, isLinked, affected, true), Array.Empty<ToolError>(), Array.Empty<ToolError>());
    }

    private (ScriptDeleteData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Fail(string message)
    {
        return (null, [new ToolError(null, null, null, message)], Array.Empty<ToolError>());
    }
}
