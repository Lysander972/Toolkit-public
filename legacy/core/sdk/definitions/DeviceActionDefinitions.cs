using static Toolkit.Core.Sdk.BuiltinEnumTypes;

#pragma warning disable TK006 // 禁止审查区域：原版 SDK 精确映射，组织性拆分非碎片 helper

namespace Toolkit.Core.Sdk;

// ╔════════════════════════════════════════════════════════════════════════════════╗
// ║                     【禁止审查区域 - 原版 SDK 精确映射】                          ║
// ╠════════════════════════════════════════════════════════════════════════════════╣
// ║  本文件包含对原版 BeScript SDK 零件动作的精确硬编码映射。                          ║
// ║                                                                                ║
// ║  ⚠️  重要声明：                                                                 ║
// ║  • 此区域内容必须与原版 SDK 保持一条不多、一条不少的完全一致                        ║
// ║  • 无论原版设计是否符合软件工程最佳实践，此处的兼容性映射都是必须的                  ║
// ║  • 请勿对本文件内容进行任何形式的代码审查、重构建议或规范性检查                     ║
// ║  • 任何看似"不规范"的命名、参数或结构都是对原版的忠实还原                          ║
// ║                                                                                ║
// ║  本文件由 Toolkit 维护，用于为插件和核心库提供原版 SDK 兼容服务。                   ║
// ╚════════════════════════════════════════════════════════════════════════════════╝

/// <summary>
/// 零件动作 (act) 定义
/// </summary>
public static class DeviceActionDefinitions
{
    private const string ResourceFileNameNote = "文件参数只填包含扩展名的文件本身名称，不填 GUID 或带路径的名称。";

    public static IEnumerable<MethodSignature> GetAll()
    {
        foreach (MethodSignature sig in GetDeviceBaseActions()) yield return sig;
        foreach (MethodSignature sig in GetCoreDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetCubeDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetCapsuleDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetDevice2DActions()) yield return sig;
        foreach (MethodSignature sig in GetObjDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetPrefabDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetParticleDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetLightDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetCameraDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetJointDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetRoterDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetSpringDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetTriggerBuildingActions()) yield return sig;
        foreach (MethodSignature sig in GetTextBuildingActions()) yield return sig;
        foreach (MethodSignature sig in GetBombDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetPropellerDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetFuncDeviceActions()) yield return sig;
        // 新增零件类型
        foreach (MethodSignature sig in GetWheelColliderDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetGunDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetLaserDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetMagnetDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetMechShooterActions()) yield return sig;
        foreach (MethodSignature sig in GetMechSlotActions()) yield return sig;
        foreach (MethodSignature sig in GetMotorWheelActions()) yield return sig;
        foreach (MethodSignature sig in GetPistonJointActions()) yield return sig;
        foreach (MethodSignature sig in GetRoter2DeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetSwitchDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetTrailDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetSmokeDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetFlareDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetFireDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetDecalDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetAreaBuildingActions()) yield return sig;
        foreach (MethodSignature sig in GetBirthBuildingActions()) yield return sig;
        foreach (MethodSignature sig in GetAntiGravityDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetMoveDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetRecorderDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetTrafficLightDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetArrowBuildingActions()) yield return sig;
        foreach (MethodSignature sig in GetCannonGunActions()) yield return sig;
        foreach (MethodSignature sig in GetClipGunActions()) yield return sig;
        foreach (MethodSignature sig in GetGatlinGunActions()) yield return sig;
        foreach (MethodSignature sig in GetLaserMeleeActions()) yield return sig;
        foreach (MethodSignature sig in GetMoveDevice2Actions()) yield return sig;
        foreach (MethodSignature sig in GetPistonDeviceLineActions()) yield return sig;
        foreach (MethodSignature sig in GetSpineDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetClientMapLogicDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetSLGLogicDeviceActions()) yield return sig;
        // VFX 零件类型
        foreach (MethodSignature sig in GetBeamDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetBillboardDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetBlackholeDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetElectricArcDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetElectricBallDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetEnergyShieldDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetFallingLeavesDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetForceFieldDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetGalaxyDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetGasPlanetDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetIndicatorDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetMiscFiresDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetMiscVFXsDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetMuzzleDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetNebulaDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetPlanetDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetScanDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetSlashDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetSunDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetTornadoDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetVolumetricLightDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetWarpDeviceActions()) yield return sig;
        foreach (MethodSignature sig in GetSourceOnlyActions()) yield return sig;
    }

    private static MethodSignature Act(
        string ownerType,
        string name,
        MethodParameter[] parameters,
        string? returnType = null,
        string? summary = null,
        RecommendLevel recommend = RecommendLevel.Default,
        CompressionHint? compressionHint = null,
        string? mapAlias = null,
        SdkAsyncMode asyncMode = SdkAsyncMode.Sync,
        string? notes = null,
        params DeviceCapability[] requireCapabilities)
    {
        string? notRecommendedReason = BuiltinRecommendationRules.GetObsoleteReason(
            MethodCategory.DeviceAction, ownerType, name);
        bool isObsolete = notRecommendedReason is not null;
        return new MethodSignature(
            MethodCategory.DeviceAction,
            ownerType,
            name,
            parameters,
            returnType,
            summary,
            null,
            recommend,
            MethodScope.Universal,
            compressionHint,
            mapAlias,
            IsObsolete: isObsolete,
            NotRecommendedReason: notRecommendedReason,
            AsyncMode: asyncMode,
            Notes: notes)
        {
            RequireCapabilities = requireCapabilities
        };
    }

    private static string BuildIndexedAnyParamRawName(int zeroBasedIndex)
    {
        string head = zeroBasedIndex.ToString();
        return head + "1@param" + head + "1";
    }

