using static Toolkit.Core.Sdk.BuiltinEnumTypes;

namespace Toolkit.Core.Sdk;

// ╔════════════════════════════════════════════════════════════════════════════════╗
// ║                     【禁止审查区域 - 原版 SDK 精确映射】                          ║
// ╠════════════════════════════════════════════════════════════════════════════════╣
// ║  本文件包含对原版 BeScript SDK 变量方法（实体与 UI 类型）的精确硬编码映射。         ║
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
/// 变量方法 (varf) 定义 - 实体类型和 UI 类型
/// </summary>
public static class VarFuncDefinitions_Entity
{
    private const string ResourceFileNameNote = "文件参数只填包含扩展名的文件本身名称，不填 GUID 或带路径的名称。";

    private static readonly (string ReturnType, string NameSuffix)[] DeviceCallReturnTypeSpecs =
    {
        (ReturnType: "Float", NameSuffix: "Float"),
        (ReturnType: "Long", NameSuffix: "Long"),
        (ReturnType: "String", NameSuffix: "String"),
        (ReturnType: "Bool", NameSuffix: "Bool"),
        (ReturnType: "Vector3", NameSuffix: "Vector3"),
        (ReturnType: "Color", NameSuffix: "Color"),
        (ReturnType: "List", NameSuffix: "Fun"),
        (ReturnType: "Dict", NameSuffix: "Dict"),
        (ReturnType: "Struct", NameSuffix: "Struct"),
        (ReturnType: "Device", NameSuffix: "Device"),
        (ReturnType: "Mech", NameSuffix: "Mech"),
        (ReturnType: "Player", NameSuffix: "Player"),
        (ReturnType: "Datetime", NameSuffix: "Datetime"),
        (ReturnType: "Bytes", NameSuffix: "Bytes"),
        (ReturnType: "Canvas", NameSuffix: "Canvas"),
        (ReturnType: "Icon", NameSuffix: "Icon"),
        (ReturnType: "UIRect", NameSuffix: "UIRect"),
        (ReturnType: "UILabel", NameSuffix: "UILabel"),
        (ReturnType: "UIButton", NameSuffix: "UIButton"),
        (ReturnType: "UIInput", NameSuffix: "UIInput"),
        (ReturnType: "UIRawImage", NameSuffix: "UIRawImage"),
        (ReturnType: "UISpine", NameSuffix: "UISpine"),
        (ReturnType: "UILine", NameSuffix: "UILine"),
        (ReturnType: "UIAvatar", NameSuffix: "UIAvatar"),
    };

    private static readonly (string ReturnType, string NameSuffix)[] MechCallReturnTypeSpecs =
    {
        (ReturnType: "Float", NameSuffix: "Float"),
        (ReturnType: "Long", NameSuffix: "Long"),
        (ReturnType: "String", NameSuffix: "String"),
        (ReturnType: "Bool", NameSuffix: "Bool"),
        (ReturnType: "Vector3", NameSuffix: "Vector3"),
        (ReturnType: "Color", NameSuffix: "Color"),
        (ReturnType: "List", NameSuffix: "Fun"),
        (ReturnType: "Dict", NameSuffix: "Dict"),
        (ReturnType: "Struct", NameSuffix: "Struct"),
        (ReturnType: "Device", NameSuffix: "Device"),
        (ReturnType: "Mech", NameSuffix: "Mech"),
        (ReturnType: "Player", NameSuffix: "Player"),
        (ReturnType: "Datetime", NameSuffix: "Datetime"),
        (ReturnType: "Bytes", NameSuffix: "Bytes"),
        (ReturnType: "Canvas", NameSuffix: "Canvas"),
        (ReturnType: "Icon", NameSuffix: "Icon"),
        (ReturnType: "UIAvatar", NameSuffix: "UIAvatar"),
        (ReturnType: "UIButton", NameSuffix: "UIButton"),
        (ReturnType: "UIInput", NameSuffix: "UIInput"),
        (ReturnType: "UILabel", NameSuffix: "UILabel"),
        (ReturnType: "UILine", NameSuffix: "UILine"),
        (ReturnType: "UIRawImage", NameSuffix: "UIRawImage"),
        (ReturnType: "UISpine", NameSuffix: "UISpine"),
        (ReturnType: "UIRect", NameSuffix: "UIRect"),
    };

    public static IEnumerable<MethodSignature> GetAll()
    {
        // 实体类型
        foreach (MethodSignature sig in GetDeviceMethods()) yield return sig;
        foreach (MethodSignature sig in GetMechMethods()) yield return sig;
        foreach (MethodSignature sig in GetPlayerMethods()) yield return sig;

        // UI 类型
        foreach (MethodSignature sig in GetCanvasMethods()) yield return sig;
        foreach (MethodSignature sig in GetUICommonMethods()) yield return sig;
        foreach (MethodSignature sig in GetUILabelMethods()) yield return sig;
        foreach (MethodSignature sig in GetUIButtonMethods()) yield return sig;
        foreach (MethodSignature sig in GetUIRectMethods()) yield return sig;
        foreach (MethodSignature sig in GetUIInputMethods()) yield return sig;
        foreach (MethodSignature sig in GetUIRawImageMethods()) yield return sig;
        foreach (MethodSignature sig in GetUISpineMethods()) yield return sig;
        foreach (MethodSignature sig in GetUILineMethods()) yield return sig;
        foreach (MethodSignature sig in GetUIAvatarMethods()) yield return sig;
    }

    private static MethodSignature Varf(
        string ownerType,
        string name,
        MethodParameter[] parameters,
        string? returnType = null,
        string? summary = null,
        RecommendLevel recommend = RecommendLevel.Default,
        CompressionHint? compressionHint = null,
        SdkAsyncMode asyncMode = SdkAsyncMode.Sync,
        string? notes = null)
    {
        string? notRecommendedReason = BuiltinRecommendationRules.GetObsoleteReason(
            MethodCategory.VarFunc, ownerType, name);
        bool isObsolete = notRecommendedReason is not null;
        return new MethodSignature(
            MethodCategory.VarFunc,
            ownerType,
            name,
            parameters,
            returnType,
            summary,
            null,
            recommend,
            MethodScope.Universal,
            compressionHint,
            IsObsolete: isObsolete,
            NotRecommendedReason: notRecommendedReason,
            AsyncMode: asyncMode,
            Notes: notes);
    }

    private static string BuildIndexedAnyParamRawName(int zeroBasedIndex)
    {
        string head = zeroBasedIndex.ToString();
        return head + "1@param" + head + "1";
    }

