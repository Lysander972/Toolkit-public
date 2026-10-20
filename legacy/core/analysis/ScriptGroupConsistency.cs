using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Analysis;

/// <summary>
/// 脚本组清单(list.json)↔实体(.code/代码仓库) 一致性扫描。
/// 双向判据：
/// - 幽灵条目：清单声明了脚本但无对应实体（内联缺同名 .code，或 CodeRepo GUID 找不到）。
/// - 孤儿文件：脚本组目录内存在未在清单登记的 .code 文件（不进入实体树，由持久化硬删除清理）。
/// Lint 报告与 ScriptGroupReconcile 交集强刷共用同一判据，禁止两处各写一套。
/// </summary>
internal static class ScriptGroupConsistency
{
    /// <summary>
    /// 找出清单中无对应实体的幽灵条目。
    /// </summary>
    internal static List<ScriptEntryInfo> FindGhostEntries(ScriptGroupEntityBase group, ProjectModel model)
    {
        HashSet<string> inlineScriptNames = new(
            group.Scripts.Select(static s => s.ScriptName),
            StringComparer.Ordinal);

        List<ScriptEntryInfo> ghosts = [];
        foreach (ScriptEntryInfo entry in group.ScriptEntries)
        {
            bool hasEntity = entry.IsCodeRepo
                ? GuidText.Validate(entry.Guid) && model.FindByGuid<CodeRepoScriptEntity>(entry.Guid) is not null
                : inlineScriptNames.Contains(entry.Name);
            if (!hasEntity)
                ghosts.Add(entry);
        }
        return ghosts;
    }

    /// <summary>
    /// 找出脚本组目录内未在清单登记的孤儿 .code 文件名（仅文件名，不含路径）。
    /// 数据源是文件树快照（与刷新同代，零 IO）；文件树未绑定（内存构造实体树）或目录不存在时返回空。
    /// </summary>
    internal static List<string> FindOrphanCodeFiles(ScriptGroupEntityBase group)
    {
        if (!FileTreeBinding.Instance.IsBound)
            return [];
        FileTreeNode? node = FileTreeBinding.Instance.FindNode(group.脚本组目录_完整相对路径);
        if (node is null)
            return [];

        HashSet<string> registeredCodeFiles = new(StringComparer.Ordinal);
        foreach (ScriptEntryInfo entry in group.ScriptEntries)
        {
            if (!entry.IsCodeRepo)
                registeredCodeFiles.Add(entry.Name + ProjectEntityLayout.CodeExt);
        }

        List<string> orphans = [];
        foreach (string fileName in node.Files)
        {
            if (fileName.EndsWith(ProjectEntityLayout.CodeExt, StringComparison.Ordinal)
                && !registeredCodeFiles.Contains(fileName))
            {
                orphans.Add(fileName);
            }
        }
        return orphans;
    }
}