    /// <summary>Device 基类动作</summary>
    private static IEnumerable<MethodSignature> GetDeviceBaseActions()
    {
        // 位置/姿态控制
        yield return Act("Device", "getPosition", Array.Empty<MethodParameter>(), "Vector3", "获取位置");
        yield return Act("Device", "setPosKinematic", new[] { new MethodParameter("坐标@pos", "Vector3", Alias: "pos") }, null, "设置位置（运动学）");
        yield return Act("Device", "setRotKinematic", new[] { new MethodParameter("向前方向@forword", "Vector3", Alias: "forword"), new MethodParameter("向上方向@up", "Vector3", Alias: "up") }, null, "设置旋转（运动学）");
        yield return Act("Device", "setLocalPos", new[] { new MethodParameter("坐标@pos", "Vector3", Alias: "pos") }, null, "设置局部位置");
        yield return Act("Device", "setLocalRot", new[] { new MethodParameter("向前@pos", "Vector3", Alias: "forward"), new MethodParameter("向上@pos", "Vector3", Alias: "up") }, null, "设置局部旋转");
        yield return Act("Device", "Exist", Array.Empty<MethodParameter>(), "Bool", "是否存在");

        // 物理与刚体
        yield return Act("Device", "setKinematic", new[] { new MethodParameter("开关@OnOff", "Bool", Alias: "OnOff") }, null, "设置运动学模式",
            requireCapabilities: [DeviceCapability.Collidable]);
        yield return Act("Device", "enableCollide", new[] { new MethodParameter("开关@OnOff", "Bool", Alias: "OnOff") }, null, "开关碰撞",
            requireCapabilities: [DeviceCapability.Collidable]);
        yield return Act("Device", "setRigidVelocity", new[] { new MethodParameter("速度@velocity", "Vector3", Alias: "velocity") }, null, "设置刚体速度");
        yield return Act("Device", "getVelocity", Array.Empty<MethodParameter>(), "Vector3", "获取速度");
        yield return Act("Device", "setRigidAngularVelocity", new[] { new MethodParameter("速度@velocity", "Vector3", Alias: "velocity") }, null, "设置刚体角速度");
        yield return Act("Device", "angularVelocity", Array.Empty<MethodParameter>(), "Vector3", "获取角速度");
        yield return Act("Device", "setInertiaTensor", new[] { new MethodParameter("转动惯量@inertiaTensor", "Vector3", Alias: "inertiaTensor") }, null, "设置转动惯量");
        yield return Act("Device", "getInertiaTensor", Array.Empty<MethodParameter>(), "Vector3", "获取转动惯量");
        yield return Act("Device", "setMass", new[] { new MethodParameter("重量@mass", "Float", Alias: "mass") }, null, "设置质量");
        yield return Act("Device", "getMass", Array.Empty<MethodParameter>(), "Float", "获取质量");
        yield return Act("Device", "massCenter", Array.Empty<MethodParameter>(), "Vector3", "获取重心");
        yield return Act("Device", "setMassCenter", new[] { new MethodParameter("世界坐标@pos", "Vector3", Alias: "pos") }, null, "设置重心");
        yield return Act("Device", "autoSetMassCenter", Array.Empty<MethodParameter>(), null, "自动设置重心");
        yield return Act("Device", "setColliderSize", new[] { new MethodParameter("尺寸@Size", "Vector3", Alias: "Size"), new MethodParameter("局部坐标@Pos", "Vector3", Alias: "Pos") }, null, "设置碰撞体尺寸");
        yield return Act("Device", "setPhysicsMaterial", new[] { new MethodParameter("弹性@bounciness", "Float", Alias: "bounciness"), new MethodParameter("动摩擦@dynamicFriction", "Float", Alias: "dynamicFriction"), new MethodParameter("静摩擦@staticFriction", "Float", Alias: "staticFriction") }, null, "设置物理材质", notes: "粘连组级别");
        yield return Act("Device", "setBounciness", new[] { new MethodParameter(string.Empty, "Float", Alias: "bounciness") }, null, "设置弹性", notes: "粘连组级别；仅设置弹性，setPhysicsMaterial 可同时设置弹性和摩擦");
        yield return Act("Device", "setFriction", new[] { new MethodParameter("静摩擦系数@staticFriction", "Float", Alias: "staticFriction"), new MethodParameter("动摩擦系数@dynamicFriction", "Float", Alias: "dynamicFriction") }, null, "设置摩擦力", notes: "粘连组级别；仅设置摩擦，setPhysicsMaterial 可同时设置弹性和摩擦");

        // 力与扭矩
        yield return Act("Device", "addForce", new[] { new MethodParameter("力@force", "Vector3", Alias: "force"), new MethodParameter("类型@type", "Enum", EnumType: ForceMode, Alias: "type") }, null, "添加力");
        yield return Act("Device", "addForceAtPos", new[] { new MethodParameter("力@force", "Vector3", Alias: "force"), new MethodParameter("世界坐标@Pos", "Vector3", Alias: "Pos"), new MethodParameter("类型@type", "Enum", EnumType: ForceMode, Alias: "type") }, null, "在指定位置添加力");
        yield return Act("Device", "addTorque", new[] { new MethodParameter("力@force", "Vector3", Alias: "force"), new MethodParameter("类型@type", "Enum", EnumType: ForceMode, Alias: "type") }, null, "添加扭矩");
        yield return Act("Device", "addTorqueTo", new[]
        {
            new MethodParameter("大小@size", "Float", Alias: "size"),
            new MethodParameter("类型@type", "Enum", EnumType: ForceMode, Alias: "type"),
            new MethodParameter("当前方向@currDir", "Vector3", Alias: "currDir"),
            new MethodParameter("目标方向@targetDir", "Vector3", Alias: "targetDir"),
            new MethodParameter("匀速@uniform", "Bool", Alias: "uniform")
        }, null, "向目标方向添加扭矩");

        // HP/战斗属性
        yield return Act("Device", "getHP", Array.Empty<MethodParameter>(), "Float", "获取生命值");
        yield return Act("Device", "setHP", new[] { new MethodParameter(string.Empty, "Float", Alias: "hp") }, null, "设置生命值");

        // 材质与颜色（运行期）
        const string shaderMaterialPropertiesNote = "不是标准 Shader 属性设置接口；只能按平台材质属性表中列出的属性名和类型调用，属性名区分大小写。属性表查 machinist Skill: references/tips/shader-material-properties.md";
        const string fixedDynamicMaterialPropertiesNote = "不是标准 Shader 属性设置接口；这是平台预设材质项的快捷设置，不接受属性名。";
        yield return Act("Device", "setDynamicMatColor", new[] { new MethodParameter(string.Empty, "Color", Alias: "color") }, null, "设置动态材质颜色", notes: fixedDynamicMaterialPropertiesNote);
        yield return Act("Device", "setDynamicShader", new[] { new MethodParameter(string.Empty, "String", Alias: "shader") }, null, "设置动态着色器", notes: shaderMaterialPropertiesNote);
        yield return Act("Device", "setDynamicMatMetalic", new[] { new MethodParameter(string.Empty, "Float", Alias: "metallic") }, null, "设置动态金属度", notes: fixedDynamicMaterialPropertiesNote);
        yield return Act("Device", "setDynamicMatGloss", new[] { new MethodParameter(string.Empty, "Float", Alias: "gloss") }, null, "设置动态光泽度", notes: fixedDynamicMaterialPropertiesNote);
        yield return Act("Device", "setDynamicMatAlpha", new[] { new MethodParameter(string.Empty, "Float", Alias: "alpha") }, null, "设置动态透明度", notes: fixedDynamicMaterialPropertiesNote);
        yield return Act("Device", "setDynamicMatEmmit", new[] { new MethodParameter(string.Empty, "Float", Alias: "emission") }, null, "设置动态自发光", notes: fixedDynamicMaterialPropertiesNote);
        yield return Act("Device", "setDynamicMatPic", new[] { new MethodParameter("纹理名@name", "String", Alias: "name"), new MethodParameter("贴图文件@filename", "String", Alias: "filename") }, null, "设置动态贴图", notes: shaderMaterialPropertiesNote + "；" + ResourceFileNameNote);
        yield return Act("Device", "setPaintColor", new[] { new MethodParameter(string.Empty, "Color", Alias: "color"), new MethodParameter("独立变色", "Bool") }, null, "设置喷漆颜色");

        // 脚本交互
        yield return Act("Device", "loopFun", new[] { new MethodParameter("方法名@methodName", "String", Alias: "methodName", IsScriptMethodName: true), new MethodParameter("每次间隔@Interval", "Float", Alias: "Interval") }, null, "循环调用方法");
        yield return Act("Device", "DisableFun", new[] { new MethodParameter("方法名@methodName", "String", Alias: "methodName", IsScriptMethodName: true) }, null, "禁用方法");

        // 补充遗漏的基类方法
        yield return Act("Device", "getDevice", Array.Empty<MethodParameter>(), "Device", "获取零件自身");
        yield return Act("Device", "Name", Array.Empty<MethodParameter>(), "String", "获取名称");
        yield return Act("Device", "LogicName", Array.Empty<MethodParameter>(), "String", "获取逻辑名称");
        yield return Act("Device", "getMech", Array.Empty<MethodParameter>(), "Mech", "获取所属机械");
        yield return Act("Device", "getPlayer", Array.Empty<MethodParameter>(), "Player", "获取所属玩家");
        yield return Act("Device", "show", new[] { new MethodParameter(string.Empty, "Bool", Alias: "visible") }, null, "显示/隐藏", notes: "仅控制渲染可见性，不影响物理、碰撞和附属UI");
        yield return Act("Device", "setLayer", new[] { new MethodParameter(string.Empty, "Float", Alias: "layer") }, null, "设置层");
        yield return Act("Device", "localToWorldPos", new[] { new MethodParameter("局部坐标@localPos", "Vector3", Alias: "localPos") }, "Vector3", "局部坐标转世界坐标");
        yield return Act("Device", "localToWorldDir", new[] { new MethodParameter("局部方向@localDir", "Vector3", Alias: "localDir") }, "Vector3", "局部方向转世界方向");
        yield return Act("Device", "worldToLocalDir", new[] { new MethodParameter("世界方向@worldDir", "Vector3", Alias: "worldDir") }, "Vector3", "世界方向转局部方向");
        yield return Act("Device", "setScale", new[] { new MethodParameter("缩放@scale", "Float", Alias: "scale") }, null, "设置缩放");
        yield return Act("Device", "setDrag", new[] { new MethodParameter("阻力@drag", "Float", Alias: "drag") }, null, "设置阻力");
        yield return Act("Device", "setAngularDrag", new[] { new MethodParameter("阻力@drag", "Float", Alias: "drag") }, null, "设置角阻力");
        yield return Act("Device", "setMaxAngularVelocity", new[] { new MethodParameter("最大转速@v", "Float", Alias: "v") }, null, "设置最大角速度");
        yield return Act("Device", "setDensity", new[] { new MethodParameter("密度@density", "Float", Alias: "density") }, null, "设置密度",
            requireCapabilities: [DeviceCapability.Collidable]);
        yield return Act("Device", "switchCollider", new[] { new MethodParameter("开关@OnOff", "Bool", Alias: "OnOff") }, null, "切换碰撞体");
        yield return Act("Device", "lockRotAxis", new[] { new MethodParameter("X轴@X", "Bool", Alias: "X"), new MethodParameter("Y轴@Y", "Bool", Alias: "Y"), new MethodParameter("Z轴@Z", "Bool", Alias: "Z") }, null, "锁定旋转轴");
        yield return Act("Device", "lockMoveAxis", new[] { new MethodParameter("X轴@X", "Bool", Alias: "X"), new MethodParameter("Y轴@Y", "Bool", Alias: "Y"), new MethodParameter("Z轴@Z", "Bool", Alias: "Z") }, null, "锁定移动轴");
        yield return Act("Device", "setDynamicMatURLPic", new[] { new MethodParameter("纹理名@name", "String", Alias: "name"), new MethodParameter("贴图网址@URL", "String", Alias: "URL") }, null, "设置动态URL贴图", notes: shaderMaterialPropertiesNote);
        yield return Act("Device", "setDynamicMatFloat", new[] { new MethodParameter("属性名@name", "String", Alias: "name"), new MethodParameter("数值@value", "Float", Alias: "value") }, null, "设置动态材质浮点参数", notes: shaderMaterialPropertiesNote);
        yield return Act("Device", "setDynamicMatVector", new[] { new MethodParameter("属性名@name", "String", Alias: "name"), new MethodParameter("数值@value", "Vector3", Alias: "value") }, null, "设置动态材质向量参数", notes: shaderMaterialPropertiesNote);
        yield return Act("Device", "setMatCustomColor", new[] { new MethodParameter("属性名@name", "String", Alias: "name"), new MethodParameter("颜色@value", "Color", Alias: "value") }, null, "设置自定义材质颜色", notes: shaderMaterialPropertiesNote);
        yield return Act("Device", "wordToLocalPos", new[] { new MethodParameter("世界坐标@worldPos", "Vector3", Alias: "worldPos") }, "Vector3", "世界坐标转局部坐标(别名)");
    }

    /// <summary>CoreDevice 核心零件动作</summary>
    private static IEnumerable<MethodSignature> GetCoreDeviceActions()
    {
        // 角色控制（人形移动）
        yield return Act("CoreDevice", "ccMove", new[] { new MethodParameter("移动方向@moveDir", "Vector3", Alias: "moveDir"), new MethodParameter("旋转@rotate", "Float", Alias: "rotate"), new MethodParameter("绝对角度@absoluteAngle", "Bool", Alias: "absoluteAngle") }, null, "角色控制器移动");
        yield return Act("CoreDevice", "ccOnGround", Array.Empty<MethodParameter>(), "Bool", "是否在地面上");
        yield return Act("CoreDevice", "ccSetHeight", new[] { new MethodParameter("高度@height", "Float", Alias: "height") }, null, "设置角色高度");
        yield return Act("CoreDevice", "ccSetCenterPosY", new[] { new MethodParameter("Y轴位置@posY", "Float", Alias: "posY") }, null, "设置角色中心 Y");

        // 整机物理状态
        yield return Act("CoreDevice", "enableMechPhysics", new[] { new MethodParameter(string.Empty, "Bool", Alias: "enabled") }, null, "开关整机物理");
        yield return Act("CoreDevice", "enableGravity", new[] { new MethodParameter(string.Empty, "Bool", Alias: "enabled") }, null, "开关重力");
        yield return Act("CoreDevice", "setMechDrag", new[] { new MethodParameter(string.Empty, "Float", Alias: "drag") }, null, "设置空气阻力");
        yield return Act("CoreDevice", "ignoreCollideWithOtherMech", new[] { new MethodParameter(string.Empty, "Mech", Alias: "other"), new MethodParameter(string.Empty, "Bool", Alias: "enabled") }, null, "忽略与其他机械碰撞");
        yield return Act("CoreDevice", "die", Array.Empty<MethodParameter>(), null, "触发死亡");

        // A* 路径寻路
        yield return Act("CoreDevice", "findPath", new[] { new MethodParameter("起点@From", "Vector3", Alias: "From"), new MethodParameter("目标@Destiny", "Vector3", Alias: "Destiny") }, null, "寻路");
        yield return Act("CoreDevice", "findPathAsync", new[] { new MethodParameter("起点@From", "Vector3", Alias: "From"), new MethodParameter("目标@Destiny", "Vector3", Alias: "Destiny") }, "Float", "异步寻路", asyncMode: SdkAsyncMode.Async);
        yield return Act("CoreDevice", "getPathCount", Array.Empty<MethodParameter>(), "Float", "获取路径点数量");
        yield return Act("CoreDevice", "getPathAt", new[] { new MethodParameter("序号@Index", "Float", Alias: "Index") }, "Vector3", "获取路径点");

        // 补充遗漏的方法
        yield return Act("CoreDevice", "fixedLoopAct", new[] { new MethodParameter("方法名@methodName", "String", Alias: "methodName", IsScriptMethodName: true), new MethodParameter("每次间隔@Interval", "Float", Alias: "Interval") }, null, "固定帧循环调用");
        yield return Act("CoreDevice", "bulletFrom", Array.Empty<MethodParameter>(), "Device", "子弹来源");
    }

    /// <summary>CubeDevice 方块零件动作</summary>
    private static IEnumerable<MethodSignature> GetCubeDeviceActions()
    {
        yield return Act("CubeDevice", "setSize", new[] { new MethodParameter("尺寸@Size", "Vector3", Alias: "Size") }, null, "设置尺寸");
    }

    /// <summary>CapsuleDevice 胶囊零件动作</summary>
    private static IEnumerable<MethodSignature> GetCapsuleDeviceActions()
    {
        yield return Act("CapsuleDevice", "setSize", new[] { new MethodParameter("半径@Radius", "Float", Alias: "Radius"), new MethodParameter("长@Length", "Float", Alias: "Length") }, null, "设置尺寸");
    }