    private static IEnumerable<MethodSignature> GetDeviceMethods()
    {
        // 基本信息
        yield return Varf("Device", "name", Array.Empty<MethodParameter>(), "String", "获取零件名称");
        yield return Varf("Device", "typeID", Array.Empty<MethodParameter>(), "Float", "获取零件类型 ID");

        // 位置与变换
        yield return Varf("Device", "pos", Array.Empty<MethodParameter>(), "Vector3", "获取位置");
        yield return Varf("Device", "localToWorldPos", new[] { new MethodParameter("局部坐标@localPos", "Vector3", Alias: "localPos") }, "Vector3", "局部坐标转世界坐标", notes: "结果受零件缩放和机械缩放影响，缩放倍数会体现在坐标差中");
        yield return Varf("Device", "worldToLocalPos", new[] { new MethodParameter("世界坐标@worldPos", "Vector3", Alias: "worldPos") }, "Vector3", "世界坐标转局部坐标", notes: "结果受零件缩放和机械缩放影响");
        yield return Varf("Device", "localToWorldDir", new[] { new MethodParameter("局部方向@localDir", "Vector3", Alias: "localDir") }, "Vector3", "局部方向转世界方向");
        yield return Varf("Device", "worldToLocalDir", new[] { new MethodParameter("世界方向@worldDir", "Vector3", Alias: "worldDir") }, "Vector3", "世界方向转局部方向");

        // 状态
        yield return Varf("Device", "mech", Array.Empty<MethodParameter>(), "Mech", "获取所属机械");
        yield return Varf("Device", "exist", Array.Empty<MethodParameter>(), "Bool", "是否存在");
        yield return Varf("Device", "hp", Array.Empty<MethodParameter>(), "Float", "获取生命值", notes: "enableWholeHP 下所有零件共享同一血量，此时 hp/setHP 操作的是全机械血量");
        yield return Varf("Device", "setHP", new[] { new MethodParameter(string.Empty, "Float", Alias: "hp") }, null, "设置生命值", notes: "HP<=0 且非无敌时触发死亡逻辑；HP>MaxHP 时自动扩展 MaxHP；enableWholeHP 下操作全机械血量");

        // 物理
        yield return Varf("Device", "velocity", Array.Empty<MethodParameter>(), "Vector3", "获取速度", notes: "粘连组级别");
        yield return Varf("Device", "setVelocity", new[] { new MethodParameter("速度@velocity", "Vector3", Alias: "velocity") }, null, "设置速度", notes: "粘连组级别");
        yield return Varf("Device", "angularVelocity", Array.Empty<MethodParameter>(), "Vector3", "获取角速度", notes: "粘连组级别");
        yield return Varf("Device", "setAngularVelocity", new[] { new MethodParameter("速度@velocity", "Vector3", Alias: "velocity") }, null, "设置角速度", notes: "粘连组级别");
        yield return Varf("Device", "addForce", new[] { new MethodParameter("力@force", "Vector3", Alias: "force"), new MethodParameter("类型@type", "Enum", EnumType: ForceMode, Alias: "type") }, null, "施加推力", notes: "瞬时力，每次调用在该物理帧累加；力/加速度模式会做帧率补偿");
        yield return Varf("Device", "AddForceAtPos", new[] { new MethodParameter("力@force", "Vector3", Alias: "force"), new MethodParameter("世界坐标@Pos", "Vector3", Alias: "Pos"), new MethodParameter("类型@type", "Enum", EnumType: ForceMode, Alias: "type") }, null, "在指定位置施加推力");
        yield return Varf("Device", "addTorque", new[] { new MethodParameter("力@force", "Vector3", Alias: "force"), new MethodParameter("类型@type", "Enum", EnumType: ForceMode, Alias: "type") }, null, "施加转动力");
        yield return Varf("Device", "addTorqueTo", new[]
        {
            new MethodParameter("大小@size", "Float", Alias: "size"),
            new MethodParameter("类型@type", "Enum", EnumType: ForceMode, Alias: "type"),
            new MethodParameter("当前方向@currDir", "Vector3", Alias: "currDir"),
            new MethodParameter("目标方向@targetDir", "Vector3", Alias: "targetDir"),
            new MethodParameter("匀速@uniform", "Bool", Alias: "uniform")
        }, null, "向目标方向施加转动力", notes: "当前方向和目标方向不能为零向量；uniform=true 时匀速转动，false 时与角度成比例");
        yield return Varf("Device", "addConstForce", new[] { new MethodParameter("力@force", "Vector3", Alias: "force"), new MethodParameter("局部力@force", "Vector3", Alias: "force2"), new MethodParameter("转动力@Torque", "Vector3", Alias: "Torque"), new MethodParameter("局部转动力@RelativeTorque", "Vector3", Alias: "RelativeTorque") }, null, "持续作用力", notes: "每次调用替换而非累加，同一零件仅保留最后一次设置的值");
        yield return Varf("Device", "setDrag", new[] { new MethodParameter("阻力@drag", "Float", Alias: "drag") }, null, "设置阻力", notes: "粘连组级别");
        yield return Varf("Device", "setAngularDrag", new[] { new MethodParameter("阻力@drag", "Float", Alias: "drag") }, null, "设置转动阻力", notes: "粘连组级别");
        yield return Varf("Device", "SetMaxAngularVelocity", new[] { new MethodParameter("最大转速@v", "Float", Alias: "v") }, null, "设置最大转速", notes: "粘连组级别");
        yield return Varf("Device", "setMass", new[] { new MethodParameter("质量@mass", "Float", Alias: "mass") }, null, "设置质量", notes: "粘连组级别，直接设置粘连组总质量");
        yield return Varf("Device", "SetMass", new[] { new MethodParameter("重量@mass", "Float", Alias: "mass") }, null, "设置质量", notes: "粘连组级别，直接设置粘连组总质量");
        yield return Varf("Device", "getMass", Array.Empty<MethodParameter>(), "Float", "获取重量", notes: "粘连组级别，返回粘连组总质量");
        yield return Varf("Device", "SetDensity", new[] { new MethodParameter("密度@density", "Float", Alias: "density") }, null, "设置密度", notes: "粘连组级别；密度必须大于 0；2D 模式不支持；默认密度为 1，基于当前碰撞体体积重算质量，可用于缩放后修正质量；手动指定质量时不需要调用此方法");
        yield return Varf("Device", "SetPhysicsMaterial", new[] { new MethodParameter("弹性@bounciness", "Float", Alias: "bounciness"), new MethodParameter("动摩擦@dynamicFriction", "Float", Alias: "dynamicFriction"), new MethodParameter("静摩擦@staticFriction", "Float", Alias: "staticFriction") }, null, "设置物理材质");
        yield return Varf("Device", "massCenter", Array.Empty<MethodParameter>(), "Vector3", "获取重心", notes: "粘连组级别");
        yield return Varf("Device", "setMassCenter", new[] { new MethodParameter("世界坐标@pos", "Vector3", Alias: "pos") }, null, "设置重心", notes: "粘连组级别");
        yield return Varf("Device", "autoSetMassCenter", Array.Empty<MethodParameter>(), null, "自动调整重心", notes: "粘连组级别");
        yield return Varf("Device", "getInertiaTensor", Array.Empty<MethodParameter>(), "Vector3", "获取转动惯量", notes: "粘连组级别");
        yield return Varf("Device", "SetInertiaTensor", new[] { new MethodParameter("转动惯量@inertiaTensor", "Vector3", Alias: "inertiaTensor") }, null, "设置转动惯量", notes: "粘连组级别");
        yield return Varf("Device", "switchCollider", new[] { new MethodParameter("开关@OnOff", "Bool", Alias: "OnOff") }, null, "开关碰撞体");
        yield return Varf("Device", "DisableCollideEvent", Array.Empty<MethodParameter>(), null, "关闭碰撞事件", notes: "碰撞仍生效，仅关闭该零件的碰撞事件通知");
        yield return Varf("Device", "SetColliderSize", new[] { new MethodParameter("尺寸@Size", "Vector3", Alias: "Size"), new MethodParameter("局部坐标@Pos", "Vector3", Alias: "Pos") }, null, "设置碰撞体尺寸");
        yield return Varf("Device", "addFixedJoint", new[] { new MethodParameter("连接目标", "Device"), new MethodParameter("断开力", "Float") }, null, "添加固定关节", notes: "粘连组级别；支持跨机械连接；断开力默认 -1 即永不断开，大于 0 时受力超过此值关节断开");
        yield return Varf("Device", "breakFixedJoint", Array.Empty<MethodParameter>(), null, "断开固定关节", notes: "粘连组级别；仅移除粘连组上的第一个固定关节");
        yield return Varf("Device", "setLayer", new[] { new MethodParameter(string.Empty, "Float", Alias: "layer") }, null, "设置层", notes: "取值范围 0~23");
        yield return Varf("Device", "getLayer", Array.Empty<MethodParameter>(), "Long", "获取层");

        // 比较
        yield return Varf("Device", "equal", new[] { new MethodParameter("零件@Device", "Device", Alias: "Device") }, "Bool", "是否相同");

        // 基础功能
        yield return Varf("Device", "player", Array.Empty<MethodParameter>(), "Player", "获取控制玩家");
        yield return Varf("Device", "logicName", Array.Empty<MethodParameter>(), "String", "获取逻辑别名");
        yield return Varf("Device", "type", Array.Empty<MethodParameter>(), "String", "获取零件类型");
        yield return Varf("Device", "saveIndex", Array.Empty<MethodParameter>(), "Float", "获取零件 ID");
        yield return Varf("Device", "show", new[] { new MethodParameter(string.Empty, "Bool", Alias: "visible") }, null, "显示隐藏", notes: "仅控制渲染可见性，不影响物理、碰撞和附属UI");
        yield return Varf("Device", "del", Array.Empty<MethodParameter>(), null, "删除零件", notes: "核心零件、被发射机械、装备禁止删除");
        yield return Varf("Device", "haveMethod", new[] { new MethodParameter("方法名@methodName", "String", Alias: "methodName", IsScriptMethodName: true) }, "Bool", "是否存在(自定义)方法");
        yield return Varf("Device", "setDeadDestroyTime", new[] { new MethodParameter(string.Empty, "Float", Alias: "time") }, null, "设置死亡回收时间");
        yield return Varf("Device", "setPosKinematic", new[] { new MethodParameter("坐标@pos", "Vector3", Alias: "pos") }, null, "强制移动", notes: "粘连组级别；操作粘连组而非机械根节点，不要与 Mech.move 混用；刚由 createAIMech/createPlayerMech 返回的新机械不要在同一调用栈用此方法二次改位置，初始坐标应写在创建参数中");
        yield return Varf("Device", "setRotKinematic", new[] { new MethodParameter("向前方向@forword", "Vector3", Alias: "forword"), new MethodParameter("向上方向@up", "Vector3", Alias: "up") }, null, "强制设置朝向", notes: "粘连组级别；操作粘连组而非机械根节点，不要与 Mech.rot 混用；方向不能为零向量；刚由 createAIMech/createPlayerMech 返回的新机械不要在同一调用栈用此方法二次改朝向，初始朝向应写在创建参数中");
        yield return Varf("Device", "SetKinematic", new[] { new MethodParameter("开关@OnOff", "Bool", Alias: "OnOff") }, null, "开关零件物理", notes: "粘连组级别；true 为运动学模式（不受物理力影响），false 为动力学模式");
        yield return Varf("Device", "SetScale", new[] { new MethodParameter("缩放@scale", "Float", Alias: "scale") }, null, "缩放", notes: "绝对值而非增量，不要重复设置叠加；活零件会同步等比例缩放同一粘连组的全部零件，死亡零件仅缩放自身；与 Mech.scale 独立，零件的世界缩放为两者之积；缩放不改变质量，物理尺寸变但质量不变，如需保持密度一致请缩放后调用 SetDensity");

        // 材质与着色器
        const string shaderMaterialPropertiesNote = "不是标准 Shader 属性设置接口；只能按平台材质属性表中列出的属性名和类型调用，属性名区分大小写。属性表查 machinist Skill: references/tips/shader-material-properties.md";
        yield return Varf("Device", "setDynamicShader", new[] { new MethodParameter(string.Empty, "String", Alias: "shader") }, null, "设置 Shader", notes: shaderMaterialPropertiesNote);
        yield return Varf("Device", "setDynamicMatPic", new[] { new MethodParameter("纹理名@name", "String", Alias: "name"), new MethodParameter("贴图文件@filename", "String", Alias: "filename") }, null, "设置材质贴图参数", notes: shaderMaterialPropertiesNote + "；" + ResourceFileNameNote);
        yield return Varf("Device", "setDynamicMatURLPic", new[] { new MethodParameter("纹理名@name", "String", Alias: "name"), new MethodParameter("贴图网址@URL", "String", Alias: "URL") }, null, "下载并设置材质贴图", notes: shaderMaterialPropertiesNote);
        yield return Varf("Device", "setDynamicMatFloat", new[] { new MethodParameter("属性名@name", "String", Alias: "name"), new MethodParameter("数值@value", "Float", Alias: "value") }, null, "设置材质小数参数", notes: shaderMaterialPropertiesNote);
        yield return Varf("Device", "setDynamicMatVector", new[] { new MethodParameter("属性名@name", "String", Alias: "name"), new MethodParameter("数值@value", "Vector3", Alias: "value") }, null, "设置材质向量参数", notes: shaderMaterialPropertiesNote);
        yield return Varf("Device", "setMatCustomColor", new[] { new MethodParameter("属性名@name", "String", Alias: "name"), new MethodParameter("颜色@value", "Color", Alias: "value") }, null, "设置材质颜色参数", notes: shaderMaterialPropertiesNote);

        // 动态调用方法 (0-10 参数)
        CompressionHint callFunHint = new(CompressionKind.NumericSuffix, "callFun", Array.Empty<string>());
        for (int i = 0; i <= 10; i++)
        {
            MethodParameter[] parameters = new MethodParameter[i + 1];
            parameters[0] = new MethodParameter("方法名@methodName", "String", Alias: "methodName", IsScriptMethodName: true);
            for (int j = 0; j < i; j++)
            {
                int suffix = j + 1;
                parameters[suffix] = new MethodParameter("参数" + BuildIndexedAnyParamRawName(j), "Any", Alias: $"param{suffix}");
            }

            yield return Varf("Device", $"callFun{i}", parameters, null, $"调用方法（{i}参数）", compressionHint: callFunHint, asyncMode: SdkAsyncMode.TsAsync);
        }
        CompressionHint laterCallFunHint = new(CompressionKind.NumericSuffix, "laterCallFun", Array.Empty<string>());
        for (int i = 0; i <= 10; i++)
        {
            MethodParameter[] parameters = new MethodParameter[i + 2];
            parameters[0] = new MethodParameter("方法名@methodName", "String", Alias: "methodName", IsScriptMethodName: true);
            parameters[1] = new MethodParameter("延迟@Delay", "Float", Alias: "Delay");
            for (int j = 0; j < i; j++)
            {
                int suffix = j + 1;
                parameters[j + 2] = new MethodParameter("参数" + BuildIndexedAnyParamRawName(j), "Any", Alias: $"param{suffix}");
            }

            yield return Varf("Device", $"laterCallFun{i}", parameters, null, $"延迟调用方法（{i}参数）", compressionHint: laterCallFunHint);
        }
        CompressionHint netCallFunHint = new(CompressionKind.NumericSuffix, "netCallFun", Array.Empty<string>());
        for (int i = 0; i <= 10; i++)
        {
            MethodParameter[] parameters = new MethodParameter[i + 1];
            parameters[0] = new MethodParameter("方法名@methodName", "String", Alias: "methodName", IsScriptMethodName: true);
            for (int j = 0; j < i; j++)
            {
                int suffix = j + 1;
                parameters[suffix] = new MethodParameter("参数" + BuildIndexedAnyParamRawName(j), "Any", Alias: $"param{suffix}");
            }

            yield return Varf("Device", $"netCallFun{i}", parameters, null, $"网络调用方法（{i}参数）", compressionHint: netCallFunHint);
        }
        CompressionHint unreliableCallFunHint = new(CompressionKind.NumericSuffix, "unReliableNetCallFun", Array.Empty<string>());
        for (int i = 0; i <= 10; i++)
        {
            MethodParameter[] parameters = new MethodParameter[i + 1];
            parameters[0] = new MethodParameter("方法名@methodName", "String", Alias: "methodName", IsScriptMethodName: true);
            for (int j = 0; j < i; j++)
            {
                int suffix = j + 1;
                parameters[suffix] = new MethodParameter("参数" + BuildIndexedAnyParamRawName(j), "Any", Alias: $"param{suffix}");
            }

            yield return Varf("Device", $"unReliableNetCallFun{i}", parameters, null, $"不可靠网络调用（{i}参数）", compressionHint: unreliableCallFunHint);
        }

        // 动态调用返回值方法 - 各类型
        string[] deviceReturnTypes = DeviceCallReturnTypeSpecs
            .Select(static x => x.ReturnType)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        CompressionHint callReturnHint = new(CompressionKind.TypeSuffixWithNumeric, "callReturn", deviceReturnTypes);
        foreach ((string? retType, string? nameSuffix) in DeviceCallReturnTypeSpecs)
        {
            for (int i = 0; i <= 10; i++)
            {
                MethodParameter[] parameters = new MethodParameter[i + 1];
                parameters[0] = new MethodParameter("方法名@methodName", "String", Alias: "methodName", IsScriptMethodName: true);
                for (int j = 0; j < i; j++)
                {
                    int suffix = j + 1;
                    parameters[suffix] = new MethodParameter("参数" + BuildIndexedAnyParamRawName(j), "Any", Alias: $"param{suffix}");
                }

                yield return Varf(
                    "Device",
                    $"callReturn{nameSuffix}{i}",
                    parameters,
                    retType,
                    $"调用返回 {retType} 方法（{i}参数）",
                    compressionHint: callReturnHint,
                    asyncMode: SdkAsyncMode.TsAsync);
            }
        }

    }

