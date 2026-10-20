namespace Toolkit.Core.Sdk;

/// <summary>
/// 事件签名选择规则：按零件类型映射 + 继承链统一处理所有事件选择。
/// </summary>
internal static class EventSignatureRules
{
    /// <summary>
    /// 按零件 type ID 解析事件归属类型（与原版一致的压缩映射）。
    /// </summary>
    public static string ResolveEventOwnerType(int deviceTypeId)
    {
        return deviceTypeId switch
        {
            DeviceTypeIds.MapCoreDevice => "MapCoreDevice",
            DeviceTypeIds.CoreDevice => "CoreDevice",
            // BirthBuilding 有独立的生成机械事件集
            DeviceTypeIds.BirthBuilding => "BirthBuilding",
            DeviceTypeIds.LaserDeviceRed or DeviceTypeIds.LaserDeviceBlue => "LaserDevice",
            DeviceTypeIds.TriggerBuilding => "TriggerBuilding",
            DeviceTypeIds.ClientMapLogicDevice => "ClientMapLogicDevice",
            DeviceTypeIds.SLGLogicDevice => "SLGLogicDevice",
            _ => DeviceTypeIds.GetActionOwnerTypeName(deviceTypeId)
        };
    }

    /// <summary>
    /// 构建事件归属类型的继承链（沿用 Action 继承），仅用于同名事件签名回溯。
    /// </summary>
    public static IReadOnlyList<string> BuildEventOwnerTypeChain(string eventOwnerType)
    {
        // 事件签名回溯需要 FuncDevice 而不是 Device；允许集合已由 IsAllowedForDeviceType 判定。
        IReadOnlyList<string> chain = DeviceActionInheritance.GetInheritanceChain(eventOwnerType, includeDevice: false);

        // 确保 FuncDevice 在链中，用于查找父级签名，不用于扩大允许范围。
        if (!chain.Contains("FuncDevice", StringComparer.Ordinal))
        {
            List<string> list = chain.ToList();
            list.Add("FuncDevice");
            return list;
        }

        return chain;
    }

    /// <summary>
    /// 根据零件 type ID 选择允许的事件签名（按继承链逐级回溯）。
    /// TS 视图生成与写回的允许事件集合必须收敛到此入口。
    /// </summary>
    public static IReadOnlyDictionary<string, EventSignature> SelectAllowedEventSignatures(
        int deviceTypeId,
        EventSignatureRegistry registry)
    {
        EventSignature[] allowed = registry.GetAll()
            .Where(sig => sig.IsAllowedForDeviceType(deviceTypeId))
            .ToArray();
        if (allowed.Length == 0)
        {
            return Array.Empty<EventSignature>()
                .ToDictionary(sig => sig.EventName, StringComparer.Ordinal);
        }

        string ownerType = ResolveEventOwnerType(deviceTypeId);
        IReadOnlyList<string> chain = BuildEventOwnerTypeChain(ownerType);
        Dictionary<string, EventSignature> result = new(StringComparer.Ordinal);
        foreach (IGrouping<string, EventSignature> group in allowed.GroupBy(sig => sig.EventName, StringComparer.Ordinal))
        {
            EventSignature? chosen = chain
                .Select(owner => group.FirstOrDefault(sig => sig.DeviceType.Equals(owner, StringComparison.Ordinal)))
                .FirstOrDefault(sig => sig is not null);
            if (chosen is null)
            {
                throw new InvalidOperationException(
                    $"事件 {group.Key} 没有匹配零件类型 {ownerType} 的签名定义（继承链: {string.Join(" -> ", chain)}）");
            }
            result[group.Key] = chosen;
        }

        return result;
    }
}
