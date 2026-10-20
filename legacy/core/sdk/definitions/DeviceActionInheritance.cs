namespace Toolkit.Core.Sdk;

// ╔════════════════════════════════════════════════════════════════════════════════╗
// ║                     【禁止审查区域 - 原版 SDK 精确映射】                          ║
// ╠════════════════════════════════════════════════════════════════════════════════╣
// ║  本文件包含对原版 BeScript SDK 零件动作继承关系的精确硬编码映射。                   ║
// ║                                                                                ║
// ║  ⚠️  重要声明：                                                                 ║
// ║  • 此区域内容必须与原版 SDK 保持一条不多、一条不少的完全一致                        ║
// ║  • 无论原版设计是否符合软件工程最佳实践，此处的兼容性映射都是必须的                  ║
// ║  • 请勿对本文件内容进行任何形式的代码审查、重构建议或规范性检查                     ║
// ║  • 任何看似"不规范"的命名、参数或结构都是对原版的忠实还原                          ║
// ║                                                                                ║
// ║  本文件由 Toolkit 维护，用于为插件和核心库提供原版 SDK 兼容服务。                   ║
// ╚════════════════════════════════════════════════════════════════════════════════╝

internal static class DeviceActionInheritance
{
    private static readonly Dictionary<string, string> BaseOwnerByDerived = new(StringComparer.Ordinal)
    {
        { "AntiGravityDevice", "Device" },
        { "AreaBuilding", "FuncDevice" },
        { "ArrowBuilding", "NoColliderBuilding" },
        { "AvatarDevice", "FuncDevice" },
        { "BaseCoreDevice", "FuncDevice" },
        { "BeamDevice", "VFXDevice" },
        { "BillboardDevice", "FuncDevice" },
        { "BirthBuilding", "MechGeneraterBase" },
        { "BlackholeDevice", "VFXDevice" },
        { "BombDevice", "Device" },
        { "BulletDevice", "Device" },
        { "CableDevice", "FuncDevice" },
        { "Capsule2DDevice", "SpriteDevice" },
        { "CameraDevice", "FuncDevice" },
        { "CannonGun", "RayGun" },
        { "CapsuleDevice", "SimpleDevice" },
        { "CircleDevice", "SpriteDevice" },
        { "ClientMapLogicDevice", "FuncDevice" },
        { "ClipDevice", "Device" },
        { "ClipGun", "GunDevice" },
        { "ColorDevice", "RepeatDevice" },
        { "CoreDevice", "BaseCoreDevice" },
        { "CubeDevice", "SimpleDevice" },
        { "DecalDevice", "FuncDevice" },
        { "Device2D", "FuncDevice" },
        { "ElectricArcDevice", "VFXDevice" },
        { "ElectricBallDevice", "VFXDevice" },
        { "EnergyBuilding", "Device" },
        { "EnergyShieldDevice", "VFXDevice" },
        { "FakeInteriorDevice", "FuncDevice" },
        { "FallingLeavesDevice", "VFXDevice" },
        { "FireDevice", "SwitchDevice" },
        { "FlameGun", "ClipGun" },
        { "FlareDevice", "FuncDevice" },
        { "ForceFieldDevice", "VFXDevice" },
        { "FuncDevice", "Device" },
        { "GalaxyDevice", "VFXDevice" },
        { "GasPlanetDevice", "PlanetBaseDevice" },
        { "GatlinGun", "RayGun" },
        { "GunDevice", "Device" },
        { "HeightFuncDevice", "Device" },
        { "IndicatorDevice", "FuncDevice" },
        { "IvyDevice", "FuncDevice" },
        { "JointDevice", "FuncDevice" },
        { "LaserDevice", "SwitchDevice" },
        { "LaserMelee", "SwitchDevice" },
        { "LightDevice", "SwitchDevice" },
        { "MagnetDevice", "FuncDevice" },
        { "MapCoreDevice", "BaseCoreDevice" },
        { "MechGeneraterBase", "FuncDevice" },
        { "MechShooter", "MechGeneraterBase" },
        { "MechSlot", "FuncDevice" },
        { "MeleeDevice", "Device" },
        { "MiscFiresDevice", "FuncDevice" },
        { "MiscVFXsDevice", "FuncDevice" },
        { "MotorWheel", "JointDevice" },
        { "MoveDevice", "FuncDevice" },
        { "MoveDevice2", "FuncDevice" },
        { "MuzzleDevice", "FuncDevice" },
        { "NebulaDevice", "VFXDevice" },
        { "NoColliderBuilding", "Device" },
        { "ObjDevice", "FuncDevice" },
        { "ParticleDevice", "FuncDevice" },
        { "PrefabDevice", "FuncDevice" },
        { "PistonDeviceLine", "JointDevice" },
        { "PistonJoint", "JointDevice" },
        { "PlanetBaseDevice", "VFXDevice" },
        { "PlanetDevice", "PlanetBaseDevice" },
        { "PosDevice", "FuncDevice" },
        { "PolygonDevice", "SpriteDevice" },
        { "PropellerDevice", "Device" },
        { "RayGun", "ClipGun" },
        { "RecorderDevice", "Device" },
        { "RectDevice", "SpriteDevice" },
        { "RepeatDevice", "Device" },
        { "Roter2Device", "JointDevice" },
        { "RoterDevice", "JointDevice" },
        { "Rotor3Device", "RoterDevice" },
        { "RPGGun", "ClipGun" },
        { "ScanDevice", "VFXDevice" },
        { "SelectDevice", "Device" },
        { "ShieldDevice", "Device" },
        { "SimpleDevice", "FuncDevice" },
        { "SlashDevice", "FuncDevice" },
        { "SLGLogicDevice", "FuncDevice" },
        { "SmokeDevice", "SwitchDevice" },
        { "SphereDevice", "SimpleDevice" },
        { "SpineDevice", "Device2D" },
        { "SpringDevice", "JointDevice" },
        { "StartBuilding", "Device" },
        { "SunDevice", "PlanetBaseDevice" },
        { "SupportDevice", "Device" },
        { "SpriteDevice", "Device2D" },
        { "SwitchDevice", "FuncDevice" },
        { "TextBuilding", "FuncDevice" },
        { "TornadoDevice", "VFXDevice" },
        { "TrafficLightDevice", "FuncDevice" },
        { "TrailDevice", "SwitchDevice" },
        { "TriggerBuilding", "FuncDevice" },
        { "VelocityFuncDevice", "Device" },
        { "VertexDevice", "Device" },
        { "VFXDevice", "FuncDevice" },
        { "VolumetricLightDevice", "FuncDevice" },
        { "WarpDevice", "VFXDevice" },
        { "WheelColliderDevice", "FuncDevice" }
    };