    private static IEnumerable<MethodSignature> GetMechMethods()
    {
        // 基本信息
        yield return Varf("Mech", "name", Array.Empty<MethodParameter>(), "String", "获取机械名称");
        yield return Varf("Mech", "isSame", new[] { new MethodParameter(string.Empty, "Mech", Alias: "other") }, "Bool", "是否同一机械");
        yield return Varf("Mech", "isAI", Array.Empty<MethodParameter>(), "Bool", "是否 AI 机械");
        yield return Varf("Mech", "isMap", Array.Empty<MethodParameter>(), "Bool", "是否地图机械");
        yield return Varf("Mech", "isBullet", Array.Empty<MethodParameter>(), "Bool", "是否被发射机械");
        yield return Varf("Mech", "isEquipment", Array.Empty<MethodParameter>(), "Bool", "是否装备");
        yield return Varf("Mech", "alive", Array.Empty<MethodParameter>(), "Bool", "是否存活");

        // 位置与变换
        yield return Varf("Mech", "rot", new[] { new MethodParameter("向前方向@forward", "Vector3", Alias: "forward"), new MethodParameter("向上方向@up", "Vector3", Alias: "up") }, null, "旋转", notes: "操作机械根节点；不要与 Device.setRotKinematic 混用，混用会导致旋转补偿错乱；刚由 createAIMech/createPlayerMech 返回的新机械不要在同一调用栈用此方法二次改朝向，初始朝向应写在创建参数中");
        yield return Varf("Mech", "corePos", Array.Empty<MethodParameter>(), "Vector3", "获取核心位置", notes: "地图机械返回 (0,0,0) 而非抛异常");
        yield return Varf("Mech", "move", new[] { new MethodParameter("位置@pos", "Vector3", Alias: "pos"), new MethodParameter("对齐方式@alignment", "Enum", EnumType: MechPositionAlignment, Alias: "alignment") }, null, "移动机械", notes: "操作机械根节点；不要与 Device.setPosKinematic 混用，混用会导致位置补偿错乱；刚由 createAIMech/createPlayerMech 返回的新机械不要在同一调用栈用此方法二次改位置，初始坐标应写在创建参数中");
        yield return Varf("Mech", "scale", new[] { new MethodParameter("比例@scale", "Float", Alias: "scale") }, null, "缩放机械", notes: "绝对值而非增量，不要重复设置叠加；缩放整个机械，与 Device.SetScale 独立，零件的世界缩放为两者之积；缩放不改变质量");

        // 零件管理
        yield return Varf("Mech", "getDevice", new[] { new MethodParameter("零件序号@deviceID", "Float", Alias: "deviceID") }, "Device", "按序号获取零件", notes: "对非地图机械，序号 0 等价于 getCoreDevice；序号从 0 开始，超出范围抛异常");
        yield return Varf("Mech", "getCoreDevice", Array.Empty<MethodParameter>(), "Device", "获取核心零件");
        yield return Varf("Mech", "deviceNum", Array.Empty<MethodParameter>(), "Float", "获取零件数量");
        yield return Varf("Mech", "getGroupDevices", new[] { new MethodParameter("路径@path", "String", Alias: "path") }, "List", "获取分组零件列表");
        yield return Varf("Mech", "findDevice", new[] { new MethodParameter("别名@LogicName", "String", Alias: "LogicName") }, "Device", "按类型查找零件", notes: "找不到时返回 null 而非抛异常，使用返回值前应检查");
        yield return Varf("Mech", "getGenerator", Array.Empty<MethodParameter>(), "Device", "获取生成器零件", notes: "仅对被发射机械返回发射器零件、对装备返回装备挂载点；其他类型机械调用抛异常");
        yield return Varf("Mech", "getWeight", Array.Empty<MethodParameter>(), "Float", "获取总重量", notes: "返回全机械总质量×100，与 Device.getMass 不同");

        // 玩家与阵营
        yield return Varf("Mech", "getPlayer", Array.Empty<MethodParameter>(), "Player", "获取控制玩家", notes: "地图机械和 AI 机械返回 null，使用返回值前应检查");
        yield return Varf("Mech", "IsLocalPlayerControl", Array.Empty<MethodParameter>(), "Bool", "是否本地玩家控制");
        yield return Varf("Mech", "team", Array.Empty<MethodParameter>(), "Float", "获取阵营");
        yield return Varf("Mech", "setTeam", new[] { new MethodParameter("阵营@team", "Float", Alias: "team") }, null, "设置阵营");

        // 物理
        yield return Varf("Mech", "enablePhysics", new[] { new MethodParameter(string.Empty, "Bool", Alias: "enabled") }, null, "开关物理模拟", notes: "影响机械内所有粘连组，会覆盖 Device.SetKinematic 的单个设置");
        yield return Varf("Mech", "enableGravity", new[] { new MethodParameter(string.Empty, "Bool", Alias: "enabled") }, null, "开关重力", notes: "影响机械内所有粘连组");
        yield return Varf("Mech", "setDrag", new[] { new MethodParameter(string.Empty, "Float", Alias: "drag") }, null, "设置空气阻力", notes: "影响机械内所有粘连组，会覆盖 Device.setDrag 的单个设置");
        yield return Varf("Mech", "getDrag", Array.Empty<MethodParameter>(), "Float", "获取空气阻力");
        yield return Varf("Mech", "setAngularDrag", new[] { new MethodParameter(string.Empty, "Float", Alias: "angularDrag") }, null, "设置转动阻力", notes: "影响机械内所有粘连组，会覆盖 Device.setAngularDrag 的单个设置");
        yield return Varf("Mech", "setPhysicsMaterial", new[] { new MethodParameter("弹性@bounciness", "Float", Alias: "bounciness"), new MethodParameter("动摩擦@dynamicFriction", "Float", Alias: "dynamicFriction"), new MethodParameter("静摩擦@staticFriction", "Float", Alias: "staticFriction") }, null, "设置物理材质", notes: "影响机械内所有零件，会覆盖 Device.SetPhysicsMaterial 的单个设置");
        yield return Varf("Mech", "enableSelfCollide", new[] { new MethodParameter(string.Empty, "Bool", Alias: "enabled") }, null, "开关自碰撞");
        yield return Varf("Mech", "enableCollideWithOther", new[] { new MethodParameter(string.Empty, "Mech", Alias: "other"), new MethodParameter(string.Empty, "Bool", Alias: "enabled") }, null, "开关与其他机械碰撞");

        // 状态控制
        yield return Varf("Mech", "setActive", new[] { new MethodParameter(string.Empty, "Bool", Alias: "active") }, null, "设置激活状态", notes: "覆盖：图形渲染、物理计算、装备等子机械对象、附属UI");
        yield return Varf("Mech", "setLayer", new[] { new MethodParameter(string.Empty, "Float", Alias: "layer") }, null, "设置层");
        yield return Varf("Mech", "enableWholeHP", new[] { new MethodParameter(string.Empty, "Bool", Alias: "enabled") }, null, "开关整体血量");
        yield return Varf("Mech", "enableDeviceBreak", new[] { new MethodParameter(string.Empty, "Bool", Alias: "enabled") }, null, "开关零件破坏");
        yield return Varf("Mech", "enableDropDeviceDie", new[] { new MethodParameter(string.Empty, "Bool", Alias: "enabled") }, null, "开关掉落零件死亡");
        yield return Varf("Mech", "SetDieExplodeForce", new[] { new MethodParameter("爆炸中心@Center", "Vector3", Alias: "Center"), new MethodParameter("力量@", "Float") }, null, "设置死亡爆炸力");
        yield return Varf("Mech", "Del", new[] { new MethodParameter("延迟@delay", "Float", Alias: "delay") }, null, "删除机械", notes: "地图机械、被发射机械、正在销毁中的机械禁止删除；延迟≤0 时立即删除，>0 时延迟后删除");

        // 自定义数据 - 各类型
        const string mapMechCustomNote = "地图机械禁止此操作";
        string[] customTypes = new[] { "Float", "Long", "String", "Bool", "Vector3", "Color", "List", "Dict", "Struct", "Bytes", "Canvas", "Datetime", "Device", "Icon", "Mech", "Player", "UIAvatar", "UIButton", "UIInput", "UILabel", "UILine", "UIRawImage", "UISpine", "UIRect" };
        CompressionHint customHint = new(CompressionKind.TypeSuffix, "custom", customTypes);
        CompressionHint setCustomHint = new(CompressionKind.TypeSuffixParam, "setCustom", customTypes);
        foreach (string? t in customTypes)
        {
            yield return Varf("Mech", $"custom{t}", new[] { new MethodParameter("名称@name", "String", Alias: "name") }, t, $"获取自定义{t}", compressionHint: customHint, notes: mapMechCustomNote);
            yield return Varf("Mech", $"setCustom{t}", new[] { new MethodParameter("名称@name", "String", Alias: "name"), new MethodParameter("值", t) }, null, $"设置自定义{t}", compressionHint: setCustomHint, notes: mapMechCustomNote);
        }
        yield return Varf("Mech", "ustomDataExist", new[] { new MethodParameter("名称@name", "String", Alias: "name") }, "Bool", "自定义数据是否存在", notes: mapMechCustomNote);

        // 动态调用方法
        CompressionHint callFunHint = new(CompressionKind.NumericSuffix, "callFun", Array.Empty<string>());
        for (int i = 0; i <= 10; i++)
        {
            MethodParameter[] parameters = new MethodParameter[i + 1];
            parameters[0] = new MethodParameter("方法名@methodName", "String", Alias: "methodName", IsScriptMethodName: true);
            for (int j = 0; j < i; j++)
            {
                int suffix = j + 1;
                parameters[suffix] = new MethodParameter("参数" + BuildIndexedAnyParamRawName(j), "Any", Alias: $"param{suffix}");
            }

            yield return Varf("Mech", $"callFun{i}", parameters, null, $"调用核心方法（{i}参数）", compressionHint: callFunHint, asyncMode: SdkAsyncMode.TsAsync, notes: mapMechCustomNote);
        }

        // 动态调用返回值方法 - 各类型
        string[] mechReturnTypes = MechCallReturnTypeSpecs
            .Select(static x => x.ReturnType)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        CompressionHint callReturnHint = new(CompressionKind.TypeSuffixWithNumeric, "callReturn", mechReturnTypes);
        foreach ((string? retType, string? nameSuffix) in MechCallReturnTypeSpecs)
        {
            for (int i = 0; i <= 10; i++)
            {
                MethodParameter[] parameters = new MethodParameter[i + 1];
                parameters[0] = new MethodParameter("方法名@methodName", "String", Alias: "methodName", IsScriptMethodName: true);
                for (int j = 0; j < i; j++)
                {
                    int suffix = j + 1;
                    parameters[suffix] = new MethodParameter($"参数{j:0}1@param{j:0}1", "Any", Alias: $"param{suffix}");
                }

                yield return Varf(
                    "Mech",
                    $"callReturn{nameSuffix}{i}",
                    parameters,
                    retType,
                    $"调用返回 {retType} 方法（{i}参数）",
                    compressionHint: callReturnHint,
                    asyncMode: SdkAsyncMode.TsAsync,
                    notes: mapMechCustomNote);
            }
        }
    }

