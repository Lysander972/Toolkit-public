using Toolkit.Core.Analysis;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Refactor;

/// <summary>
/// 脚本组一致性修复（保护性交集强刷）。
/// 双向清理：
/// - 幽灵条目：清单(list.json)中无对应实体的条目被裁掉，list.json 重写；
/// - 孤儿文件：目录内未登记的 .code 由 SaveChanges 的局部管辖硬删除清理
///   （Core_Persistence_Diff_Spec：当前快照 − 期望树 = DeleteFile，即使无内容改动也非 no-op）。
/// 判据与 Lint 规则 LINT-STRUCT-SCRIPT-ORPHAN 共用（ScriptGroupConsistency）。
/// 用于 Lint 检测到越界直写污染后的自愈。
/// </summary>
public static class ScriptGroupReconcile
{
    /// <summary>
    /// 对工程内所有零件做交集裁剪。返回是否发生了任何写盘。
    /// </summary>
    public static bool ReconcileAllDevices(ProjectModel model)
    {
        List<string> devicePaths;
        model.EnterReadLock();
        try
        {
            devicePaths = model.List<DeviceEntity>()
                .Select(static d => d.实体_完整相对路径标识)
                .ToList();
        }
        finally
        {
            model.ExitReadLock();
        }

        bool anyWritten = false;
        foreach (string devicePath in devicePaths)
        {
            if (ReconcileDeviceToIntersection(model, devicePath))
                anyWritten = true;
        }
        return anyWritten;
    }

    /// <summary>
    /// 把指定零件的所有脚本组裁剪到清单∩实体交集，并清理孤儿 .code 文件。
    /// 读锁内完成 Find + ToWritable + 裁剪判定；写锁由 SaveChangesIfVersionMatches 内部获取。
    /// 版本守卫：保存前若模型被并发改动则放弃本轮（返回 false），等下一轮 Lint 重试。
    /// 返回 true 表示发生了写盘（裁剪或孤儿删除生效）。
    /// </summary>
    public static bool ReconcileDeviceToIntersection(ProjectModel model, string deviceRelativePath)
    {
        long version;
        WritableDeviceEntity writable;
        bool changed;

        model.EnterReadLock();
        try
        {
            if (model.Find<DeviceEntity>(deviceRelativePath) is not DeviceEntity readOnlyDevice)
                return false;
            version = model.RefreshVersion;
            writable = readOnlyDevice.ToWritable();
            changed = false;
            foreach (ScriptGroupEntityBase baseGroup in writable.ScriptGroups)
            {
                WritableScriptGroupEntity group = (WritableScriptGroupEntity)baseGroup;

                // 幽灵条目：裁剪清单（ghosts 与 ScriptEntries 同源，值相等必同为幽灵，Except 安全）
                List<ScriptEntryInfo> ghosts = ScriptGroupConsistency.FindGhostEntries(group, model);
                if (ghosts.Count > 0)
                {
                    group.ScriptEntries = group.ScriptEntries.Except(ghosts).ToList();
                    changed = true;
                }

                // 孤儿 .code：不修改实体，仅触发保存让持久化层硬删除（当前快照 − 期望树）
                if (ScriptGroupConsistency.FindOrphanCodeFiles(group).Count > 0)
                    changed = true;
            }
        }
        finally
        {
            model.ExitReadLock();
        }

        if (!changed)
            return false;

        return model.SaveChangesIfVersionMatches(writable, version).VersionMatched;
    }
}