    public static string? GetBaseOwnerType(string ownerType)
    {
        if (BaseOwnerByDerived.ContainsKey(ownerType))
            return BaseOwnerByDerived[ownerType];
        return null;
    }

    /// <summary>
    /// 获取完整继承链（从当前类型到 Device，含 Device）。
    /// </summary>
    /// <param name="ownerType">起始类型</param>
    /// <param name="includeDevice">是否包含 Device（默认 true）</param>
    /// <returns>继承链列表，包含起始类型</returns>
    public static IReadOnlyList<string> GetInheritanceChain(string ownerType, bool includeDevice = true)
    {
        List<string> chain = [];

        string? current = ownerType;
        while (current is not null)
        {
            if (!includeDevice && string.Equals(current, "Device", StringComparison.Ordinal))
            {
                break;
            }

            chain.Add(current);
            if (string.Equals(current, "Device", StringComparison.Ordinal))
            {
                break;
            }
            current = GetBaseOwnerType(current);
        }

        return chain;
    }

    /// <summary>
    /// 沿继承链查找：对每个类型执行查找函数，返回第一个成功的结果。
    /// </summary>
    /// <param name="ownerType">起始类型</param>
    /// <param name="tryFind">查找函数，参数为当前类型，返回 (是否找到, 结果)</param>
    /// <param name="startFromParent">是否从父类开始</param>
    /// <returns>找到的结果，未找到返回 default</returns>
    public static TResult? FindInChain<TResult>(
        string ownerType,
        Func<string, (bool Found, TResult? Value)> tryFind,
        bool startFromParent = false)
    {
        IReadOnlyList<string> chain = GetInheritanceChain(ownerType, includeDevice: true);
        int startIndex = startFromParent ? 1 : 0;
        for (int i = startIndex; i < chain.Count; i++)
        {
            (bool found, TResult? value) = tryFind(chain[i]);
            if (found)
            {
                return value;
            }
        }
        return default;
    }

    /// <summary>
    /// 沿继承链检查：对每个类型执行检查函数，返回是否有任一成功。
    /// </summary>
    public static bool AnyInChain(
        string ownerType,
        Func<string, bool> predicate,
        bool startFromParent = false)
    {
        IReadOnlyList<string> chain = GetInheritanceChain(ownerType, includeDevice: true);
        int startIndex = startFromParent ? 1 : 0;
        for (int i = startIndex; i < chain.Count; i++)
        {
            if (predicate(chain[i]))
            {
                return true;
            }
        }
        return false;
    }

}