    private static IEnumerable<MethodSignature> GetPlayerMethods()
    {
        // 基本信息
        yield return Varf("Player", "nickName", Array.Empty<MethodParameter>(), "String", "获取玩家昵称");
        yield return Varf("Player", "desc", Array.Empty<MethodParameter>(), "String", "获取玩家简介");
        yield return Varf("Player", "id", Array.Empty<MethodParameter>(), "Long", "获取玩家 ID");
        yield return Varf("Player", "GuidID", Array.Empty<MethodParameter>(), "Long", "获取公会 ID");
        yield return Varf("Player", "socialID", Array.Empty<MethodParameter>(), "String", "获取渠道 ID", notes: "返回的是渠道标识，非玩家个体数据");
        yield return Varf("Player", "LastOnlineTime", Array.Empty<MethodParameter>(), "Datetime", "获取最近上线时间");
        yield return Varf("Player", "exist", Array.Empty<MethodParameter>(), "Bool", "是否存在");
        yield return Varf("Player", "isSame", new[] { new MethodParameter(string.Empty, "Player", Alias: "other") }, "Bool", "是否同一玩家");

        // 机械与阵营
        yield return Varf("Player", "controlledMech", Array.Empty<MethodParameter>(), "Mech", "获取控制的机械");
        yield return Varf("Player", "team", Array.Empty<MethodParameter>(), "Float", "获取阵营");
        yield return Varf("Player", "setTeam", new[] { new MethodParameter("阵营序号@teamID", "Float", Alias: "teamID") }, null, "设置阵营");

        // 玩家数据
        yield return Varf("Player", "existPlayerData", new[] { new MethodParameter("名称@name", "String", Alias: "name") }, "Bool", "是否存在玩家数据");

        // 玩家数据读写 - 各类型
        string[] playerDataTypes = new[] { "Float", "Long", "String", "Bool", "Vector3", "Color", "List", "Dict", "Struct", "Bytes", "Canvas", "Datetime", "Device", "Icon", "Mech", "Player", "UIAvatar", "UIButton", "UIInput", "UILabel", "UILine", "UIRawImage", "UISpine", "UIRect" };
        CompressionHint playerDataHint = new(CompressionKind.TypeSuffix, "playerData", playerDataTypes);
        CompressionHint setPlayerDataHint = new(CompressionKind.TypeSuffixParam, "setPlayerData", playerDataTypes);
        foreach (string? t in playerDataTypes)
        {
            yield return Varf("Player", $"playerData{t}", new[] { new MethodParameter("名称@name", "String", Alias: "name") }, t, $"获取玩家数据{t}", compressionHint: playerDataHint, notes: "找不到该名称的数据时不抛异常，返回值保持默认");
            yield return Varf("Player", $"setPlayerData{t}", new[] { new MethodParameter("名称@name", "String", Alias: "name"), new MethodParameter("值", t) }, null, $"设置玩家数据{t}", compressionHint: setPlayerDataHint);
        }
    }

    private static IEnumerable<MethodSignature> GetCanvasMethods()
    {
        yield return Varf("Canvas", "ref", Array.Empty<MethodParameter>(), "Canvas", "引用");
        yield return Varf("Canvas", "breakLink", Array.Empty<MethodParameter>(), null, "断开全部 UI", notes: "断开后 getAllUIPaths 和 read 系列方法仍正常工作");
        yield return Varf("Canvas", "exist", Array.Empty<MethodParameter>(), "Bool", "是否存在", notes: "判断 Canvas 是否已赋值而非 UI 是否可见；通过 create.canvas() 赋值后恒为 true");
        yield return Varf("Canvas", "getAllUIPaths", Array.Empty<MethodParameter>(), "List", "得到全部 UI 路径", notes: "完全静态，仅取决于布局文件本身的形状，是扁平集合而非树结构；不会因添加子 UI 等操作增加，但运行时删除 UI 会减少");

        // 读取各类型 UI
        string[] uiTypes = new[] { "UIRect", "UIInput", "UILabel", "UIButton", "UIRawImage", "UISpine" };
        CompressionHint readHint = new(CompressionKind.TypeSuffix, "read", uiTypes);
        foreach (string? uiType in uiTypes)
        {
            yield return Varf("Canvas", $"read{uiType}", new[] { new MethodParameter("路径@Path", "String", Alias: "Path") }, uiType, $"读取{uiType}", compressionHint: readHint, notes: "路径必须存在于布局文件中，不存在或类型不匹配时报错；路径格式为 / 分隔的层级名称");
        }
    }