    /// <summary>Device2D 二维零件动作</summary>
    private static IEnumerable<MethodSignature> GetDevice2DActions()
    {
        yield return Act("Device2D", "setTrigger2D", new[] { new MethodParameter("开关@OnOff", "Bool", Alias: "OnOff") }, null, "开关 2D 触发器模式");
        yield return Act("Device2D", "SetTriggerCallBack", new[] { new MethodParameter("进入触发器回调方法", "String", IsScriptMethodName: true), new MethodParameter("离开触发器回调方法", "String", IsScriptMethodName: true) }, null, "设置 2D 触发器回调");
        yield return Act("RectDevice", "playVideo", new[]
        {
            new MethodParameter("文件名@file", "String", Alias: "file"),
            new MethodParameter("循环@loop", "Bool", Alias: "loop")
        }, "Struct", "播放视频");
        yield return Act("RectDevice", "playVideoAsync", new[]
        {
            new MethodParameter("文件名@file", "String", Alias: "file"),
            new MethodParameter("循环@loop", "Bool", Alias: "loop")
        }, "Struct", "播放视频", asyncMode: SdkAsyncMode.Async,
            notes: "返回 Struct 字段：time(Float)。");
        yield return Act("RectDevice", "setVideoParam", new[] { new MethodParameter("速率", "Float"), new MethodParameter("音量", "Float"), new MethodParameter("是否循环", "Bool") }, "Struct", "设置视频参数");
        yield return Act("RectDevice", "pauseVideo", new[] { new MethodParameter("暂停@pause", "Bool", Alias: "pause") }, null, "暂停视频");
        yield return Act("SpriteDevice", "setColor", new[] { new MethodParameter("颜色@color", "Color", Alias: "color") }, null, "设置颜色");
        yield return Act("SpriteDevice", "setTexture", new[] { new MethodParameter("贴图文件@filename", "String", Alias: "filename"), new MethodParameter("贴图大小", "Vector3") }, null, "设置贴图", notes: ResourceFileNameNote);
        yield return Act("SpriteDevice", "setURLTexture", new[] { new MethodParameter("网址@url", "String", Alias: "url") }, "Vector3", "设置网络图片", asyncMode: SdkAsyncMode.Async, notes: "返回图片分辨率；失败时返回 (0,0,0)");
        yield return Act("SpriteDevice", "setDanmuAvatar", new[] { new MethodParameter("玩家UID@UID", "String", Alias: "UID") }, null, "设置弹幕头像");
    }

    /// <summary>ObjDevice 自定义模型零件动作</summary>
    private static IEnumerable<MethodSignature> GetObjDeviceActions()
    {
        yield return Act("ObjDevice", "playAnimation", new[] { new MethodParameter("动画名称@animation name", "String", Alias: "animation_name"), new MethodParameter("过度时间@crossTime", "Float", Alias: "crossTime") }, null, "播放动画");
        yield return Act("ObjDevice", "stopAnimation", Array.Empty<MethodParameter>(), null, "停止动画");
        yield return Act("ObjDevice", "stopNameAnimation", new[] { new MethodParameter("动画名@Name", "String", Alias: "Name") }, null, "停止指定名称动画");
        yield return Act("ObjDevice", "getAnimationNameList", Array.Empty<MethodParameter>(), "List", "得到动画名称列表");
        yield return Act("ObjDevice", "getAnimationLength", new[] { new MethodParameter("动画名称@animation name", "String", Alias: "animation_name") }, "Float", "得到动画时长");
        yield return Act("ObjDevice", "setAnimState", new[] { new MethodParameter("动画名称@animation name", "String", Alias: "animation_name"), new MethodParameter("权重@weight", "Float", Alias: "weight"), new MethodParameter("速度@speed", "Float", Alias: "speed"), new MethodParameter("是否循环@Loop", "Bool", Alias: "Loop") }, null, "设置动画状态");
        yield return Act("ObjDevice", "setAnimLayer", new[] { new MethodParameter("动画名称@animation name", "String", Alias: "animation_name"), new MethodParameter("层@layer", "Float", Alias: "layer") }, null, "设置动画层");
        yield return Act("ObjDevice", "setAnimEvent", new[] { new MethodParameter("动画名称@animation name", "String", Alias: "animation_name"), new MethodParameter("触发时间@time", "Float", Alias: "time"), new MethodParameter("回调方法@callback", "String", Alias: "callback", IsScriptMethodName: true) }, null, "设置动画事件");
        yield return Act("ObjDevice", "clearAnimEvent", new[] { new MethodParameter("动画名称@animation name", "String", Alias: "animation_name") }, null, "清理动画事件");
        yield return Act("ObjDevice", "appendDevice", new[] { new MethodParameter("节点路径@TransformPath", "String", Alias: "TransformPath"), new MethodParameter("零件@device", "Device", Alias: "device") }, null, "零件跟随骨骼");
        yield return Act("ObjDevice", "setBoneSpringEffect", new[] { new MethodParameter("节点路径@TransformPath", "String", Alias: "TransformPath"), new MethodParameter("弹性@Spring", "Float", Alias: "Spring"), new MethodParameter("阻尼@Damping", "Float", Alias: "Damping") }, null, "设置节点晃动效果");
        yield return Act("ObjDevice", "enableSkin", new[] { new MethodParameter("模型名称@name", "String", Alias: "name"), new MethodParameter("显示@enable", "Bool", Alias: "enable") }, null, "开关皮肤");
        yield return Act("ObjDevice", "setDynamicMatPic2", new[] { new MethodParameter("纹理名@name", "String", Alias: "name"), new MethodParameter("贴图文件@filename", "String", Alias: "filename"), new MethodParameter("序号@Index", "Float", Alias: "Index") }, null, "设置子模型贴图参数", notes: ResourceFileNameNote);
        yield return Act("ObjDevice", "setMutiDynamicFloat", new[] { new MethodParameter("纹理名@name", "String", Alias: "name"), new MethodParameter("参数", "Float"), new MethodParameter("序号@Index", "Float", Alias: "Index") }, null, "设置子模型小数参数");
        yield return Act("ObjDevice", "setMutiDynamicVector", new[] { new MethodParameter("纹理名@name", "String", Alias: "name"), new MethodParameter("参数", "Vector3"), new MethodParameter("序号@Index", "Float", Alias: "Index") }, null, "设置子模型向量参数");
        yield return Act("ObjDevice", "setMutiDynamicColor", new[] { new MethodParameter("纹理名@name", "String", Alias: "name"), new MethodParameter("参数", "Color"), new MethodParameter("序号@Index", "Float", Alias: "Index") }, null, "设置子模型颜色参数");
        yield return Act("ObjDevice", "updateOffScreen", new[] { new MethodParameter("开关@onOff", "Bool", Alias: "onOff") }, null, "离屏时更新");
        yield return Act("ObjDevice", "replaceMesh", new[] { new MethodParameter("数据@data", "Bytes", Alias: "data"), new MethodParameter("缩放@scale", "Float", Alias: "scale") }, null, "替换网格",
            notes: "用模型二进制运行时替换自定义模型零件的网格；可接 ai.tripo 生成结果，但不会导入工程资源。");
        yield return Act("ObjDevice", "setVertexIndex", new[] { new MethodParameter("坐标列表@posList", "List", Alias: "posList"), new MethodParameter("法线列表@normalList", "List", Alias: "normalList"), new MethodParameter("索引列表@indexList", "List", Alias: "indexList"), new MethodParameter("UV列表@uvList", "List", Alias: "uvList") }, null, "设置顶点索引数据");
        yield return Act("ObjDevice", "getVertexs", Array.Empty<MethodParameter>(), "List", "得到顶点列表");
        yield return Act("ObjDevice", "getMeshNormals", Array.Empty<MethodParameter>(), "List", "得到模型法线列表");
        yield return Act("ObjDevice", "getMeshUVs", Array.Empty<MethodParameter>(), "List", "得到模型 UV 列表");
        yield return Act("ObjDevice", "getMeshIndicies", Array.Empty<MethodParameter>(), "List", "得到模型索引列表");
        yield return Act("ObjDevice", "getMesh", Array.Empty<MethodParameter>(), "Mesh", "得到模型",
            notes: "返回当前自定义模型零件的运行时 Mesh，可传给 ObjDevice.replaceMesh2 或 map.setCustomMesh。");
        yield return Act("ObjDevice", "replaceMesh2", new[] { new MethodParameter("模型网格@mesh", "Mesh", Alias: "mesh"), new MethodParameter("缩放@scale", "Float", Alias: "scale") }, null, "替换网格",
            notes: "接收运行时 Mesh 数据并替换当前网格；不是按文件名加载模型资源。");
        yield return Act("ObjDevice", "AddMixingTransform", new[] { new MethodParameter("动画名称@animation name", "String", Alias: "animation_name"), new MethodParameter("骨骼名称@bone", "String", Alias: "bone") }, null, "设置动画作用骨骼");
    }

    /// <summary>PrefabDevice 预制体零件动作</summary>
    private static IEnumerable<MethodSignature> GetPrefabDeviceActions()
    {
        yield return Act("PrefabDevice", "playParticleSystem", new[] { new MethodParameter("强制重播@replay", "Bool", Alias: "replay") }, null, "播放粒子系统");
        yield return Act("PrefabDevice", "stopParticleSystem", Array.Empty<MethodParameter>(), null, "关闭粒子系统");
        yield return Act("PrefabDevice", "getAnimationNameList", Array.Empty<MethodParameter>(), "List", "得到动画名称列表");
        yield return Act("PrefabDevice", "playAnimation", new[] { new MethodParameter("动画名称@animation name", "String", Alias: "animation_name"), new MethodParameter("过度时间@crossTime", "Float", Alias: "crossTime"), new MethodParameter("速度@speed", "Float", Alias: "speed") }, null, "播放动画");
        yield return Act("PrefabDevice", "stopAnimation", Array.Empty<MethodParameter>(), null, "停止动画");
        yield return Act("PrefabDevice", "appendDevice", new[] { new MethodParameter("节点路径@TransformPath", "String", Alias: "TransformPath"), new MethodParameter("零件@device", "Device", Alias: "device") }, null, "零件跟随节点");
    }

