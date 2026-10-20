using Toolkit.Core.Analysis;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Lint;

public static partial class LintEngine
{
    /// <summary>
    /// 检测脚本组清单(list.json)与实体(.code / 代码仓库)的一致性（双向）。
    /// 幽灵条目：清单声明了脚本但无对应实体；孤儿文件：目录内 .code 未在清单登记。
    /// 判据与 ScriptGroupReconcile 交集强刷共用（ScriptGroupConsistency），
    /// Host 检测到本规则后触发保护性强刷（裁掉幽灵条目 + 硬删除孤儿文件）。
    /// </summary>
    private static IReadOnlyList<LintDiagnostic> ValidateScriptGroupConsistency(DeviceEntityBase device, ProjectModel model)
    {
        List<LintDiagnostic> diagnostics = [];

        foreach (ScriptGroupEntityBase group in device.ScriptGroups)
        {
            LintLocation location = new(group.listJson_完整相对路径, 1);

            foreach (ScriptEntryInfo entry in ScriptGroupConsistency.FindGhostEntries(group, model))
            {
                string kind = entry.IsCodeRepo ? "代码仓库脚本" : "脚本文件";
                diagnostics.Add(Diag(
                    LintRuleIds.StructScriptOrphan,
                    LintSeverity.Warning,
                    $"脚本清单与实体不一致：'{entry.Name}' 在脚本组中无对应{kind}（越界直写污染），将被交集裁剪",
                    location));
            }

            foreach (string fileName in ScriptGroupConsistency.FindOrphanCodeFiles(group))
            {
                diagnostics.Add(Diag(
                    LintRuleIds.StructScriptOrphan,
                    LintSeverity.Warning,
                    $"脚本清单与实体不一致：'{fileName}' 存在于脚本组目录但未在清单登记（越界直写污染），将被硬删除",
                    location));
            }
        }

        return diagnostics;
    }
}