    /// <summary>UI 控件通用方法（适用于所有 UI 类型）</summary>
    private static IEnumerable<MethodSignature> GetUICommonMethods()
    {
        string[] uiTypes = new[] { "UI" };
        string[] customTypes = new[]
        {
            "Float", "Vector3", "Bool", "Long", "String", "List", "Dict", "Struct",
            "Device", "Mech", "Player", "Color", "UIRect", "UIInput", "UILabel", "UIButton",
            "UIRawImage", "UISpine", "UIAvatar", "Icon", "UILine", "Canvas", "Datetime", "Bytes"
        };

        foreach (string? uiType in uiTypes)
        {
            // 坐标与位置
            yield return Varf(uiType, "pos", new[] { new MethodParameter("坐标@pos", "Vector3", Alias: "pos") }, null, "设置坐标", notes: "输入为 0~1 屏幕坐标，相对全屏 Canvas，中心点 (0.5, 0.5)。不是屏幕像素，也不是 setCanvasPos 使用的布局坐标。");
            yield return Varf(uiType, "getPos", Array.Empty<MethodParameter>(), "Vector3", "获取坐标", notes: "返回 0~1 屏幕坐标，相对全屏 Canvas。不是屏幕像素，也不是 getCanvasPos 返回的布局坐标。");
            yield return Varf(uiType, "setCanvasPos", new[] { new MethodParameter("坐标@pos", "Vector3", Alias: "pos") }, null, "设置布局坐标", notes: "直接设置 RectTransform.anchoredPosition，单位是 UI 布局坐标，受 parentAnchor/父节点尺寸影响；不是 0~1 屏幕坐标，也不是 Screen.width/height 像素坐标。");
            yield return Varf(uiType, "getCanvasPos", Array.Empty<MethodParameter>(), "Vector3", "获取布局坐标", notes: "返回 RectTransform.anchoredPosition，单位是 UI 布局坐标；不是 0~1 屏幕坐标，也不是 Screen.width/height 像素坐标。");
            yield return Varf(uiType, "setPixelPos", new[] { new MethodParameter("分辨率坐标@pos", "Vector3", Alias: "pos"), new MethodParameter("对齐方式@alignment", "Enum", EnumType: UiPixelAlignment, Alias: "alignment") }, null, "设置像素坐标", notes: "输入为 Screen.width/height 像素坐标；内部先归一化为 0~1 屏幕坐标，再按 alignment 转为 UI 位置。");

            // 大小
            yield return Varf(uiType, "size", new[] { new MethodParameter("大小@size", "Vector3", Alias: "size") }, null, "设置大小", notes: "size 为 UI 布局尺寸，写入 RectTransform 尺寸或按当前扩展模式换算；不是屏幕分辨率像素。");
            yield return Varf(uiType, "getSize", Array.Empty<MethodParameter>(), "Vector3", "获取大小", notes: "返回 UI 布局尺寸，即 RectTransform.rect.size；不是 Screen.width/height 像素尺寸。");

            // 显示控制
            yield return Varf(uiType, "active", new[] { new MethodParameter("开关@OnOff", "Bool", Alias: "OnOff") }, null, "设置显示状态");
            yield return Varf(uiType, "isActive", Array.Empty<MethodParameter>(), "Bool", "获取显示状态");
            yield return Varf(uiType, "activeInHierarchy", Array.Empty<MethodParameter>(), "Bool", "获取层级显示状态", notes: "自身及所有父级都 active 时才为 true");

            // 颜色与样式
            yield return Varf(uiType, "setColor", new[] { new MethodParameter("颜色@color", "Color", Alias: "color") }, null, "设置颜色", notes: "仅影响自身，不影响子控件");
            yield return Varf(uiType, "shadow", new[] { new MethodParameter("偏移@offset", "Vector3", Alias: "offset"), new MethodParameter("颜色@color", "Color", Alias: "color") }, null, "添加阴影");
            yield return Varf(uiType, "outline", new[] { new MethodParameter("偏移@offset", "Float", Alias: "offset"), new MethodParameter("颜色@color", "Color", Alias: "color") }, null, "添加描边");

            // 旋转与变换
            yield return Varf(uiType, "rot", new[] { new MethodParameter("角度@angle", "Float", Alias: "angle") }, null, "设置 2D 旋转", notes: "与 Canvas 静态布局属性 rot 等价，都设置绕 Z 轴的 2D 旋转角（度，正向逆时针）；绝对值非增量，覆盖既有旋转");
            yield return Varf(uiType, "rot3D", new[] { new MethodParameter(string.Empty, "Vector3", Alias: "rotation") }, null, "设置 3D 旋转", notes: "2D UI也支持3D旋转，但可能部分被遮挡");
            yield return Varf(uiType, "rotation", new[] { new MethodParameter("向前@forward", "Vector3", Alias: "forward"), new MethodParameter("向上@up", "Vector3", Alias: "up") }, null, "设置旋转（角度）");
            yield return Varf(uiType, "scale", new[] { new MethodParameter("比例@scale", "Float", Alias: "scale") }, null, "设置缩放", notes: "设置当前 UI 的变换缩放。已有子 UI 会随父级变换一起缩放；动态 addSon 挂入的子 UI 会保留当时的世界变换，之后按子 UI 自身缩放与父级缩放的相对关系叠加。");

            // 布局与锚点
            yield return Varf(uiType, "alignment", new[] { new MethodParameter("对齐方式@alignment", "Enum", EnumType: UiAlignment, Alias: "alignment") }, null, "设置对齐方式");
            yield return Varf(uiType, "parentAnchor", new[] { new MethodParameter("对齐方式@alignment", "Enum", EnumType: UiAlignment, Alias: "alignment") }, null, "设置父锚点");

            // 层级管理
            yield return Varf(uiType, "breakLink", Array.Empty<MethodParameter>(), null, "断开父关联", notes: "断开后再次调用创建方法会生成新 UI 实例，而非复用当前实例；不销毁当前 UI");
            yield return Varf(uiType, "addSon", new[] { new MethodParameter(string.Empty, "UI", Alias: "child") }, null, "添加子控件", notes: "已是子控件时幂等，不重复添加;可更换父级;只改变运行时UI实例的父子关系，不影响Canvas静态成员集合;动态挂到父 UI 下时保留子 UI 当前世界变换，之后按子 UI 自身缩放与父级缩放的相对关系叠加。");
            yield return Varf(uiType, "sonNum", Array.Empty<MethodParameter>(), "Float", "获取子控件数量");
            yield return Varf(uiType, "getSonUIButton", new[] { new MethodParameter("路径@path", "String", Alias: "path") }, "UIButton", "获取子控件（UIButton）", notes: "路径为 / 分隔的层级名称，找不到或类型不匹配时抛异常;注意与Canvas一侧的静态路径不同");
            yield return Varf(uiType, "getSonUIInput", new[] { new MethodParameter("路径@path", "String", Alias: "path") }, "UIInput", "获取子控件（UIInput）", notes: "路径为 / 分隔的层级名称，找不到或类型不匹配时抛异常;注意与Canvas一侧的静态路径不同");
            yield return Varf(uiType, "getSonUILabel", new[] { new MethodParameter("路径@path", "String", Alias: "path") }, "UILabel", "获取子控件（UILabel）", notes: "路径为 / 分隔的层级名称，找不到或类型不匹配时抛异常;注意与Canvas一侧的静态路径不同");
            yield return Varf(uiType, "getSonUIRawImage", new[] { new MethodParameter("路径@path", "String", Alias: "path") }, "UIRawImage", "获取子控件（UIRawImage）", notes: "路径为 / 分隔的层级名称，找不到或类型不匹配时抛异常;注意与Canvas一侧的静态路径不同");
            yield return Varf(uiType, "getSonUIRect", new[] { new MethodParameter("路径@path", "String", Alias: "path") }, "UIRect", "获取子控件（UIRect）", notes: "路径为 / 分隔的层级名称，找不到或类型不匹配时抛异常;注意与Canvas一侧的静态路径不同");
            yield return Varf(uiType, "setSiblingIndex", new[] { new MethodParameter(string.Empty, "Float", Alias: "index") }, null, "设置排序位置");
            yield return Varf(uiType, "moveToTop", Array.Empty<MethodParameter>(), null, "移动到目标上方");

            // 生命周期
            yield return Varf(uiType, "del", new[] { new MethodParameter("延迟@Delay", "Float", Alias: "Delay") }, null, "删除控件", notes: "删除自身及所有子控件；延迟 ≤0 立即删除");
            yield return Varf(uiType, "delSon", new[] { new MethodParameter("序号@index", "Float", Alias: "index") }, null, "删除子控件");
            yield return Varf(uiType, "delAllChild", Array.Empty<MethodParameter>(), null, "删除所有子控件");
            yield return Varf(uiType, "exist", Array.Empty<MethodParameter>(), "Bool", "是否存在", notes: "判断 UI 是否已被删除，与显示/隐藏状态无关");
            yield return Varf(uiType, "equal", new[] { new MethodParameter("UI", "UI") }, "Bool", "是否相同");
            yield return Varf(uiType, "copy", Array.Empty<MethodParameter>(), "UI", "获取引用副本", notes: "返回变量与原变量指向同一 UI 控件，对任一方的修改直接影响另一方");
            yield return Varf(uiType, "loopCall", new[] { new MethodParameter("方法名", "String", IsScriptMethodName: true) }, null, "每帧循环", notes: "回调方法的参数是这个 UI 本身");

            // 交互与跟随
            yield return Varf(uiType, "followDevice", new[] { new MethodParameter("跟随目标@device", "Device", Alias: "device"), new MethodParameter("位置偏移@offset", "Vector3", Alias: "offset"), new MethodParameter("摄像机序号@camIndex", "Float", Alias: "camIndex") }, null, "跟随零件", notes: "空零件(常量或变量)时跟随世界坐标;计算出的是2D坐标,Z值恒定,3D UI在跟随时同样按2D坐标计算,Z轴锁定不动;跟随零件被禁用全部功能则UI隐藏,被删除则UI也被删除;UI可能移到屏幕外面(不同于世界坐标转屏幕坐标会确保UI在屏幕内)");
            yield return Varf(uiType, "setOwner", new[] { new MethodParameter("目标@VOwner", "Device", Alias: "VOwner") }, null, "设置所有者", notes: "只能设置一次，与 followDevice 互斥；所有者零件被销毁时 UI 也被删除");
            yield return Varf(uiType, "isPosInside", new[] { new MethodParameter("坐标@pos", "Vector3", Alias: "pos") }, "Bool", "坐标是否在内部", notes: "输入为 0~1 屏幕坐标，不是像素坐标，也不是布局坐标。mouse.pos 返回像素坐标，需先用 screen.fromPixel 转换。");
            yield return Varf(uiType, "posToLocal", new[] { new MethodParameter("坐标@pos", "Vector3", Alias: "pos") }, "Vector3", "屏幕坐标转内部坐标", notes: "输入为 0~1 屏幕坐标，不是像素坐标，也不是布局坐标；返回当前 UI 的局部坐标，单位是 UI 内部坐标，不是 0~1 比例。mouse.pos 返回像素坐标，需先用 screen.fromPixel 转换。");

            // 自定义数据 - 各类型（来自 UIVar）
            CompressionHint customHint = new(CompressionKind.TypeSuffix, "custom", customTypes);
            CompressionHint setCustomHint = new(CompressionKind.TypeSuffixParam, "setCustom", customTypes);
            foreach (string? t in customTypes)
            {
                yield return Varf(uiType, $"custom{t}", new[] { new MethodParameter("名称@name", "String", Alias: "name") }, t, $"获取自定义{t}", compressionHint: customHint);
                yield return Varf(uiType, $"setCustom{t}", new[] { new MethodParameter("名称@name", "String", Alias: "name"), new MethodParameter("值", t) }, null, $"设置自定义{t}", compressionHint: setCustomHint);
            }
        }
    }

