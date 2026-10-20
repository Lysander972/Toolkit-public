using Toolkit.Core.Persistence;

namespace Toolkit.Core.Sdk;

/// <summary>
/// 零件能力解析器。按零件类型、所属载体与骨骼绑定判断能力，
/// 是 Lint 与 TS 视图共用的唯一入口。
/// </summary>
public static class DeviceCapabilityResolver
{
    /// <summary>
    /// 不具备碰撞能力的零件类型。
    /// </summary>
    private static readonly HashSet<int> AlwaysNoRigidbodyTypes =
    [
        DeviceTypeIds.BirthBuilding,
        DeviceTypeIds.AreaBuilding,
        DeviceTypeIds.StartBuilding,
        DeviceTypeIds.ClientMapLogicDevice,
        DeviceTypeIds.MapCoreDevice,
        DeviceTypeIds.SLGLogicDevice,
    ];

    /// <summary>
    /// 在地图内无碰撞能力；在普通机械内由骨骼绑定状态决定。
    /// </summary>
    private static readonly HashSet<int> CarrierAndBoneConditionalTypes =
    [
        DeviceTypeIds.PosDevice,
        DeviceTypeIds.TriggerBuilding,
    ];

    /// <summary>
    /// 在地图内无碰撞能力；在普通机械内具有碰撞能力，与骨骼绑定无关。
    /// </summary>
    private static readonly HashSet<int> CarrierConditionalTypes =
    [
        DeviceTypeIds.TextBuilding,
        DeviceTypeIds.NoColliderBuilding_1030,
        DeviceTypeIds.NoColliderBuilding_1032,
        DeviceTypeIds.NoColliderBuilding_1058,
        DeviceTypeIds.NoColliderBuilding_1061,
    ];

    /// <summary>
    /// 判定零件实例是否具备指定能力。
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// 出现未实现的 DeviceCapability。新增能力时必须同步实现判定逻辑，禁止静默放行。
    /// </exception>
    public static bool HasCapability(DeviceEntityBase device, DeviceCapability capability)
    {
        if (capability != DeviceCapability.Collidable)
            throw new NotSupportedException(
                $"DeviceCapability.{capability} 尚未实现判定逻辑；新增能力时必须在此处补充对应的能力规则。");

        int deviceType = device.DeviceType;

        if (AlwaysNoRigidbodyTypes.Contains(deviceType))
            return false;

        if (CarrierAndBoneConditionalTypes.Contains(deviceType))
        {
            if (device.CarrierKind == "Map")
                return false;
            return !HasStickToBone(device);
        }

        if (CarrierConditionalTypes.Contains(deviceType))
        {
            if (device.CarrierKind == "Map")
                return false;
            return true;
        }

        return !HasStickToBone(device);
    }

    /// <summary>
    /// 按设备类型 ID 判定（无实例字段时使用，仅做类型级永远规则）。
    /// 不覆盖骨骼绑定与所属载体这类实例条件，调用方需要明确语义。
    /// </summary>
    /// <exception cref="NotSupportedException">同 <see cref="HasCapability"/>.</exception>
    public static bool IsAlwaysDisallowedForType(int deviceTypeId, DeviceCapability capability)
    {
        if (capability != DeviceCapability.Collidable)
            throw new NotSupportedException(
                $"DeviceCapability.{capability} 尚未实现判定逻辑；新增能力时必须在此处补充对应的能力规则。");

        return AlwaysNoRigidbodyTypes.Contains(deviceTypeId);
    }

    private static bool HasStickToBone(DeviceEntityBase device)
    {
        string? stickToBone = device.StickToBone;
        return stickToBone is { Length: > 0 };
    }
}
