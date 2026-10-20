using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.Refactor;

/// <summary>
/// 重构工具辅助方法。
/// 注意：本类不再包含文件 I/O 操作，所有文件操作应通过实体类（ProjectEntity 子类）完成。
/// </summary>
internal static class RefactorHelper
{
    /// <summary>校验脚本名称（禁止扩展名、禁止空格、禁止首尾空白）</summary>
    public static void ValidateScriptName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("脚本名称不能为空", nameof(name));
        }

        // 禁止首尾空白
        if (char.IsWhiteSpace(name[0]) || char.IsWhiteSpace(name[^1]))
        {
            throw new ArgumentException($"脚本名称禁止包含首尾空白: '{name}'", nameof(name));
        }

        // 禁止空格：空格会导致名称匹配混乱
        if (name.Contains(' '))
        {
            throw new ArgumentException($"脚本名称禁止包含空格: '{name}'", nameof(name));
        }

        // 禁止 .code 扩展名
        if (name.EndsWith(ProjectEntityLayout.CodeExt, StringComparison.Ordinal))
        {
            throw new ArgumentException($"脚本名称不应包含扩展名 '{ProjectEntityLayout.CodeExt}'", nameof(name));
        }
    }

    /// <summary>判断目录是否是零件脚本目录（scripts 或 event_xxx）</summary>
    public static bool IsDeviceScriptDir(string dir)
    {
        string name = RelativePathUtils.GetLastSegment(dir);
        return name.Equals(ProjectEntityLayout.ScriptsDirName, StringComparison.Ordinal)
            || name.StartsWith(ProjectEntityLayout.EventDirPrefix, StringComparison.Ordinal);
    }

    /// <summary>解析零件脚本目录路径</summary>
    /// <param name="deviceDir">零件目录</param>
    /// <param name="scriptGroup">脚本组名（scripts 或 event_XXX）</param>
    public static string ResolveDeviceScriptDir(string deviceDir, string scriptGroup)
    {
        string devicePath = WorkspacePath.RequireRelativePath(deviceDir);
        return RelativePathUtils.Combine(devicePath, scriptGroup);
    }

    /// <summary>
    /// 根据 act 调用的 OwnerName/OwnerNameIsAlias，在调用方所在作用域内解析目标零件路径。
    /// this → 调用方自身（1110 除外，1110 的 this 指向目标机械核心）
    /// 纯数字 → 同作用域内按 DeviceId 查找
    /// 非数字 → 同作用域内按 LogicName 查找
    /// </summary>
    public static string? ResolveActTargetDevicePath(
        string? ownerName, bool ownerNameIsAlias,
        DeviceEntity caller, ProjectModel model)
    {
        if (string.IsNullOrWhiteSpace(ownerName)) return null;

        if (!ownerNameIsAlias && ownerName == "this")
        {
            if (caller.DeviceType == DeviceTypeIds.BirthBuilding)
            {
                string? mechGuid = caller.MechFileGuid;
                if (GuidText.Validate(mechGuid)
                    && model.FindByGuid<MechEntityBase>(mechGuid) is MechEntityBase birthMech)
                {
                    DeviceEntity? core = birthMech.Devices.OfType<DeviceEntity>()
                        .FirstOrDefault(static d => d.DeviceType == DeviceTypeIds.CoreDevice);
                    if (core != null) return core.device目录_完整相对路径;
                }
            }
            return caller.device目录_完整相对路径;
        }

        IEnumerable<DeviceEntity> siblings = caller.Parent switch
        {
            MapEntityBase map => map.Devices.OfType<DeviceEntity>(),
            MechEntityBase mech => mech.Devices.OfType<DeviceEntity>(),
            _ => Enumerable.Empty<DeviceEntity>()
        };

        string lookupName = ownerNameIsAlias && ownerName.StartsWith("/", StringComparison.Ordinal)
            ? ownerName.Substring(1)
            : ownerName;

        if (!ownerNameIsAlias && int.TryParse(lookupName, out int targetId))
        {
            foreach (DeviceEntity sibling in siblings)
                if (sibling.DeviceId == targetId)
                    return sibling.device目录_完整相对路径;
        }
        else
        {
            foreach (DeviceEntity sibling in siblings)
                if (string.Equals(sibling.LogicName, lookupName, StringComparison.Ordinal))
                    return sibling.device目录_完整相对路径;
        }

        return null;
    }

}