    private static IEnumerable<MethodSignature> GetUILabelMethods()
    {
        // UILabel 专用方法 (来自 TextUIVar 和 LabelLabelVar)
        yield return Varf("UILabel", "text", new[] { new MethodParameter("文本@text", "String", AllowImplicitToString: true, Alias: "text") }, null, "设置文本");
        yield return Varf("UILabel", "textDirtyFilter", new[] { new MethodParameter("文本@text", "String", AllowImplicitToString: true, Alias: "text") }, null, "设置文本并过滤脏词", notes: "非空文本时异步过滤，文本在过滤完成后才更新");
        yield return Varf("UILabel", "floatText", new[] { new MethodParameter("小数@float", "Float", Alias: "float"), new MethodParameter("有效位数@digits", "Long", Alias: "digits") }, null, "设置浮点数文本", notes: "digits 范围 [0,5]，小数点后保留位数");
        yield return Varf("UILabel", "setWidth", new[] { new MethodParameter(string.Empty, "Float", Alias: "width") }, null, "设置宽度");
        yield return Varf("UILabel", "setFontSize", new[] { new MethodParameter(string.Empty, "Float", Alias: "fontSize") }, null, "设置字体大小", notes: "小数部分被截断为整数");
        yield return Varf("UILabel", "labelEffect", new[] { new MethodParameter("加粗@bold", "Float", Alias: "bold"), new MethodParameter("间距@spacing", "Vector3", Alias: "spacing"), new MethodParameter("风格@style", "Enum", EnumType: LabelEffectStyle, Alias: "style") }, null, "设置特效文本样式", notes: "仅 TextPro 模式可用");
        yield return Varf("UILabel", "labelShadow", new[] { new MethodParameter("颜色@color", "Color", Alias: "color"), new MethodParameter("偏移@offset", "Vector3", Alias: "offset"), new MethodParameter("软@softness", "Float", Alias: "softness") }, null, "设置阴影效果", notes: "仅 TextPro 模式可用");
        yield return Varf("UILabel", "labelLightEffect", new[] { new MethodParameter("颜色@color", "Color", Alias: "color"), new MethodParameter("角度@angle", "Float", Alias: "angle"), new MethodParameter("高光@specular", "Float", Alias: "specular"), new MethodParameter("反光@reflectivity", "Float", Alias: "reflectivity"), new MethodParameter("阴影1@diffuse", "Float", Alias: "diffuse"), new MethodParameter("阴影2@ambient", "Float", Alias: "ambient") }, null, "设置光照效果", notes: "仅 TextPro 模式可用");
        yield return Varf("UILabel", "labelBevel", new[] { new MethodParameter("力度@bevel", "Float", Alias: "bevel"), new MethodParameter("偏移@offset", "Float", Alias: "offset"), new MethodParameter("宽度@width", "Float", Alias: "width"), new MethodParameter("圆角@roundness", "Float", Alias: "roundness"), new MethodParameter("削平@clamp", "Float", Alias: "clamp") }, null, "设置斜面效果", notes: "仅 TextPro 模式可用");
        yield return Varf("UILabel", "labelGlow", new[] { new MethodParameter("颜色@color", "Color", Alias: "color"), new MethodParameter("偏@offset", "Float", Alias: "offset"), new MethodParameter("内@inner", "Float", Alias: "inner"), new MethodParameter("外@outer", "Float", Alias: "outer"), new MethodParameter("减淡@power", "Float", Alias: "power") }, null, "设置发光效果", notes: "仅 TextPro 模式可用");
        yield return Varf("UILabel", "labelGradient", new[] { new MethodParameter("左上@topLeft", "Color", Alias: "topLeft"), new MethodParameter("右上@topRight", "Color", Alias: "topRight"), new MethodParameter("左下@bottomLeft", "Color", Alias: "bottomLeft"), new MethodParameter("右下@bottomRight", "Color", Alias: "bottomRight") }, null, "设置渐变效果", notes: "仅 TextPro 模式可用");
        yield return Varf("UILabel", "getTextSize", Array.Empty<MethodParameter>(), "Vector3", "获取文本尺寸", notes: "返回 UI 布局尺寸；x 为控件宽度与文本首选宽度的较小值，y 为文本首选高度。不是屏幕像素尺寸。");
        yield return Varf("UILabel", "ref", Array.Empty<MethodParameter>(), "UILabel", "引用", notes: "返回同一个运行时 UI 控件引用，不复制 UI 实例。");
    }