    /// <summary>ParticleDevice 粒子零件动作</summary>
    private static IEnumerable<MethodSignature> GetParticleDeviceActions()
    {
        yield return Act("ParticleDevice", "active", new[] { new MethodParameter("开关@OnOff", "Bool", Alias: "OnOff"), new MethodParameter("快进时间@advance", "Float", Alias: "advance") }, null, "开关粒子");
        yield return Act("ParticleDevice", "emit", new[] { new MethodParameter("数量@Count", "Float", Alias: "Count") }, null, "发射指定个数粒子");
        yield return Act("ParticleDevice", "setParticleDir", new[] { new MethodParameter("朝向@Dir", "Vector3", Alias: "Dir") }, null, "设置粒子朝向");
        yield return Act("ParticleDevice", "startLifetimeMultiplier", new[] { new MethodParameter(string.Empty, "Float", Alias: "multiplier") }, null, "设置生命周期倍数");
        yield return Act("ParticleDevice", "startSpeed", new[] { new MethodParameter("最小@min", "Float", Alias: "min"), new MethodParameter("最大@max", "Float", Alias: "max") }, null, "设置初始速度");
        yield return Act("ParticleDevice", "startSize", new[] { new MethodParameter("最小@min", "Float", Alias: "min"), new MethodParameter("最大@max", "Float", Alias: "max") }, null, "设置初始大小");
        yield return Act("ParticleDevice", "color", new[] { new MethodParameter(string.Empty, "Color", Alias: "colorMin"), new MethodParameter(string.Empty, "Color", Alias: "colorMax") }, null, "设置颜色");
        yield return Act("ParticleDevice", "setBright", new[] { new MethodParameter(string.Empty, "Float", Alias: "brightness") }, null, "设置亮度");
        yield return Act("ParticleDevice", "gravityModifierMultiplier", new[] { new MethodParameter(string.Empty, "Float", Alias: "multiplier") }, null, "设置重力倍数");
        yield return Act("ParticleDevice", "rateOverTime", new[] { new MethodParameter(string.Empty, "Float", Alias: "rate") }, null, "设置发射速率");
        yield return Act("ParticleDevice", "colorOverLifetime", new[] { new MethodParameter("颜色列表@Color List", "List", Alias: "Color_List"), new MethodParameter("时间列表@Time List", "List", Alias: "Time_List") }, null, "设置生命周期颜色");
        yield return Act("ParticleDevice", "angle", new[] { new MethodParameter(string.Empty, "Float", Alias: "angle") }, null, "设置角度");
        yield return Act("ParticleDevice", "radius", new[] { new MethodParameter(string.Empty, "Float", Alias: "radius") }, null, "设置半径");
        yield return Act("ParticleDevice", "rotSpeed", new[] { new MethodParameter(string.Empty, "Float", Alias: "speed") }, null, "设置旋转速度");
        yield return Act("ParticleDevice", "orbital", new[] { new MethodParameter(string.Empty, "Vector3", Alias: "velocity") }, null, "设置轨道速度");
        yield return Act("ParticleDevice", "renderMode", new[] { new MethodParameter(string.Empty, "Enum", EnumType: ParticleRenderMode, Alias: "mode") }, null, "设置渲染模式");
        yield return Act("ParticleDevice", "lengthScale", new[] { new MethodParameter(string.Empty, "Float", Alias: "scale") }, null, "设置长度缩放");
        yield return Act("ParticleDevice", "matType", new[] { new MethodParameter(string.Empty, "Enum", EnumType: ParticleMaterial, Alias: "type") }, null, "设置材质类型");
        yield return Act("ParticleDevice", "setOffset", new[] { new MethodParameter(string.Empty, "Float", Alias: "offset") }, null, "设置偏移");
        yield return Act("ParticleDevice", "activeAtPos", new[] { new MethodParameter(string.Empty, "Vector3", Alias: "position") }, null, "在指定位置激活");
        yield return Act("ParticleDevice", "exportParticle", Array.Empty<MethodParameter>(), "String", "导出粒子配置");
        yield return Act("ParticleDevice", "importParticle", new[] { new MethodParameter(string.Empty, "String", Alias: "config") }, null, "导入粒子配置");
        yield return Act("ParticleDevice", "resizeParticleSystem", new[] { new MethodParameter("比例@Rate", "Float", Alias: "Rate") }, null, "缩放粒子系统");
        yield return Act("ParticleDevice", "sizeOverLifetime", new[] { new MethodParameter(string.Empty, "Vector3", Alias: "p0"), new MethodParameter(string.Empty, "Vector3", Alias: "p1"), new MethodParameter(string.Empty, "Vector3", Alias: "p2"), new MethodParameter(string.Empty, "Vector3", Alias: "p3"), new MethodParameter(string.Empty, "Vector3", Alias: "p4") }, null, "设置生命周期大小");
        yield return Act("ParticleDevice", "velocityOverLifetime", new[] { new MethodParameter(string.Empty, "Vector3", Alias: "p0"), new MethodParameter(string.Empty, "Vector3", Alias: "p1"), new MethodParameter(string.Empty, "Vector3", Alias: "p2"), new MethodParameter(string.Empty, "Vector3", Alias: "p3"), new MethodParameter(string.Empty, "Vector3", Alias: "p4") }, null, "设置生命周期速度");
        yield return Act("ParticleDevice", "setCollideLayerMask", new[] { new MethodParameter(string.Empty, "List", Alias: "layers") }, null, "设置粒子碰撞层列表");
    }

    /// <summary>LightDevice 灯光零件动作</summary>
    private static IEnumerable<MethodSignature> GetLightDeviceActions()
    {
        yield return Act("LightDevice", "setColor", new[] { new MethodParameter("颜色@color", "Color", Alias: "color") }, null, "设置颜色");
        yield return Act("LightDevice", "setLight", new[] { new MethodParameter("distance", "Float"), new MethodParameter("indensity", "Float"), new MethodParameter("angle", "Float") }, null, "设置灯光参数");
    }

    /// <summary>CameraDevice 摄像机零件动作</summary>
    private static IEnumerable<MethodSignature> GetCameraDeviceActions()
    {
        yield return Act("CameraDevice", "active", Array.Empty<MethodParameter>(), null, "激活摄像机");
        yield return Act("CameraDevice", "disActive", Array.Empty<MethodParameter>(), null, "停用摄像机");
        yield return Act("CameraDevice", "setDistance", new[] { new MethodParameter("距离@dis", "Float", Alias: "dis") }, null, "设置距离");
        yield return Act("CameraDevice", "setYDistance", new[] { new MethodParameter("Y方向距离@Y dis", "Float", Alias: "Y_dis") }, null, "设置Y方向距离");
        yield return Act("CameraDevice", "setXDistance", new[] { new MethodParameter("X方向距离@X dis", "Float", Alias: "X_dis") }, null, "设置X方向距离");
        yield return Act("CameraDevice", "setCameraScale", new[] { new MethodParameter("缩放@scale", "Float", Alias: "scale") }, null, "设置缩放");
    }

    /// <summary>JointDevice 关节零件动作</summary>
    private static IEnumerable<MethodSignature> GetJointDeviceActions()
    {
        yield return Act("JointDevice", "setJointMassScale", new[] { new MethodParameter("系数@rate", "Float", Alias: "rate") }, null, "设置关节质量系数");
    }

    /// <summary>RoterDevice 旋转零件动作</summary>
    private static IEnumerable<MethodSignature> GetRoterDeviceActions()
    {
        yield return Act("RoterDevice", "rotate", new[] { new MethodParameter("角度@angle", "Float", Alias: "angle"), new MethodParameter("转动方式@rotType", "Enum", EnumType: RotateMode, Alias: "rotType") }, null, "旋转");
        yield return Act("RoterDevice", "setRotateLimit", new[] { new MethodParameter("范围系数@rangeRate", "Float", Alias: "rangeRate") }, null, "设置旋转限制");
        yield return Act("RoterDevice", "getAngle", Array.Empty<MethodParameter>(), "Float", "获取角度");
        yield return Act("RoterDevice", "setPower", new[] { new MethodParameter(string.Empty, "Float", Alias: "power") }, null, "设置力量");
        yield return Act("RoterDevice", "setDamper", new[] { new MethodParameter(string.Empty, "Float", Alias: "damper") }, null, "设置阻尼");
    }

    /// <summary>SpringDevice 弹簧零件动作</summary>
    private static IEnumerable<MethodSignature> GetSpringDeviceActions()
    {
        yield return Act("SpringDevice", "active", Array.Empty<MethodParameter>(), null, "激活");
        yield return Act("SpringDevice", "disActive", Array.Empty<MethodParameter>(), null, "停用");
        yield return Act("SpringDevice", "setLength", new[] { new MethodParameter("最大长度@maxLength", "Float", Alias: "maxLength") }, null, "设置长度");
        yield return Act("SpringDevice", "setSpringForce", new[] { new MethodParameter("弹力绳力量@force", "Float", Alias: "force") }, null, "设置弹力");
        yield return Act("SpringDevice", "setSpringDrag", new[] { new MethodParameter("弹力绳阻尼@spring drag", "Float", Alias: "spring_drag") }, null, "设置阻尼");
    }

