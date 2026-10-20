using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.Sdk;
using Toolkit.Core.TsView;

namespace Toolkit.Core.Lint;

public static partial class LintEngine
{
    /// <summary>
    /// 校验事件脚本的事件类型是否允许在该零件类型上使用
    /// </summary>
    public static IReadOnlyList<LintDiagnostic> ValidateEventDeviceMatch(DeviceEntity device)
    {
        List<LintDiagnostic> diagnostics = [];

        int deviceTypeId = device.DeviceType;
        EventSignatureRegistry registry = EventSignatureRegistry.Instance;

        foreach (string eventName in device.EventScripts.Keys)
        {
            IReadOnlyList<EventSignature> signatures = registry.GetByName(eventName);
            if (signatures.Count == 0)
            {
                diagnostics.Add(CreateLintEngineErrorDiagnostic(
                    $"事件签名未注册，无法校验零件类型: {eventName}",
                    new LintLocation(
                        device.device目录_完整相对路径,
                        1)));
                continue;
            }

            if (!registry.IsEventAllowedForDeviceType(eventName, deviceTypeId))
            {
                string typeName = DeviceTypeIds.GetActionOwnerTypeName(deviceTypeId);
                diagnostics.Add(DiagError(
                    LintRuleIds.EventDeviceMismatch,
                    $"事件'{eventName}'不允许在零件类型'{typeName}'(type={deviceTypeId}) 上使用",
                    new LintLocation(device.device目录_完整相对路径, 1)));
            }
        }

        return diagnostics;
    }

    /// <summary>
    /// 规则1：自定义脚本名与该零件类型允许的事件名完全重名 → Error
    /// </summary>
    private static IEnumerable<LintDiagnostic> ValidateCustomScriptNameClashEvent(
        DeviceEntityBase device,
        BescriptParseData parseData,
        InlineScriptEntityBase script)
    {
        // 仅自定义脚本（非事件脚本）参与此检查
        if (!string.IsNullOrEmpty(script.EventName))
            yield break;

        string methodName = parseData.Name;
        EventSignatureRegistry registry = EventSignatureRegistry.Instance;
        if (registry.IsEventAllowedForDeviceType(methodName, device.DeviceType))
        {
            yield return CreateEventNameClashDiagnostic(methodName, script.实体_完整相对路径标识);
        }
    }

    /// <summary>
    /// 规则2：机械产生器(1110) 同名事件脚本组内脚本名与目标机械核心(CoreDevice) 重叠 → Warning
    ///
    /// 注入语义：产生器存储事件名通过 <see cref="BirthBuildingEventMap.ToCoreName"/>
    /// 映射为目标核心事件名（mechUpdate → update 等），注入到目标机械核心的同名事件脚本组中。
    /// 当两边在同名事件脚本组中存在同名脚本时（产生器侧 a.code 与核心侧 a.code 同时落入
    /// update 组），注入过程会产生覆盖冲突 —— 这才是真正有意义的告警条件。
    /// 仅"事件名重叠"是底层硬编码的常态，不报警。
    /// </summary>
    public static IReadOnlyList<LintDiagnostic> ValidateBirthBuildingEventClashCore(
        DeviceEntityBase device, ProjectModel model)
    {
        List<LintDiagnostic> diagnostics = [];

        if (device.DeviceType != DeviceTypeIds.BirthBuilding)
            return diagnostics;

        string? mechGuid = device.MechFileGuid;
        if (!GuidText.Validate(mechGuid))
            return diagnostics;

        if (model.FindByGuid<MechEntityBase>(mechGuid) is not MechEntityBase mech)
            return diagnostics;

        // 收集目标机械上所有 CoreDevice 的"核心事件名 → 脚本名集合"
        Dictionary<string, HashSet<string>> coreEventScripts = new(StringComparer.Ordinal);
        foreach (DeviceEntityBase core in mech.Devices)
        {
            if (core.DeviceType != DeviceTypeIds.CoreDevice)
                continue;
            foreach (KeyValuePair<string, IEnumerable<InlineScriptEntityBase>> kv in core.EventScripts)
            {
                if (!coreEventScripts.TryGetValue(kv.Key, out HashSet<string>? set))
                {
                    set = new(StringComparer.Ordinal);
                    coreEventScripts[kv.Key] = set;
                }
                foreach (InlineScriptEntityBase s in kv.Value)
                    set.Add(s.ScriptName);
            }
        }

        if (coreEventScripts.Count == 0)
            return diagnostics;

        foreach (KeyValuePair<string, IEnumerable<InlineScriptEntityBase>> birthKv in device.EventScripts)
        {
            if (!BirthBuildingEventMap.ToCoreName.TryGetValue(birthKv.Key, out string? coreEventName))
                continue;

            if (!coreEventScripts.TryGetValue(coreEventName, out HashSet<string>? coreNames))
                continue;

            foreach (InlineScriptEntityBase s in birthKv.Value)
            {
                if (coreNames.Contains(s.ScriptName))
                {
                    diagnostics.Add(CreateBirthBuildingEventClashCoreDiagnostic(
                        birthKv.Key,
                        coreEventName,
                        s.ScriptName,
                        device.device目录_完整相对路径));
                }
            }
        }

        return diagnostics;
    }
}