    private static IEnumerable<MethodSignature> GetUIButtonMethods()
    {
        // UIButton 专用方法 (来自 TextUIVar 和 LabelButtonVar)
        yield return Varf("UIButton", "text", new[] { new MethodParameter("文本@text", "String", AllowImplicitToString: true, Alias: "text") }, null, "设置文本");
        yield return Varf("UIButton", "textDirtyFilter", new[] { new MethodParameter("文本@text", "String", AllowImplicitToString: true, Alias: "text") }, null, "设置文本并过滤脏词", notes: "非空文本时异步过滤，文本在过滤完成后才更新");
        yield return Varf("UIButton", "floatText", new[] { new MethodParameter("小数@float", "Float", Alias: "float"), new MethodParameter("有效位数@digits", "Long", Alias: "digits") }, null, "设置浮点数文本", notes: "digits 范围 [0,5]，小数点后保留位数");
        yield return Varf("UIButton", "pressing", Array.Empty<MethodParameter>(), "Bool", "是否正在按下");
        yield return Varf("UIButton", "isDown", Array.Empty<MethodParameter>(), "Bool", "是否按下", notes: "仅在按下发生的同一渲染帧为 true，下一帧恢复 false");
        yield return Varf("UIButton", "isRelease", Array.Empty<MethodParameter>(), "Bool", "是否释放", notes: "仅在释放发生的同一渲染帧为 true，下一帧恢复 false");
        yield return Varf("UIButton", "touchPos", new[] { new MethodParameter("是否局部空间@Local", "Bool", Alias: "Local") }, "Vector3", "获取触摸位置", notes: "Local=true 时返回相对按钮的归一化坐标 [0,1]，Local=false 时返回相对屏幕的归一化坐标");
        yield return Varf("UIButton", "setTextScale", new[] { new MethodParameter("比例@scaleRate", "Float", Alias: "scaleRate") }, null, "设置文本缩放", notes: "范围 (0,1]");
        yield return Varf("UIButton", "addClickCB", new[] { new MethodParameter("方法名@funcName", "String", Alias: "funcName", IsScriptMethodName: true), new MethodParameter("参数列表@paramName", "List", Alias: "paramName") }, null, "添加点击回调", notes: "不可重复注册同名方法；参数引用而非快照，回调触发时使用参数当前值");
        yield return Varf("UIButton", "addPointDownCB", new[] { new MethodParameter("方法名@funcName", "String", Alias: "funcName", IsScriptMethodName: true), new MethodParameter("参数列表@paramList", "List", Alias: "paramList") }, null, "添加按下回调", notes: "不可重复注册同名方法");
        yield return Varf("UIButton", "addPointUpCB", new[] { new MethodParameter("方法名@funcName", "String", Alias: "funcName", IsScriptMethodName: true), new MethodParameter("参数列表@paramList", "List", Alias: "paramList") }, null, "添加抬起回调", notes: "不可重复注册同名方法");
        yield return Varf("UIButton", "addPointEnterCB", new[] { new MethodParameter("方法名@funcName", "String", Alias: "funcName", IsScriptMethodName: true), new MethodParameter("参数列表@paramList", "List", Alias: "paramList") }, null, "添加进入回调", notes: "不可重复注册同名方法");
        yield return Varf("UIButton", "addPointExitCB", new[] { new MethodParameter("方法名@funcName", "String", Alias: "funcName", IsScriptMethodName: true), new MethodParameter("参数列表@paramList", "List", Alias: "paramList") }, null, "添加离开回调", notes: "不可重复注册同名方法");
        yield return Varf("UIButton", "removePointDownEvents", Array.Empty<MethodParameter>(), null, "移除按下事件", notes: "清空全部回调，不支持移除单个");
        yield return Varf("UIButton", "removePointUpEvents", Array.Empty<MethodParameter>(), null, "移除抬起事件", notes: "清空全部回调，不支持移除单个");
        yield return Varf("UIButton", "removeClickEvents", Array.Empty<MethodParameter>(), null, "移除点击事件", notes: "清空全部回调，不支持移除单个");
        yield return Varf("UIButton", "removePointEnterEvents", Array.Empty<MethodParameter>(), null, "移除进入事件", notes: "清空全部回调，不支持移除单个");
        yield return Varf("UIButton", "removePointExitEvents", Array.Empty<MethodParameter>(), null, "移除离开事件", notes: "清空全部回调，不支持移除单个");
        yield return Varf("UIButton", "enabelButton", new[] { new MethodParameter("开关@onOff", "Bool", Alias: "onOff") }, null, "开关按钮");
        yield return Varf("UIButton", "ref", Array.Empty<MethodParameter>(), "UIButton", "引用", notes: "返回同一个运行时 UI 控件引用，不复制 UI 实例。");

        // 带参数的回调方法 (0-10 参数)
        CompressionHint pointDownHint = new(CompressionKind.NumericSuffix, "PointDownCB", Array.Empty<string>());
        for (int i = 0; i <= 10; i++)
        {
            MethodParameter[] parameters = new MethodParameter[i + 1];
            parameters[0] = new MethodParameter("方法名@funcName", "String", Alias: "funcName", IsScriptMethodName: true);
            for (int j = 0; j < i; j++)
            {
                int suffix = j + 1;
                parameters[suffix] = new MethodParameter("参数" + BuildIndexedAnyParamRawName(j), "Any", Alias: $"param{suffix}");
            }

            yield return Varf("UIButton", $"PointDownCB{i}", parameters, null, $"按下回调（{i}参数）", compressionHint: pointDownHint, notes: "不可重复注册同名方法；参数在注册时深拷贝快照");
        }
        CompressionHint pointUpHint = new(CompressionKind.NumericSuffix, "PointUpCB", Array.Empty<string>());
        for (int i = 0; i <= 10; i++)
        {
            MethodParameter[] parameters = new MethodParameter[i + 1];
            parameters[0] = new MethodParameter("方法名@funcName", "String", Alias: "funcName", IsScriptMethodName: true);
            for (int j = 0; j < i; j++)
            {
                int suffix = j + 1;
                parameters[suffix] = new MethodParameter("参数" + BuildIndexedAnyParamRawName(j), "Any", Alias: $"param{suffix}");
            }

            yield return Varf("UIButton", $"PointUpCB{i}", parameters, null, $"抬起回调（{i}参数）", compressionHint: pointUpHint, notes: "不可重复注册同名方法；参数在注册时深拷贝快照");
        }
        CompressionHint pointerEnterHint = new(CompressionKind.NumericSuffix, "PointerEnterCB", Array.Empty<string>());
        for (int i = 0; i <= 10; i++)
        {
            MethodParameter[] parameters = new MethodParameter[i + 1];
            parameters[0] = new MethodParameter("方法名@funcName", "String", Alias: "funcName", IsScriptMethodName: true);
            for (int j = 0; j < i; j++)
            {
                int suffix = j + 1;
                parameters[suffix] = new MethodParameter("参数" + BuildIndexedAnyParamRawName(j), "Any", Alias: $"param{suffix}");
            }

            yield return Varf("UIButton", $"PointerEnterCB{i}", parameters, null, $"进入回调（{i}参数）", compressionHint: pointerEnterHint, notes: "不可重复注册同名方法；参数在注册时深拷贝快照");
        }
        CompressionHint pointerExitHint = new(CompressionKind.NumericSuffix, "PointerExitCB", Array.Empty<string>());
        for (int i = 0; i <= 10; i++)
        {
            MethodParameter[] parameters = new MethodParameter[i + 1];
            parameters[0] = new MethodParameter("方法名@funcName", "String", Alias: "funcName", IsScriptMethodName: true);
            for (int j = 0; j < i; j++)
            {
                int suffix = j + 1;
                parameters[suffix] = new MethodParameter("参数" + BuildIndexedAnyParamRawName(j), "Any", Alias: $"param{suffix}");
            }

            yield return Varf("UIButton", $"PointerExitCB{i}", parameters, null, $"离开回调（{i}参数）", compressionHint: pointerExitHint, notes: "不可重复注册同名方法；参数在注册时深拷贝快照");
        }
        CompressionHint clickHint = new(CompressionKind.NumericSuffix, "clickCB", Array.Empty<string>());
        for (int i = 0; i <= 10; i++)
        {
            MethodParameter[] parameters = new MethodParameter[i + 1];
            parameters[0] = new MethodParameter("方法名@funcName", "String", Alias: "funcName", IsScriptMethodName: true);
            for (int j = 0; j < i; j++)
            {
                int suffix = j + 1;
                parameters[suffix] = new MethodParameter("参数" + BuildIndexedAnyParamRawName(j), "Any", Alias: $"param{suffix}");
            }

            yield return Varf("UIButton", $"clickCB{i}", parameters, null, $"点击回调（{i}参数）", compressionHint: clickHint, notes: "不可重复注册同名方法；参数在注册时深拷贝快照");
        }
    }

    private static IEnumerable<MethodSignature> GetUIRectMethods()
    {
        // UIRect 专用方法 (来自 UIRectVar)
        yield return Varf("UIRect", "autoLayout", new[] { new MethodParameter("大小@size", "Vector3", Alias: "size"), new MethodParameter("间距@spacing", "Vector3", Alias: "spacing"), new MethodParameter("边距@padding", "Vector3", Alias: "padding"), new MethodParameter("对齐方式@alignment", "Enum", EnumType: UiAlignment, Alias: "alignment") }, null, "自动排版", notes: "网格布局；padding 左右对称（x）、上下对称（y）");
        yield return Varf("UIRect", "scrollView", new[] { new MethodParameter("面板@content", "UIRect", Alias: "content"), new MethodParameter("滑动模式@method", "Enum", EnumType: ScrollViewMovement, Alias: "method") }, null, "滑动区域模式", notes: "content 的 pivot 被强制设为 (0.5, 1)");
        yield return Varf("UIRect", "mask", new[] { new MethodParameter("关闭", "Bool") }, null, "子 UI 遮罩", notes: "true 为关闭遮罩，false 为启用遮罩");
        yield return Varf("UIRect", "fallOffEffect", new[] { new MethodParameter(string.Empty, "Float", Alias: "value") }, null, "模糊效果");
        yield return Varf("UIRect", "fillMode", new[] { new MethodParameter("填充方式@fillMethod", "Enum", EnumType: UiFillMethod, Alias: "fillMethod"), new MethodParameter("比例@scale", "Float", Alias: "scale") }, null, "填充设置", notes: "调用后 Image 类型被永久设为 Filled");
        yield return Varf("UIRect", "tiled", Array.Empty<MethodParameter>(), null, "设置平铺", notes: "仅对 ProceduralImage 生效，替换后不可逆");
        yield return Varf("UIRect", "loadPic", new[] { new MethodParameter("文件名@filename", "String", Alias: "filename") }, null, "读取本地图片", notes: "异步加载，文件不存在时抛异常；" + ResourceFileNameNote);
        yield return Varf("UIRect", "loadPicAsync", new[] { new MethodParameter("文件名@filename", "String", Alias: "filename") }, "Vector3", "读取本地图片", asyncMode: SdkAsyncMode.Async, notes: "返回图片分辨率；" + ResourceFileNameNote);
        yield return Varf("UIRect", "Radius", new[] { new MethodParameter("圆角半径", "Float") }, null, "圆角");
        yield return Varf("UIRect", "Thickness", new[] { new MethodParameter("厚度", "Float") }, null, "厚度");
        yield return Varf("UIRect", "flexLayout", new[] { new MethodParameter("间距@spacing", "Vector3", Alias: "spacing") }, null, "灵活格子排版");
        yield return Varf("UIRect", "ref", Array.Empty<MethodParameter>(), "UIRect", "引用", notes: "返回同一个运行时 UI 控件引用，不复制 UI 实例。");
    }

#pragma warning disable TK006 // UI 组件方法分组注册，与其他 Get*Methods 对称
    private static IEnumerable<MethodSignature> GetUIInputMethods()
    {
        // UIInput 专用方法 (来自 UIInputVar)
        yield return Varf("UIInput", "getText", Array.Empty<MethodParameter>(), "String", "获取文本", notes: "回车和换行符会被移除");
        yield return Varf("UIInput", "setText", new[] { new MethodParameter("文本@text", "String", AllowImplicitToString: true, Alias: "text") }, null, "设置文本");
        yield return Varf("UIInput", "DirtyFilter", Array.Empty<MethodParameter>(), null, "脏词过滤", notes: "异步执行，过滤结果写入前用户继续输入会被覆盖");
        yield return Varf("UIInput", "setFocus", new[] { new MethodParameter("是否聚焦@OnOff", "Bool", Alias: "OnOff") }, null, "设置焦点");
        yield return Varf("UIInput", "isFocus", Array.Empty<MethodParameter>(), "Bool", "是否有焦点");
        yield return Varf("UIInput", "setReadOnly", new[] { new MethodParameter("开关@OnOff", "Bool", Alias: "OnOff") }, null, "设置只读", notes: "仅阻止用户键盘输入，setText 仍可直接写入");
        yield return Varf("UIInput", "ref", Array.Empty<MethodParameter>(), "UIInput", "引用", notes: "返回同一个运行时 UI 控件引用，不复制 UI 实例。");
    }