    /// <summary>TriggerBuilding 触发器零件动作</summary>
    private static IEnumerable<MethodSignature> GetTriggerBuildingActions()
    {
        yield return Act("TriggerBuilding", "setTriggerSize", new[] { new MethodParameter("大小@size", "Vector3", Alias: "size") }, null, "设置触发器大小");
        yield return Act("TriggerBuilding", "getInsideMechs", Array.Empty<MethodParameter>(), "List", "得到区域内机械");
        yield return Act("TriggerBuilding", "setTriggerEnterCB", new[] { new MethodParameter("通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true) }, null, "设置进入触发器通知方法");
        yield return Act("TriggerBuilding", "setTriggerLeaveCB", new[] { new MethodParameter("通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true) }, null, "设置离开触发器通知方法");
    }

    /// <summary>TextBuilding 文字零件动作</summary>
    private static IEnumerable<MethodSignature> GetTextBuildingActions()
    {
        yield return Act("TextBuilding", "getLabel", Array.Empty<MethodParameter>(), "UILabel", "获取文本标签");
    }

    /// <summary>BombDevice 炸弹零件动作</summary>
    private static IEnumerable<MethodSignature> GetBombDeviceActions()
    {
        yield break;
    }

    /// <summary>PropellerDevice 螺旋桨零件动作</summary>
    private static IEnumerable<MethodSignature> GetPropellerDeviceActions()
    {
        yield break;
    }

    /// <summary>FuncDevice 功能零件基类动作</summary>
    private static IEnumerable<MethodSignature> GetFuncDeviceActions()
    {
        yield break;
    }

    /// <summary>WheelColliderDevice 轮子碰撞器零件动作</summary>
    private static IEnumerable<MethodSignature> GetWheelColliderDeviceActions()
    {
        yield return Act("WheelColliderDevice", "motorTorque", new[] { new MethodParameter("移动@motorTorque", "Float", Alias: "motorTorque") }, null, "设置驱动扭矩");
        yield return Act("WheelColliderDevice", "steerAngle", new[] { new MethodParameter("转动@steerAngle", "Float", Alias: "steerAngle") }, null, "设置转向角度");
        yield return Act("WheelColliderDevice", "brakeTorque", new[] { new MethodParameter("刹车@brakeTorque", "Float", Alias: "brakeTorque") }, null, "设置刹车扭矩");
        yield return Act("WheelColliderDevice", "forceAppPointDis", new[] { new MethodParameter(string.Empty, "Float", Alias: "distance") }, null, "设置力作用点距离");
        yield return Act("WheelColliderDevice", "wheelDampingRate", new[] { new MethodParameter("阻尼@damping", "Float", Alias: "damping") }, null, "设置轮子阻尼");
        yield return Act("WheelColliderDevice", "suspensionSpring", new[] { new MethodParameter("弹力@spring", "Float", Alias: "spring"), new MethodParameter("阻尼@damper", "Float", Alias: "damper"), new MethodParameter("目标位置@targetPos", "Float", Alias: "targetPos"), new MethodParameter("最大距离@suspensionDis", "Float", Alias: "suspensionDis") }, null, "设置悬挂弹簧");
        yield return Act("WheelColliderDevice", "forwardFriction", new[] { new MethodParameter("倍数@stiffness", "Float", Alias: "stiffness"), new MethodParameter("极值点@extremum", "Vector3", Alias: "extremum"), new MethodParameter("渐近点@asymptote", "Vector3", Alias: "asymptote") }, null, "设置前向摩擦");
        yield return Act("WheelColliderDevice", "sidewaysFriction", new[] { new MethodParameter("倍数@stiffness", "Float", Alias: "stiffness"), new MethodParameter("极值点@extremum", "Vector3", Alias: "extremum"), new MethodParameter("渐近点@asymptote", "Vector3", Alias: "asymptote") }, null, "设置侧向摩擦");
        yield return Act("WheelColliderDevice", "isOnGround", Array.Empty<MethodParameter>(), "Bool", "是否接地");
        yield return Act("WheelColliderDevice", "rpm", Array.Empty<MethodParameter>(), "Float", "获取转速");
    }

    /// <summary>GunDevice 枪械零件动作</summary>
    private static IEnumerable<MethodSignature> GetGunDeviceActions()
    {
        yield break;
    }

    /// <summary>LaserDevice 激光零件动作</summary>
    private static IEnumerable<MethodSignature> GetLaserDeviceActions()
    {
        yield return Act("LaserDevice", "setEnemyType", new[] { new MethodParameter("跟踪类型@traceType", "Enum", EnumType: LaserEnemyType, Alias: "traceType") }, null, "设置敌人类型");
        yield return Act("LaserDevice", "checkHit", Array.Empty<MethodParameter>(), "Bool", "检测命中");
        yield return Act("LaserDevice", "getHitDeviceInfo", Array.Empty<MethodParameter>(), "Struct", "获取命中零件信息",
            notes: "返回 Struct 字段：pos(Vector3)、normal(Vector3)、device(Device)。");
        yield return Act("LaserDevice", "getTargetPos", Array.Empty<MethodParameter>(), "Vector3", "获取目标位置");
        yield return Act("LaserDevice", "getTargetDevice", Array.Empty<MethodParameter>(), "Device", "获取目标零件");
        yield return Act("LaserDevice", "setRaycastElapse", new[] { new MethodParameter("秒@second", "Float", Alias: "second") }, null, "设置射线检测间隔");
        yield return Act("LaserDevice", "setColor", new[] { new MethodParameter("颜色@color", "Color", Alias: "color") }, null, "设置颜色");
        yield return Act("LaserDevice", "setLength", new[] { new MethodParameter("长度@length", "Float", Alias: "length") }, null, "设置长度");
        yield return Act("LaserDevice", "setOffset", new[] { new MethodParameter("偏移@offset", "Float", Alias: "offset") }, null, "设置偏移");
        yield return Act("LaserDevice", "setCheckLayer", new[] { new MethodParameter("层列表@layers", "List", Alias: "layers") }, null, "设置检测层",
            notes: "列表元素必须为 Float（平台层编号，有效范围 0-23）。");
        yield return Act("LaserDevice", "setWidth", new[] { new MethodParameter("宽度@width", "Float", Alias: "width") }, null, "设置宽度");
    }

    /// <summary>MagnetDevice 磁铁零件动作</summary>
    private static IEnumerable<MethodSignature> GetMagnetDeviceActions()
    {
        yield return Act("MagnetDevice", "release", Array.Empty<MethodParameter>(), null, "释放");
        yield return Act("MagnetDevice", "setAutoAttach", new[] { new MethodParameter("是否自动吸物体@autoAttach", "Bool", Alias: "autoAttach") }, null, "设置自动吸附");
        yield return Act("MagnetDevice", "getIfAttach", Array.Empty<MethodParameter>(), "Bool", "是否已吸附");
    }

    /// <summary>MechShooter 机械发射器动作</summary>
    /// <remarks>
    /// ⚠️ 禁止修改警告 ⚠️
    /// 此方法中的 generate(List参数) 与 generate0..generate8(展开参数) 并存是必须的！
    /// 这是两套不同的 API：
    ///   - generate(Method_Name, Param_List): 旧版 List 参数形式
    ///   - generate0..generate8: 新版展开参数形式（无压缩提示）
    /// 与 GetSourceOnlyActions() 中的 generate0..generate8（有压缩提示）共同存在。
    /// 绝对禁止删除或修改任何注册！映射冲突应在映射规则层解决，而非此处！
    /// </remarks>
    private static IEnumerable<MethodSignature> GetMechShooterActions()
    {
        // 新版展开参数 API：generate0..generate8 / shootDir0..shootDir8
        // 压缩后输出为 generate / shootDir
        CompressionHint generateHint = new(CompressionKind.NumericSuffix, "generate", Array.Empty<string>());
        CompressionHint shootDirHint = new(CompressionKind.NumericSuffix, "shootDir", Array.Empty<string>());
        for (int i = 0; i <= 8; i++)
        {
            List<MethodParameter> generateParams = new(capacity: 1 + i)
            {
                new("方法名@Method Name", "String", Alias: "methodName", IsScriptMethodName: true)
            };
            for (int j = 1; j <= i; j++)
            {
                generateParams.Add(new($"参数{j - 1}1@param{j - 1}1", "Any", Alias: "data" + j));
            }
            yield return Act("MechShooter", "generate" + i, generateParams.ToArray(), null, "生成机械", compressionHint: generateHint);

            List<MethodParameter> shootDirParams = new(capacity: 2 + i)
            {
                new("子弹速度@bulletSpeed", "Vector3", Alias: "bulletSpeed"),
                new("方法名@Method Name", "String", Alias: "methodName", IsScriptMethodName: true)
            };
            for (int j = 1; j <= i; j++)
            {
                shootDirParams.Add(new($"参数{j - 1}1@param{j - 1}1", "Any", Alias: "data" + j));
            }
            yield return Act("MechShooter", "shootDir" + i, shootDirParams.ToArray(), null, "向指定方向发射", compressionHint: shootDirHint);
        }

        yield return Act("MechShooter", "setGenerator", new[] { new MethodParameter("机械产生器别名@Generator Alias", "String", Alias: "Generator_Alias") }, null, "设置生成器");
        yield return Act("MechShooter", "setVelocity", new[] { new MethodParameter("速度@speed", "Float", Alias: "speed") }, null, "设置速度");
        yield return Act("MechShooter", "setAntiImpulse", new[] { new MethodParameter("后坐力系数@antiImpulse", "Float", Alias: "antiImpulse") }, null, "设置后坐力");
        yield return Act("MechShooter", "setPosOffset", new[] { new MethodParameter("偏移坐标@offsetPos", "Vector3", Alias: "offsetPos") }, null, "设置位置偏移");
        yield return Act("MechShooter", "enableRelativeVelocity", new[] { new MethodParameter("开关@OnOff", "Bool", Alias: "OnOff") }, null, "启用相对速度");
    }

    /// <summary>MechSlot 机械槽位动作</summary>
    private static IEnumerable<MethodSignature> GetMechSlotActions()
    {
        yield return Act("MechSlot", "setOffsetRot", new[] { new MethodParameter("局部偏移@localOffset", "Vector3", Alias: "localOffset"), new MethodParameter("局部向前方向@localForward", "Vector3", Alias: "localForward"), new MethodParameter("局部向上方向@localUp", "Vector3", Alias: "localUp") }, null, "设置偏移旋转");
        yield return Act("MechSlot", "getEquipMech", Array.Empty<MethodParameter>(), "Mech", "获取装备的机械");
        yield return Act("MechSlot", "generateEquipment", new[]
        {
            new MethodParameter("机械产生器别名@generatorAlias", "String", Alias: "generatorAlias"),
            new MethodParameter("机械文件名@MechFilename", "String", Alias: "MechFilename")
        }, "Mech", "生成装备");
        yield return Act("MechSlot", "destroyEquipment", Array.Empty<MethodParameter>(), null, "销毁装备");
    }

    /// <summary>MotorWheel 马达轮动作</summary>
    private static IEnumerable<MethodSignature> GetMotorWheelActions()
    {
        yield return Act("MotorWheel", "turnForwardOrBack", new[] { new MethodParameter("移动@turn", "Float", Alias: "turn") }, null, "前进或后退");
        yield return Act("MotorWheel", "turnLeftOrRight", new[] { new MethodParameter("转动@turn", "Float", Alias: "turn") }, null, "左转或右转");
        yield return Act("MotorWheel", "setMoveForceRate", new[] { new MethodParameter(string.Empty, "Float", Alias: "rate") }, null, "设置移动力倍率");
        yield return Act("MotorWheel", "setMotorVelocityRate", new[] { new MethodParameter(string.Empty, "Float", Alias: "rate") }, null, "设置马达速度倍率");
    }

    /// <summary>PistonJoint 活塞关节动作</summary>
    private static IEnumerable<MethodSignature> GetPistonJointActions()
    {
        yield return Act("PistonJoint", "move", new[] { new MethodParameter("距离@dis", "Float", Alias: "dis") }, null, "移动");
        yield return Act("PistonJoint", "getAngle", Array.Empty<MethodParameter>(), "Float", "获取角度");
        yield return Act("PistonJoint", "setPower", new[] { new MethodParameter(string.Empty, "Float", Alias: "power") }, null, "设置力量");
        yield return Act("PistonJoint", "setDamper", new[] { new MethodParameter(string.Empty, "Float", Alias: "damper") }, null, "设置阻尼");
        yield return Act("PistonJoint", "setMinMaxPos", new[] { new MethodParameter("最小", "Float"), new MethodParameter("最大", "Float") }, null, "设置最小最大位置");
    }

    /// <summary>Roter2Device 旋转零件2动作</summary>
    private static IEnumerable<MethodSignature> GetRoter2DeviceActions()
    {
        yield return Act("Roter2Device", "rotate", new[] { new MethodParameter("角度@angle", "Vector3", Alias: "angle") }, null, "旋转");
        yield return Act("Roter2Device", "getAngle", Array.Empty<MethodParameter>(), "Vector3", "获取角度");
        yield return Act("Roter2Device", "setPower", new[] { new MethodParameter(string.Empty, "Float", Alias: "power") }, null, "设置力量");
        yield return Act("Roter2Device", "setDamper", new[] { new MethodParameter(string.Empty, "Float", Alias: "damper") }, null, "设置阻尼");
    }

    /// <summary>SwitchDevice 开关零件动作</summary>
    private static IEnumerable<MethodSignature> GetSwitchDeviceActions()
    {
        yield return Act("SwitchDevice", "active", Array.Empty<MethodParameter>(), null, "激活");
        yield return Act("SwitchDevice", "disActive", Array.Empty<MethodParameter>(), null, "停用");
    }

    /// <summary>TrailDevice 拖尾零件动作</summary>
    private static IEnumerable<MethodSignature> GetTrailDeviceActions()
    {
        yield return Act("TrailDevice", "setColor", new[] { new MethodParameter("颜色@color", "Color", Alias: "color") }, null, "设置颜色");
        yield return Act("TrailDevice", "parts_SmokeSize", new[] { new MethodParameter("大小@size", "Float", Alias: "size") }, null, "设置烟雾大小");
        yield return Act("TrailDevice", "parts_Duration", new[] { new MethodParameter("持续时间@duration", "Float", Alias: "duration") }, null, "设置持续时间");
        yield return Act("TrailDevice", "parts_clearPoints", Array.Empty<MethodParameter>(), null, "清空拖尾点");
    }

    /// <summary>SmokeDevice 烟雾零件动作</summary>
    private static IEnumerable<MethodSignature> GetSmokeDeviceActions()
    {
        yield return Act("SmokeDevice", "setColor", new[] { new MethodParameter("颜色@color", "Color", Alias: "color") }, null, "设置颜色");
        yield return Act("SmokeDevice", "parts_SmokeSize", new[] { new MethodParameter("大小@size", "Float", Alias: "size") }, null, "设置烟雾大小");
        yield return Act("SmokeDevice", "parts_Duration", new[] { new MethodParameter("持续时间@smokeTimeLength", "Float", Alias: "smokeTimeLength") }, null, "设置持续时间");
    }

    /// <summary>FlareDevice 闪光零件动作</summary>
    private static IEnumerable<MethodSignature> GetFlareDeviceActions()
    {
        yield return Act("FlareDevice", "flareScale", new[] { new MethodParameter("缩放@Scale", "Vector3", Alias: "Scale") }, null, "设置闪光缩放");
        yield return Act("FlareDevice", "flareLengthScale", new[] { new MethodParameter("发光系数@Rate", "Float", Alias: "Rate") }, null, "设置闪光长度缩放");
        yield return Act("FlareDevice", "flareColor", new[] { new MethodParameter("颜色@Color", "Color", Alias: "Color") }, null, "设置闪光颜色");
        yield return Act("FlareDevice", "setTexture", new[] { new MethodParameter("贴图文件@filename", "String", Alias: "filename") }, null, "设置贴图", notes: ResourceFileNameNote);
    }

    /// <summary>FireDevice 火焰零件动作</summary>
    private static IEnumerable<MethodSignature> GetFireDeviceActions()
    {
        yield return Act("FireDevice", "setColor", new[] { new MethodParameter("颜色@color", "Color", Alias: "color") }, null, "设置颜色");
    }

    /// <summary>DecalDevice 贴花零件动作</summary>
    private static IEnumerable<MethodSignature> GetDecalDeviceActions()
    {
        yield return Act("DecalDevice", "setTexture", new[] { new MethodParameter("贴图文件@filename", "String", Alias: "filename") }, null, "设置贴图", notes: ResourceFileNameNote);
    }

    /// <summary>AreaBuilding 区域零件动作</summary>
    private static IEnumerable<MethodSignature> GetAreaBuildingActions()
    {
        yield return Act("AreaBuilding", "getCenter", Array.Empty<MethodParameter>(), "Vector3", "获取中心");
        yield return Act("AreaBuilding", "getSize", Array.Empty<MethodParameter>(), "Vector3", "获取大小");
        yield return Act("AreaBuilding", "getRndPos", Array.Empty<MethodParameter>(), "Vector3", "获取随机位置");
    }

    /// <summary>BirthBuilding 出生点零件动作</summary>
    private static IEnumerable<MethodSignature> GetBirthBuildingActions()
    {
        yield return Act("BirthBuilding", "getGenerateMech", Array.Empty<MethodParameter>(), "Mech", "获取生成的机械", notes: "返回此产生器最近一次产生的机械引用");
        yield return Act("BirthBuilding", "changeMech", new[] { new MethodParameter("机械文件名@mech filename", "String", Alias: "mech_filename") }, null, "更换机械", notes: "切换机械生成器后续生成时使用的机械文件；不会替换已经生成的机械实例。");
        yield return Act("BirthBuilding", "getMechSnapshot", new[] { new MethodParameter("坐标@pos", "Vector3", Alias: "pos") }, "UIRawImage", "获取机械快照");
    }

    /// <summary>AntiGravityDevice 反重力零件动作</summary>
    private static IEnumerable<MethodSignature> GetAntiGravityDeviceActions()
    {
        yield break;
    }

    /// <summary>MoveDevice 移动零件动作</summary>
    private static IEnumerable<MethodSignature> GetMoveDeviceActions()
    {
        yield break;
    }

    /// <summary>RecorderDevice 录制零件动作</summary>
    private static IEnumerable<MethodSignature> GetRecorderDeviceActions()
    {
        yield break;
    }

    /// <summary>TrafficLightDevice 交通灯零件动作</summary>
    private static IEnumerable<MethodSignature> GetTrafficLightDeviceActions()
    {
        yield return Act("TrafficLightDevice", "changeLight", new[] { new MethodParameter("类型@Type", "Enum", EnumType: TrafficLightState, Alias: "Type") }, null, "切换灯光");
    }

    /// <summary>ArrowBuilding 箭头零件动作</summary>
    private static IEnumerable<MethodSignature> GetArrowBuildingActions()
    {
        yield return Act("ArrowBuilding", "setColor", new[] { new MethodParameter("颜色@color", "Color", Alias: "color") }, null, "设置颜色");
    }

    /// <summary>CannonGun 加农炮动作</summary>
    private static IEnumerable<MethodSignature> GetCannonGunActions()
    {
        yield break;
    }

    /// <summary>ClipGun 弹夹枪动作</summary>
    private static IEnumerable<MethodSignature> GetClipGunActions()
    {
        yield break;
    }

    /// <summary>GatlinGun 加特林动作</summary>
    private static IEnumerable<MethodSignature> GetGatlinGunActions()
    {
        yield break;
    }

    /// <summary>LaserMelee 激光近战武器动作</summary>
    private static IEnumerable<MethodSignature> GetLaserMeleeActions()
    {
        yield return Act("LaserMelee", "setColor", new[] { new MethodParameter("颜色@color", "Color", Alias: "color") }, null, "设置颜色");
        yield return Act("LaserMelee", "setLight", new[] { new MethodParameter("范围@range", "Float", Alias: "range"), new MethodParameter("亮度@intensity", "Float", Alias: "intensity") }, null, "设置灯光");
    }

    /// <summary>MoveDevice2 移动零件2动作</summary>
    private static IEnumerable<MethodSignature> GetMoveDevice2Actions()
    {
        yield break;
    }

    /// <summary>PistonDeviceLine 活塞线零件动作</summary>
    private static IEnumerable<MethodSignature> GetPistonDeviceLineActions()
    {
        yield return Act("PistonDeviceLine", "move", new[] { new MethodParameter("距离@dis", "Float", Alias: "dis") }, null, "移动");
    }

    /// <summary>ClientMapLogicDevice 客户端地图逻辑零件动作</summary>
    private static IEnumerable<MethodSignature> GetClientMapLogicDeviceActions()
    {
        yield return Act("ClientMapLogicDevice", "setActive", new[] { new MethodParameter(string.Empty, "Bool", Alias: "active") }, null, "设置激活状态");
    }

    /// <summary>SLGLogicDevice 策略游戏逻辑零件动作</summary>
    private static IEnumerable<MethodSignature> GetSLGLogicDeviceActions()
    {
        yield return Act("SLGLogicDevice", "initialWorld", new[]
        {
            new MethodParameter("名称@name", "String", Alias: "name"),
            new MethodParameter("格子数@GridNum", "Long", Alias: "GridNum"),
            new MethodParameter("格子大小@GridSize", "Long", Alias: "GridSize")
        }, null, "初始化世界");
        yield return Act("SLGLogicDevice", "asyncEnterWorld", new[]
        {
            new MethodParameter("名称@name", "String", Alias: "name")
        }, "Struct", "异步进入世界", asyncMode: SdkAsyncMode.Async,
            notes: "返回 Struct 由服务端填充。");
        yield return Act("SLGLogicDevice", "exitWorld", Array.Empty<MethodParameter>(), null, "退出世界");
        yield return Act("SLGLogicDevice", "findItemAsync", new[]
        {
            new MethodParameter("物体ID@itemID", "Long", Alias: "itemID"),
            new MethodParameter("坐标X@X", "Long", Alias: "X"),
            new MethodParameter("坐标Y@Y", "Long", Alias: "Y")
        }, "Struct", "异步查找物品", asyncMode: SdkAsyncMode.Async,
            notes: "返回 Struct 由服务端填充。");
        yield return Act("SLGLogicDevice", "getItem", new[] { new MethodParameter("物体ID@itemID", "Long", Alias: "itemID") }, "Struct", "获取物品",
            notes: "返回 Struct 字段：id(Long)、x(Long)、y(Long)、type(Long)、owner(Player)、info(Dict)、error(String)。未找到时返回空结构体。");
        yield return Act("SLGLogicDevice", "getItemAt", new[] { new MethodParameter("x", "Long"), new MethodParameter("y", "Long") }, "List", "获取指定位置物品");
        yield return Act("SLGLogicDevice", "getAllItemInView", new[] { new MethodParameter("类型@type", "Long", Alias: "type") }, "List", "获取视野内所有物品");
        yield return Act("SLGLogicDevice", "worldNewItem", new[]
        {
            new MethodParameter("x", "Long"),
            new MethodParameter("y", "Long"),
            new MethodParameter("类型@type", "Long", Alias: "type"),
            new MethodParameter("信息@info", "Dict", Alias: "info"),
            new MethodParameter("存在时间@lifeLength", "Long", Alias: "lifeLength"),
            new MethodParameter("禁止同类型重叠@ForbidSameType", "Bool", Alias: "ForbidSameType"),
            new MethodParameter("报错通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true)
        }, "Long", "创建物品");
        yield return Act("SLGLogicDevice", "worldDeleteItem", new[]
        {
            new MethodParameter("id", "Long"),
            new MethodParameter("报错通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true),
            new MethodParameter("延迟@Delay", "Float", Alias: "Delay")
        }, null, "删除物品");
        yield return Act("SLGLogicDevice", "worldMoveItem", new[]
        {
            new MethodParameter("id", "Long"),
            new MethodParameter("x", "Long"),
            new MethodParameter("y", "Long"),
            new MethodParameter("禁止同类型重叠@ForbidSameType", "Bool", Alias: "ForbidSameType"),
            new MethodParameter("报错通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true)
        }, null, "移动物品");
        yield return Act("SLGLogicDevice", "worldMoveFocus", new[] { new MethodParameter("x", "Long"), new MethodParameter("y", "Long") }, null, "移动焦点");
        yield return Act("SLGLogicDevice", "worldChangeItem", new[]
        {
            new MethodParameter("id", "Long"),
            new MethodParameter("信息@info", "Dict", Alias: "info"),
            new MethodParameter("报错通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true)
        }, null, "修改物品属性");
        yield return Act("SLGLogicDevice", "worldChangeGlobalData", new[]
        {
            new MethodParameter("信息@info", "Dict", Alias: "info"),
            new MethodParameter("报错通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true)
        }, null, "修改全局数据");
        yield return Act("SLGLogicDevice", "worldSetItemOwner", new[]
        {
            new MethodParameter("id", "Long"),
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("报错通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true)
        }, null, "设置物品所有者");
        yield return Act("SLGLogicDevice", "worldItemVarAddAsync", new[]
        {
            new MethodParameter("id", "Long"),
            new MethodParameter("变量名@key", "String", Alias: "key"),
            new MethodParameter("增加量@add", "Long", Alias: "add")
        }, "Struct", "异步物品变量增加", asyncMode: SdkAsyncMode.Async,
            notes: "返回 Struct 由服务端填充。");
        yield return Act("SLGLogicDevice", "worldItemVarLimit", new[]
        {
            new MethodParameter("id", "Long"),
            new MethodParameter("变量名@key", "String", Alias: "key"),
            new MethodParameter("上限@limit", "Long", Alias: "limit"),
            new MethodParameter("报错通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true)
        }, null, "物品变量限制");
        yield return Act("SLGLogicDevice", "worldTalk", new[]
        {
            new MethodParameter("内容@word", "String", Alias: "word"),
            new MethodParameter("附件@attach", "Dict", Alias: "attach")
        }, null, "世界聊天");
        yield return Act("SLGLogicDevice", "Fun_getGlobalData", Array.Empty<MethodParameter>(), "Dict", "获取全局数据");
    }

    /// <summary>SpineDevice 骨骼动画零件动作</summary>
    private static IEnumerable<MethodSignature> GetSpineDeviceActions()
    {
        yield return Act("SpineDevice", "PlayAnim", new[] { new MethodParameter("动画名", "String"), new MethodParameter("循环", "Bool") }, null, "播放动画");
        yield return Act("SpineDevice", "AnimDuration", Array.Empty<MethodParameter>(), "Float", "动画时长");
        yield return Act("SpineDevice", "SetTimeScale", new[] { new MethodParameter("速率", "Float") }, null, "动画速率");
        yield return Act("SpineDevice", "SetMirror", new[] { new MethodParameter("左右", "Bool"), new MethodParameter("上下", "Bool") }, null, "镜像");
        yield return Act("SpineDevice", "SetCallBack", new[] { new MethodParameter("结束回调方法", "String", IsScriptMethodName: true), new MethodParameter("自定义回调前缀", "String") }, null, "设置事件回调");
        yield return Act("SpineDevice", "SetSkin", new[] { new MethodParameter("皮肤列表", "List") }, null, "设置皮肤");
        yield return Act("SpineDevice", "boneList", Array.Empty<MethodParameter>(), "List", "骨骼列表");
        yield return Act("SpineDevice", "boneIndex", new[] { new MethodParameter("骨骼名", "String") }, "Float", "得到骨骼序号");
        yield return Act("SpineDevice", "BonePos", new[] { new MethodParameter("骨骼序号", "Float") }, "Vector3", "得到骨骼位置");
    }

    /// <summary>
    /// 补充 SDK 动作定义，包含方法名、所属类型及已登记的参数和返回值
    /// </summary>
    private static IEnumerable<MethodSignature> GetSourceOnlyActions()
    {
        // Device (source-only)
        (string ReturnType, string NameSuffix)[] callClientMapRetTypes = new (string ReturnType, string NameSuffix)[]
        {
            (ReturnType: "Float", NameSuffix: "Float"),
            (ReturnType: "Vector3", NameSuffix: "Vector3"),
            (ReturnType: "Bool", NameSuffix: "Bool"),
            (ReturnType: "Color", NameSuffix: "Color"),
            (ReturnType: "Long", NameSuffix: "Long"),
            (ReturnType: "Device", NameSuffix: "Device"),
            (ReturnType: "String", NameSuffix: "String"),
            (ReturnType: "List", NameSuffix: "List"),
            (ReturnType: "Dict", NameSuffix: "Dict"),
            (ReturnType: "Struct", NameSuffix: "Struct"),
            (ReturnType: "Mech", NameSuffix: "Mech"),
            (ReturnType: "Player", NameSuffix: "Player"),
            (ReturnType: "Datetime", NameSuffix: "Datetime"),
        };

        string[] callClientMapReturnTypes = callClientMapRetTypes
            .Select(static x => x.ReturnType)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        CompressionHint callClientMapRetHint = new(CompressionKind.TypeSuffixWithNumeric, "callClientMapRet", callClientMapReturnTypes);
        foreach ((string? returnType, string? nameSuffix) in callClientMapRetTypes)
        {
            for (int i = 0; i <= 10; i++)
            {
                MethodParameter[] parameters = new MethodParameter[i + 1];
                parameters[0] = new MethodParameter("零件别名.方法名@LogicName.MethodName", "String", Alias: "LogicName.MethodName", IsScriptMethodName: true);
                for (int j = 0; j < i; j++)
                {
                    int suffix = j + 1;
                    parameters[suffix] = new MethodParameter("参数" + BuildIndexedAnyParamRawName(j), "Any", Alias: $"param{suffix}");
                }

                yield return Act(
                    "Device",
                    $"callClientMapRet{nameSuffix}Func{i}",
                    parameters,
                    returnType,
                    "可变参数方法；调用分机逻辑返回",
                    compressionHint: callClientMapRetHint);
            }
        }
        CompressionHint callMapFuncHint = new(CompressionKind.NumericSuffix, "callMapFunc", Array.Empty<string>());
        for (int i = 0; i <= 10; i++)
        {
            MethodParameter[] parameters = new MethodParameter[i + 1];
            parameters[0] = new MethodParameter("零件别名.方法名@LogicName.MethodName", "String", Alias: "LogicName.MethodName", IsScriptMethodName: true);
            for (int j = 0; j < i; j++)
            {
                int suffix = j + 1;
                parameters[suffix] = new MethodParameter("参数" + BuildIndexedAnyParamRawName(j), "Any", Alias: $"param{suffix}");
            }

            yield return Act(
                "Device",
                $"callMapFunc{i}",
                parameters,
                null,
                $"调用地图方法（{i} 参数）",
                compressionHint: callMapFuncHint);
        }

        // BeamDevice (source-only)
        yield return Act("BeamDevice", "setColor1", new[] { new MethodParameter(string.Empty, "Color", Alias: "color1") });
        yield return Act("BeamDevice", "setColor2", new[] { new MethodParameter(string.Empty, "Color", Alias: "color2") });
        yield return Act("BeamDevice", "setDir", new[] { new MethodParameter("向前", "Vector3"), new MethodParameter("向上", "Vector3") });
        yield return Act("BeamDevice", "switch", new[] { new MethodParameter("开关@Onoff", "Bool", Alias: "Onoff") });
        yield return Act("BeamDevice", "setSize", new[] { new MethodParameter("长@Length", "Float", Alias: "Length"), new MethodParameter("半径@Radius", "Float", Alias: "RadiusStart"), new MethodParameter("击中半径@Radius", "Float", Alias: "RadiusEnd") });

        // BillboardDevice (source-only)
        yield return Act("BillboardDevice", "PlayFrameAnim", new[] { new MethodParameter("动画名", "String"), new MethodParameter("帧间隔", "Float") });
        yield return Act("BillboardDevice", "SetFrameAnim", new[] { new MethodParameter("动画名", "String"), new MethodParameter("文件名列表@filename", "List", Alias: "filename"), new MethodParameter("是否循环", "Bool") });
        yield return Act("BillboardDevice", "SetPic", new[] { new MethodParameter("文件名", "String") });
        yield return Act("BillboardDevice", "SetPicOffset", new[] { new MethodParameter("偏移", "Vector3") });
        yield return Act("BillboardDevice", "picRot", new[] { new MethodParameter("角度", "Float") });
        yield return Act("BillboardDevice", "setFrameAnimCallBack", new[] { new MethodParameter("动画名", "String"), new MethodParameter("帧序号", "Float"), new MethodParameter("回调方法", "String", IsScriptMethodName: true) });
        yield return Act("BillboardDevice", "setMirror", new[] { new MethodParameter("左右镜像", "Bool"), new MethodParameter("上下镜像", "Bool") });

        // BlackholeDevice (source-only)
        yield return Act("BlackholeDevice", "setBlackholeColor", new[] { new MethodParameter("颜色@Color", "Color", Alias: "Color") });
        yield return Act("BlackholeDevice", "setBlackholeParam", new[]
        {
            new MethodParameter("变形量@DisortionAmount", "Float", Alias: "DisortionAmount"),
            new MethodParameter("变形缩放@DisortionScale", "Float", Alias: "DisortionScale"),
            new MethodParameter("旋转@RotationAmount", "Float", Alias: "RotationAmount"),
            new MethodParameter("螺旋@TwirlStrength", "Float", Alias: "TwirlStrength"),
            new MethodParameter("颗粒转速@ParticleRotate", "Float", Alias: "ParticleRotate"),
            new MethodParameter("颗粒吸引@ParticleAttract", "Float", Alias: "ParticleAttract"),
            new MethodParameter("颗粒范围@ParticleRadius", "Float", Alias: "ParticleRadius")
        });

        // ElectricArcDevice (source-only)
        yield return Act("ElectricArcDevice", "setElecricArc", new[]
        {
            new MethodParameter("宽度@Thickness", "Float", Alias: "Thickness"),
            new MethodParameter("速度@Speed", "Float", Alias: "Speed"),
            new MethodParameter("力度@Power", "Float", Alias: "Power"),
            new MethodParameter("频率@Frequency", "Float", Alias: "Frequency")
        });
        yield return Act("ElectricArcDevice", "setElectricArcColor", new[] { new MethodParameter("颜色@Color", "Color", Alias: "Color") });
        yield return Act("ElectricArcDevice", "setPoints", new[] { new MethodParameter("P1", "Vector3"), new MethodParameter("P2", "Vector3"), new MethodParameter("P3", "Vector3") });

        // ElectricBallDevice (source-only)
        yield return Act("ElectricBallDevice", "setColor1", new[] { new MethodParameter(string.Empty, "Color", Alias: "color1") });
        yield return Act("ElectricBallDevice", "setColor2", new[] { new MethodParameter(string.Empty, "Color", Alias: "color2") });
        yield return Act("ElectricBallDevice", "setColor3", new[] { new MethodParameter(string.Empty, "Color", Alias: "color3") });
        yield return Act("ElectricBallDevice", "setSpawnRate", new[] { new MethodParameter(string.Empty, "Float", Alias: "rate") });

        // EnergyShieldDevice (source-only)
        yield return Act("EnergyShieldDevice", "setFresnelColor", new[] { new MethodParameter(string.Empty, "Color", Alias: "color") });
        yield return Act("EnergyShieldDevice", "setFresnelPower", new[] { new MethodParameter(string.Empty, "Float", Alias: "power") });
        yield return Act("EnergyShieldDevice", "setFrontColor", new[] { new MethodParameter(string.Empty, "Color", Alias: "color") });
        yield return Act("EnergyShieldDevice", "setHitPos", new[]
        {
            new MethodParameter("坐标", "Vector3"),
            new MethodParameter("范围", "Float"),
            new MethodParameter("亮度", "Float"),
            new MethodParameter("衰减", "Float"),
            new MethodParameter("弹力", "Float")
        });
        yield return Act("EnergyShieldDevice", "setMesh", new[] { new MethodParameter(string.Empty, "String", Alias: "mesh") });

        // FallingLeavesDevice (source-only)
        yield return Act("FallingLeavesDevice", "setCount", new[] { new MethodParameter("数量@Count", "Float", Alias: "Count"), new MethodParameter("时长@LifeTime", "Float", Alias: "LifeTime") });
        yield return Act("FallingLeavesDevice", "setRange", new[] { new MethodParameter(string.Empty, "Float", Alias: "range") });
        yield return Act("FallingLeavesDevice", "setRndRot", new[] { new MethodParameter(string.Empty, "Float", Alias: "rotation") });
        yield return Act("FallingLeavesDevice", "setWind", new[] { new MethodParameter("风向@Wind", "Vector3", Alias: "Wind"), new MethodParameter("随机风@Turbulence", "Float", Alias: "Turbulence") });
        yield return Act("FallingLeavesDevice", "setSize", new[] { new MethodParameter(string.Empty, "Vector3", Alias: "size") });

        // ForceFieldDevice (source-only)
        yield return Act("ForceFieldDevice", "setForceFieldScale", new[] { new MethodParameter("大小", "Float") });
        yield return Act("ForceFieldDevice", "setColor", new[] { new MethodParameter(string.Empty, "Color", Alias: "color") });
        yield return Act("ForceFieldDevice", "setFresnelPower", new[] { new MethodParameter(string.Empty, "Float", Alias: "power") });
        yield return Act("ForceFieldDevice", "setTwirl", new[] { new MethodParameter("扭曲1", "Float"), new MethodParameter("扭曲1", "Float") });

        // GalaxyDevice (source-only)
        yield return Act("GalaxyDevice", "setFocusDistance", new[] { new MethodParameter(string.Empty, "Float", Alias: "distance") });
        yield return Act("GalaxyDevice", "setFocusRange", new[] { new MethodParameter(string.Empty, "Float", Alias: "range") });
        yield return Act("GalaxyDevice", "setMainTex", new[] { new MethodParameter(string.Empty, "Float", Alias: "index") });

        // GasPlanetDevice (source-only)
        yield return Act("GasPlanetDevice", "setAtmosphereColor", new[] { new MethodParameter("颜色1@Color1", "Color", Alias: "Color1") });
        yield return Act("GasPlanetDevice", "setColors", new[]
        {
            new MethodParameter("颜色列表@ColorList", "List", Alias: "ColorList"),
            new MethodParameter("偏移1@Pos1", "Float", Alias: "Pos1"),
            new MethodParameter("偏移2@Pos2", "Float", Alias: "Pos2"),
            new MethodParameter("偏移3@Pos3", "Float", Alias: "Pos3")
        });
        yield return Act("GasPlanetDevice", "setFlow", new[] { new MethodParameter("力度@Power", "Float", Alias: "Power"), new MethodParameter("速度@Speed", "Float", Alias: "Speed") });
        yield return Act("GasPlanetDevice", "setMainTex", new[] { new MethodParameter(string.Empty, "String", Alias: "texture") }, notes: ResourceFileNameNote);

        // IndicatorDevice (source-only)
        yield return Act("IndicatorDevice", "setIndicator", new[] { new MethodParameter("尺寸", "Vector3"), new MethodParameter("填充", "Float") });
        yield return Act("IndicatorDevice", "setColor", new[] { new MethodParameter("颜色", "Color"), new MethodParameter("颜色2", "Color") });
        yield return Act("IndicatorDevice", "switch", new[] { new MethodParameter("开关", "Bool") });

        // MiscFiresDevice (source-only)
        yield return Act("MiscFiresDevice", "play", new[] { new MethodParameter("序号", "Float"), new MethodParameter("缩放", "Float") });
        yield return Act("MiscFiresDevice", "stop", Array.Empty<MethodParameter>());

        // MiscVFXsDevice (source-only)
        yield return Act("MiscVFXsDevice", "play", new[] { new MethodParameter("序号", "Float"), new MethodParameter("缩放", "Float") });
        yield return Act("MiscVFXsDevice", "setColor", new[] { new MethodParameter("名称", "String"), new MethodParameter("数值", "Color") });
        yield return Act("MiscVFXsDevice", "stop", Array.Empty<MethodParameter>());
        yield return Act("MiscVFXsDevice", "getAllProperties", Array.Empty<MethodParameter>(), "String");
        yield return Act("MiscVFXsDevice", "setFloat", new[] { new MethodParameter("名称", "String"), new MethodParameter("数值", "Float") });
        yield return Act("MiscVFXsDevice", "setVector", new[] { new MethodParameter("名称", "String"), new MethodParameter("数值", "Vector3") });

        // MuzzleDevice (source-only)
        yield return Act("MuzzleDevice", "SetParams", new[] { new MethodParameter("颜色@Color", "Color", Alias: "Color"), new MethodParameter("持续时间@time", "Float", Alias: "time") });
        yield return Act("MuzzleDevice", "play", Array.Empty<MethodParameter>());

        // NebulaDevice (source-only)
        yield return Act("NebulaDevice", "setVelocity", new[] { new MethodParameter(string.Empty, "Float", Alias: "velocity") });
        yield return Act("NebulaDevice", "setPushForce", new[] { new MethodParameter("爆炸点", "Vector3"), new MethodParameter(string.Empty, "Float", Alias: "force") });
        yield return Act("NebulaDevice", "setRndRot", new[] { new MethodParameter(string.Empty, "Float", Alias: "rotation") });
        yield return Act("NebulaDevice", "setCloud", new[]
        {
            new MethodParameter("数量@Count", "Float", Alias: "Count"),
            new MethodParameter("大小范围@Scale", "Vector3", Alias: "Scale"),
            new MethodParameter("产生范围@Range", "Vector3", Alias: "Range"),
            new MethodParameter("颜色1@Color1", "Color", Alias: "Color1"),
            new MethodParameter("颜色2@Color2", "Color", Alias: "Color2")
        });
        yield return Act("NebulaDevice", "setStone", new[]
        {
            new MethodParameter("数量@Count", "Float", Alias: "Count"),
            new MethodParameter("大小范围@Scale", "Vector3", Alias: "Scale"),
            new MethodParameter("产生范围@Range", "Vector3", Alias: "Range"),
            new MethodParameter("颜色1@Color1", "Color", Alias: "Color1"),
            new MethodParameter("颜色2@Color2", "Color", Alias: "Color2")
        });

        // PlanetDevice (source-only)
        yield return Act("PlanetDevice", "setMainTex", new[] { new MethodParameter(string.Empty, "String", Alias: "texture") }, notes: ResourceFileNameNote);
        yield return Act("PlanetDevice", "setColors", new[]
        {
            new MethodParameter("颜色列表@ColorList", "List", Alias: "ColorList"),
            new MethodParameter("偏移1@Pos1", "Float", Alias: "Pos1"),
            new MethodParameter("偏移2@Pos2", "Float", Alias: "Pos2"),
            new MethodParameter("偏移3@Pos3", "Float", Alias: "Pos3")
        });
        yield return Act("PlanetDevice", "setAtmosphereColor", new[] { new MethodParameter(string.Empty, "Color", Alias: "color"), new MethodParameter("菲涅尔@Fresnel", "Float", Alias: "Fresnel") });
        yield return Act("PlanetDevice", "setWater", new[] { new MethodParameter("颜色@Color", "Color", Alias: "Color"), new MethodParameter("金属度@Metallic", "Float", Alias: "Metallic") });
        yield return Act("PlanetDevice", "setCloud", new[] { new MethodParameter("颜色", "Color"), new MethodParameter("速度@Speed", "Float", Alias: "Speed") });

        // ScanDevice (source-only)
        yield return Act("ScanDevice", "setColor", new[] { new MethodParameter(string.Empty, "Color", Alias: "color") });
        yield return Act("ScanDevice", "setMesh", new[] { new MethodParameter(string.Empty, "String", Alias: "mesh") });
        yield return Act("ScanDevice", "setRange", new[] { new MethodParameter(string.Empty, "Float", Alias: "range") });
        yield return Act("ScanDevice", "setShake", new[] { new MethodParameter(string.Empty, "Float", Alias: "shake") });
        yield return Act("ScanDevice", "setThickness", new[] { new MethodParameter(string.Empty, "Float", Alias: "thickness") });

        // SlashDevice (source-only)
        yield return Act("SlashDevice", "SetParams", new[] { new MethodParameter("颜色@Color", "Color", Alias: "Color"), new MethodParameter("时间@time", "Float", Alias: "time"), new MethodParameter("填充", "Float"), new MethodParameter("扭曲", "Float") });
        yield return Act("SlashDevice", "play", Array.Empty<MethodParameter>());

        // SunDevice (source-only)
        yield return Act("SunDevice", "setMainTex", new[] { new MethodParameter(string.Empty, "String", Alias: "texture") }, notes: ResourceFileNameNote);
        yield return Act("SunDevice", "setColors", new[]
        {
            new MethodParameter("颜色列表@ColorList", "List", Alias: "ColorList"),
            new MethodParameter("偏移1@Pos1", "Float", Alias: "Pos1"),
            new MethodParameter("偏移2@Pos2", "Float", Alias: "Pos2"),
            new MethodParameter("偏移3@Pos3", "Float", Alias: "Pos3"),
            new MethodParameter("边缘颜色@FresnelColor", "Color", Alias: "FresnelColor"),
            new MethodParameter("边缘系数@Fresnel", "Float", Alias: "Fresnel")
        });
        yield return Act("SunDevice", "setAtmosphereColor", new[] { new MethodParameter("颜色1@Color1", "Color", Alias: "Color1"), new MethodParameter("颜色2@Color2", "Color", Alias: "Color2") });
        yield return Act("SunDevice", "setCrown", new[] { new MethodParameter("颜色@Color", "Color", Alias: "Color") });
        yield return Act("SunDevice", "setFlow", new[] { new MethodParameter("力度@Power", "Float", Alias: "Power"), new MethodParameter("速度@Speed", "Float", Alias: "Speed") });

        // TornadoDevice (source-only)
        yield return Act("TornadoDevice", "setTornadoColor", new[] { new MethodParameter("颜色1@Color1", "Color", Alias: "Color1"), new MethodParameter("颜色2@Color2", "Color", Alias: "Color2") });
        yield return Act("TornadoDevice", "setTornadoParam", new[] { new MethodParameter("持续时间@Duration", "Float", Alias: "Duration"), new MethodParameter("缩放@Scale", "Vector3", Alias: "Scale") });
        yield return Act("TornadoDevice", "switchTornado", Array.Empty<MethodParameter>());

        // VolumetricLightDevice (source-only)
        yield return Act("VolumetricLightDevice", "setColor", new[] { new MethodParameter(string.Empty, "Color", Alias: "color") });
        yield return Act("VolumetricLightDevice", "setVolumeParam", new[] { new MethodParameter("角度@SpotAngle", "Float", Alias: "SpotAngle"), new MethodParameter("边缘@fresnelPow", "Float", Alias: "fresnelPow"), new MethodParameter("距离衰减@Fade", "Float", Alias: "Fade"), new MethodParameter("尺寸@Size", "Vector3", Alias: "Size") });

        // WarpDevice (source-only)
        yield return Act("WarpDevice", "setWarpColor", new[] { new MethodParameter("颜色@Color", "Color", Alias: "Color") });
        yield return Act("WarpDevice", "setWarpParam", new[] { new MethodParameter("速度@Speed", "Float", Alias: "Speed"), new MethodParameter("扭曲力度@FresnelAmount", "Float", Alias: "FresnelAmount"), new MethodParameter("扭曲@Twirl", "Float", Alias: "Twirl") });
    }

    // VFX 零件类型（暂无具体动作定义，返回空）
    private static IEnumerable<MethodSignature> GetBeamDeviceActions() { yield break; }
    private static IEnumerable<MethodSignature> GetBillboardDeviceActions() { yield break; }
    private static IEnumerable<MethodSignature> GetBlackholeDeviceActions() { yield break; }
    private static IEnumerable<MethodSignature> GetElectricArcDeviceActions() { yield break; }
    private static IEnumerable<MethodSignature> GetElectricBallDeviceActions() { yield break; }
    private static IEnumerable<MethodSignature> GetEnergyShieldDeviceActions() { yield break; }
    private static IEnumerable<MethodSignature> GetFallingLeavesDeviceActions() { yield break; }
    private static IEnumerable<MethodSignature> GetForceFieldDeviceActions() { yield break; }
    private static IEnumerable<MethodSignature> GetGalaxyDeviceActions() { yield break; }
    private static IEnumerable<MethodSignature> GetGasPlanetDeviceActions() { yield break; }
    private static IEnumerable<MethodSignature> GetIndicatorDeviceActions() { yield break; }
    private static IEnumerable<MethodSignature> GetMiscFiresDeviceActions() { yield break; }
    private static IEnumerable<MethodSignature> GetMiscVFXsDeviceActions() { yield break; }
    private static IEnumerable<MethodSignature> GetMuzzleDeviceActions() { yield break; }
    private static IEnumerable<MethodSignature> GetNebulaDeviceActions() { yield break; }
    private static IEnumerable<MethodSignature> GetPlanetDeviceActions() { yield break; }
    private static IEnumerable<MethodSignature> GetScanDeviceActions() { yield break; }
    private static IEnumerable<MethodSignature> GetSlashDeviceActions() { yield break; }
    private static IEnumerable<MethodSignature> GetSunDeviceActions() { yield break; }
    private static IEnumerable<MethodSignature> GetTornadoDeviceActions() { yield break; }
    private static IEnumerable<MethodSignature> GetVolumetricLightDeviceActions() { yield break; }
    private static IEnumerable<MethodSignature> GetWarpDeviceActions() { yield break; }
}
#pragma warning restore TK006