    private static IEnumerable<MethodSignature> GetUIRawImageMethods()
    {
        // UIRawImage 专用方法 (来自 UIRawImageVar)
        yield return Varf("UIRawImage", "preDownloadVideo", new[] { new MethodParameter("文件名@file", "String", Alias: "file") }, "Struct", "预加载视频", asyncMode: SdkAsyncMode.Async,
            notes: "返回 Struct 字段：time(Float)。");
        yield return Varf("UIRawImage", "setVideo", new[]
        {
            new MethodParameter("文件名@file", "String", Alias: "file"),
            new MethodParameter("循环@loop", "Bool", Alias: "loop")
        }, "Struct", "设置视频");
        yield return Varf("UIRawImage", "setVideoAsync", new[]
        {
            new MethodParameter("文件名@file", "String", Alias: "file"),
            new MethodParameter("循环@loop", "Bool", Alias: "loop")
        }, "Struct", "设置视频", asyncMode: SdkAsyncMode.Async,
            notes: "返回 Struct 字段：time(Float)。");
        yield return Varf("UIRawImage", "setVideoParam", new[] { new MethodParameter("速率", "Float"), new MethodParameter("音量", "Float"), new MethodParameter("是否循环", "Bool") }, null, "设置视频参数");
        yield return Varf("UIRawImage", "pauseVideo", new[] { new MethodParameter("暂停@pause", "Bool", Alias: "pause") }, null, "暂停视频");
        yield return Varf("UIRawImage", "videoTime", Array.Empty<MethodParameter>(), "Float", "视频播放进度");
        yield return Varf("UIRawImage", "SetData", new[] { new MethodParameter("数据@bytes", "Bytes", Alias: "bytes") }, null, "设置数据");
        yield return Varf("UIRawImage", "SetDanmuAvatar", new[] { new MethodParameter("玩家UID@UID", "String", Alias: "UID") }, null, "设置弹幕头像");
        yield return Varf("UIRawImage", "SetURLPic", new[] { new MethodParameter("网址@url", "String", Alias: "url") }, null, "设置网络图片");
        yield return Varf("UIRawImage", "SetURLPicAsync", new[] { new MethodParameter("网址@url", "String", Alias: "url") }, "Vector3", "设置网络图片", asyncMode: SdkAsyncMode.Async, notes: "失败时返回 (0,0,0)");
        yield return Varf("UIRawImage", "paintAll", new[] { new MethodParameter("颜色列表@colorList", "List", Alias: "colorList") }, null, "全部填充");
        yield return Varf("UIRawImage", "setSize", new[] { new MethodParameter("大小@size", "Vector3", Alias: "size") }, null, "设置大小");
        yield return Varf("UIRawImage", "setMirror", new[] { new MethodParameter("左右镜像", "Bool"), new MethodParameter("上下镜像", "Bool") }, null, "设置镜像");
        yield return Varf("UIRawImage", "paint", new[] { new MethodParameter("坐标@pos", "Vector3", Alias: "pos"), new MethodParameter("颜色@color", "Color", Alias: "color") }, null, "绘制像素", notes: "pos 为图片纹理像素坐标，不是屏幕像素或 UI 布局坐标；不立即显示，须在所有 paint 后调用 apply");
        yield return Varf("UIRawImage", "apply", Array.Empty<MethodParameter>(), null, "应用更改");
        yield return Varf("UIRawImage", "setShader", new[] { new MethodParameter("是否发光@addtive", "Bool", Alias: "addtive") }, null, "设置着色器");
        yield return Varf("UIRawImage", "readPixels", Array.Empty<MethodParameter>(), "List", "读取像素列表");
        yield return Varf("UIRawImage", "readPixel", new[] { new MethodParameter("x", "Long"), new MethodParameter("y", "Long") }, "Color", "读取颜色");
        yield return Varf("UIRawImage", "GetTexSize", Array.Empty<MethodParameter>(), "Vector3", "得到图片分辨率");
        yield return Varf("UIRawImage", "loadPic", new[] { new MethodParameter("文件名@filename", "String", Alias: "filename") }, null, "读取本地图片", notes: ResourceFileNameNote);
        yield return Varf("UIRawImage", "loadPicAsync", new[] { new MethodParameter("文件名@filename", "String", Alias: "filename") }, "Vector3", "读取本地图片", asyncMode: SdkAsyncMode.Async, notes: "返回图片分辨率；文件不存在时返回 (0,0,0)；" + ResourceFileNameNote);
        yield return Varf("UIRawImage", "LoadUserPic", new[] { new MethodParameter("文件名@filename", "String", Alias: "filename") }, null, "读取用户图片", notes: ResourceFileNameNote);
        yield return Varf("UIRawImage", "setImageRect", new[] { new MethodParameter("序号@Index", "Float", Alias: "Index"), new MethodParameter("横向格子数@Colomns", "Long", Alias: "Colomns"), new MethodParameter("纵向格子数@Rows", "Long", Alias: "Rows") }, null, "设置图片区域");
        yield return Varf("UIRawImage", "setTextureFrom", new[] { new MethodParameter("来源@from", "UIRawImage", Alias: "from") }, null, "引用纹理", notes: "共享引用，来源释放后当前控件也失效");
        yield return Varf("UIRawImage", "screenPosToLocalPixel", new[] { new MethodParameter("坐标@pos", "Vector3", Alias: "pos") }, "Vector3", "屏幕坐标转本地像素", notes: "输入为 0~1 屏幕坐标；返回图片纹理像素坐标，原点在图片左下角。");
        yield return Varf("UIRawImage", "createRT", new[] { new MethodParameter("分辨率@res", "Vector3", Alias: "res") }, null, "生成可渲染纹理", notes: "res 为纹理分辨率，不是屏幕分辨率或 UI 布局尺寸。");
        yield return Varf("UIRawImage", "paint2CreateRT", new[]
        {
            new MethodParameter("目标贴图@target", "UIRawImage", Alias: "target"),
            new MethodParameter("缩放@scale", "Vector3", Alias: "scale"),
            new MethodParameter("偏移@offset", "Vector3", Alias: "offset")
        }, null, "绘制图片到可渲染纹理");
        yield return Varf("UIRawImage", "snapShot", Array.Empty<MethodParameter>(), null, "截图", notes: "截取游戏画面而非 UI 画面");
        yield return Varf("UIRawImage", "savePic", new[] { new MethodParameter("文件名", "String") }, null, "保存图片文件");
        yield return Varf("UIRawImage", "clearRT", new[] { new MethodParameter("颜色", "Color") }, null, "清空可渲染纹理");
        yield return Varf("UIRawImage", "SetFrameAnim", new[] { new MethodParameter("动画名", "String"), new MethodParameter("文件名列表@filename", "List", Alias: "filename"), new MethodParameter("是否循环", "Bool") }, null, "设置帧动画", notes: "同名动画只能注册一次");
        yield return Varf("UIRawImage", "PlayFrameAnim", new[] { new MethodParameter("动画名", "String"), new MethodParameter("帧间隔", "Float") }, null, "播放帧动画");
        yield return Varf("UIRawImage", "SetFrameAnimCallBack", new[] { new MethodParameter("动画名", "String"), new MethodParameter("帧序号", "Float"), new MethodParameter("回调方法", "String", IsScriptMethodName: true) }, null, "设置帧动画回调", notes: "帧序号从 1 开始");
        yield return Varf("UIRawImage", "ref", Array.Empty<MethodParameter>(), "UIRawImage", "引用", notes: "返回同一个运行时 UI 控件引用，不复制 UI 实例。");
    }

    private static IEnumerable<MethodSignature> GetUISpineMethods()
    {
        // UISpine 专用方法 (来自 UISpineVar)
        yield return Varf("UISpine", "PlayAnim", new[] { new MethodParameter("动画名", "String"), new MethodParameter("循环", "Bool") }, null, "播放动画");
        yield return Varf("UISpine", "SetTimeScale", new[] { new MethodParameter("速率", "Float") }, null, "动画速率");
        yield return Varf("UISpine", "AnimDuration", Array.Empty<MethodParameter>(), "Float", "动画时长");
        yield return Varf("UISpine", "SetMirror", new[] { new MethodParameter("左右", "Bool"), new MethodParameter("上下", "Bool") }, null, "镜像");
        yield return Varf("UISpine", "SetCallBack", new[] { new MethodParameter("结束回调方法", "String", IsScriptMethodName: true), new MethodParameter("自定义回调前缀", "String") }, null, "设置事件回调", notes: "覆盖式赋值；自定义事件回调名 = 前缀 + 事件名");
        yield return Varf("UISpine", "SetSkin", new[] { new MethodParameter("皮肤列表", "List") }, null, "设置皮肤", notes: "多皮肤叠加合并，任一皮肤名找不到时抛异常");
        yield return Varf("UISpine", "boneList", Array.Empty<MethodParameter>(), "List", "骨骼列表");
        yield return Varf("UISpine", "boneIndex", new[] { new MethodParameter("骨骼名", "String") }, "Float", "得到骨骼序号", notes: "找不到时返回 -1");
        yield return Varf("UISpine", "BonePos", new[] { new MethodParameter("骨骼序号", "Float") }, "Vector3", "得到骨骼位置", notes: "返回世界坐标；索引越界直接抛异常");
        yield return Varf("UISpine", "SetTex", new[] { new MethodParameter("文件名@filename", "String", Alias: "filename") }, null, "替换贴图");
        yield return Varf("UISpine", "ref", Array.Empty<MethodParameter>(), "UISpine", "引用", notes: "返回同一个运行时 UI 控件引用，不复制 UI 实例。");
    }

    private static IEnumerable<MethodSignature> GetUILineMethods()
    {
        // UILine 专用方法 (来自 UILineVar)
        yield return Varf("UILine", "setPos", new[] { new MethodParameter("起点@startPos", "Vector3", Alias: "startPos"), new MethodParameter("终点@endPos", "Vector3", Alias: "endPos"), new MethodParameter("宽度@width", "Float", Alias: "width") }, null, "设置位置", notes: "坐标为归一化值 0~1；仅支持两点线段");
        yield return Varf("UILine", "setPoints", new[] { new MethodParameter("坐标@pos", "List", Alias: "pos") }, null, "设置多个点", notes: "点数范围 2~500，坐标为归一化值 0~1");
        yield return Varf("UILine", "bezierMode", new[] { new MethodParameter(string.Empty, "Bool", Alias: "enabled") }, null, "开启曲线模式", notes: "开启时要求点数满足 4+3n（如 4,7,10...），不满足则抛异常");
        yield return Varf("UILine", "lineListMode", new[] { new MethodParameter(string.Empty, "Bool", Alias: "enabled") }, null, "开启断线模式", notes: "每两个点构成一条独立线段");
        yield return Varf("UILine", "ref", Array.Empty<MethodParameter>(), "UILine", "引用", notes: "返回同一个运行时 UI 控件引用，不复制 UI 实例。");
    }

    private static IEnumerable<MethodSignature> GetUIAvatarMethods()
    {
        // UIAvatar 专用方法 (来自 UIAvatarVar)
        yield return Varf("UIAvatar", "setSize", new[] { new MethodParameter("大小@size", "Float", Alias: "size") }, null, "设置大小");
        yield return Varf("UIAvatar", "ref", Array.Empty<MethodParameter>(), "UIAvatar", "引用", notes: "返回同一个运行时 UI 控件引用，不复制 UI 实例。");
    }
#pragma warning restore TK006
}
