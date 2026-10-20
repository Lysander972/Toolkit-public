namespace Toolkit.Core.Sdk;

// ╔════════════════════════════════════════════════════════════════════════════════╗
// ║                     【禁止审查区域 - 原版 SDK 精确映射】                          ║
// ╠════════════════════════════════════════════════════════════════════════════════╣
// ║  本文件包含对原版 BeScript SDK 编辑器函数的精确硬编码映射。                        ║
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
/// 编辑器函数 (dvf) 定义
/// </summary>
public static class EditorFuncDefinitions
{
    private const string ResourceFileNameNote = "文件参数只填包含扩展名的文件本身名称，不填 GUID 或带路径的名称。";

    public static IEnumerable<MethodSignature> GetAll()
    {
        foreach (MethodSignature sig in GetDeviceEditorFunctions()) yield return sig;
        foreach (MethodSignature sig in GetObjDeviceEditorFunctions()) yield return sig;
        yield return Dvf("RepeatDevice", "setXZ", new[] { new MethodParameter("尺寸@size", "Vector3", "尺寸，只有x y两个轴有效", Alias: "size") }, null, "设置尺寸");
        yield return Dvf("RepeatDevice", "setY", new[] { new MethodParameter(string.Empty, "Float", Alias: "thickness") }, null, "设置厚度");
        yield return Dvf("RepeatDevice", "getXZ", Array.Empty<MethodParameter>(), "Vector3", "获取尺寸（只有x y两个轴有效）");
        yield return Dvf("RepeatDevice", "getY", Array.Empty<MethodParameter>(), "Float", "获取厚度");

        yield return Dvf("VertexDevice", "setVertex", new[] {
            new MethodParameter("序号@index", "Float", "序号0~5", Alias: "index"),
            new MethodParameter("坐标@pos", "Vector3", "坐标", Alias: "pos")
        }, null, "设置顶点局部坐标");
        yield return Dvf("VertexDevice", "getVertex", new[] { new MethodParameter("序号@index", "Float", "序号0~5", Alias: "index") }, "Vector3", "读取顶点局部坐标");
        yield return Dvf("VertexDevice", "setVertices", new[] { new MethodParameter("坐标列表@posList", "List", "坐标列表，需要包含6个向量", Alias: "posList") }, null, "设置全部顶点位置");

        yield return Dvf("ParticleDevice", "switch", new[] { new MethodParameter("开关@OnOff", "Bool", Alias: "OnOff") }, null, "开关粒子");
    }

    private static MethodSignature Dvf(
        string ownerType,
        string name,
        MethodParameter[] parameters,
        string? returnType = null,
        string? summary = null,
        RecommendLevel recommend = RecommendLevel.Default,
        string? notes = null)
    {
        string? notRecommendedReason = BuiltinRecommendationRules.GetObsoleteReason(
            MethodCategory.EditorFunc, ownerType, name);
        bool isObsolete = notRecommendedReason is not null;
        return new MethodSignature(
            MethodCategory.EditorFunc,
            ownerType,
            name,
            parameters,
            returnType,
            summary,
            null,
            recommend,
            MethodScope.EditorOnly,
            IsObsolete: isObsolete,
            NotRecommendedReason: notRecommendedReason,
            Notes: notes);
    }

    /// <summary>Device 基类编辑器函数</summary>
    private static IEnumerable<MethodSignature> GetDeviceEditorFunctions()
    {
        // 位置与缩放
        yield return Dvf("Device", "getPos", Array.Empty<MethodParameter>(), "Vector3", "获取零件位置");
        yield return Dvf("Device", "setPos", new[] { new MethodParameter("坐标@pos", "Vector3", Alias: "pos") }, null, "设置零件位置");
        yield return Dvf("Device", "getCenterPos", Array.Empty<MethodParameter>(), "Vector3", "获取几何中心");
        yield return Dvf("Device", "getScale", Array.Empty<MethodParameter>(), "Float", "获取缩放");
        yield return Dvf("Device", "setScale", new[] { new MethodParameter("缩放@scale", "Float", Alias: "scale") }, null, "设置缩放");
        yield return Dvf("Device", "setRot", new[] { new MethodParameter("向前方向@forward", "Vector3", Alias: "forward"), new MethodParameter("向上方向@up", "Vector3", Alias: "up") }, null, "设置旋转");

        // 坐标转换
        yield return Dvf("Device", "localToWorldPos", new[] { new MethodParameter("局部坐标@localPos", "Vector3", Alias: "localPos") }, "Vector3", "局部坐标转世界坐标");
        yield return Dvf("Device", "worldToLocalPos", new[] { new MethodParameter("世界坐标@worldPos", "Vector3", Alias: "worldPos") }, "Vector3", "世界坐标转局部坐标");
        yield return Dvf("Device", "localToWorldDir", new[] { new MethodParameter("局部方向@localPos", "Vector3", Alias: "localPos") }, "Vector3", "局部方向转世界方向");
        yield return Dvf("Device", "WorldToLocalDir", new[] { new MethodParameter("世界方向@worldDir", "Vector3", Alias: "worldDir") }, "Vector3", "世界方向转局部方向");

        // 锚点
        yield return Dvf("Device", "getAnchors", Array.Empty<MethodParameter>(), "Float", "获取锚点数量");
        yield return Dvf("Device", "getAnchorPos", new[] { new MethodParameter("序号@index", "Float", Alias: "index") }, "Vector3", "获取锚点位置");
        yield return Dvf("Device", "getAnchorLocal2WorldDir", new[] { new MethodParameter("序号@index", "Float", Alias: "index"), new MethodParameter("世界方向@worldDir", "Vector3", Alias: "worldDir") }, "Vector3", "获取锚点方向");

        // 名称/标签/ID
        yield return Dvf("Device", "getTypeName", Array.Empty<MethodParameter>(), "String", "获取类型名");
        yield return Dvf("Device", "getTypeID", Array.Empty<MethodParameter>(), "Float", "获取类型 ID");
        yield return Dvf("Device", "getID", Array.Empty<MethodParameter>(), "Float", "获取零件 ID");
        yield return Dvf("Device", "getIndex", Array.Empty<MethodParameter>(), "Float", "获取零件序号");
        yield return Dvf("Device", "getReadableName", Array.Empty<MethodParameter>(), "String", "获取可读名称");
        yield return Dvf("Device", "getNickname", Array.Empty<MethodParameter>(), "String", "获取逻辑别名");
        yield return Dvf("Device", "setLogicName", new[] { new MethodParameter("别名@alias", "String", Alias: "alias") }, null, "设置逻辑别名");
        yield return Dvf("Device", "setTag", new[] { new MethodParameter("标签@tag", "String", Alias: "tag") }, null, "设置标签");
        yield return Dvf("Device", "setCanPress", new[] { new MethodParameter("可点击@canPress", "Bool", Alias: "canPress") }, null, "设置可点击");

        // 焊接
        yield return Dvf("Device", "setManualWeld", new[] { new MethodParameter("开关@OnOff", "Bool", Alias: "OnOff"), new MethodParameter("黏连零件列表@weld devices", "List", Alias: "weld_devices") }, null, "设置手动焊接列表");

        // 材质与外观
        yield return Dvf("Device", "setColor", new[] { new MethodParameter("颜色@color", "Color", Alias: "color") }, null, "设置颜色");
        yield return Dvf("Device", "getColor", Array.Empty<MethodParameter>(), "Color", "获取颜色");
        yield return Dvf("Device", "setMetalic", new[] { new MethodParameter("金属度@matMetalic", "Float", Alias: "matMetalic") }, null, "设置金属度");
        yield return Dvf("Device", "getMetalic", Array.Empty<MethodParameter>(), "Float", "获取金属度");
        yield return Dvf("Device", "setGlossiness", new[] { new MethodParameter("光泽度@matGloss", "Float", Alias: "matGloss") }, null, "设置光泽度");
        yield return Dvf("Device", "getGloss", Array.Empty<MethodParameter>(), "Float", "获取光泽度");
        yield return Dvf("Device", "setTransparent", new[] { new MethodParameter("透明度@matAlpha", "Float", Alias: "matAlpha") }, null, "设置透明度");
        yield return Dvf("Device", "setPic", new[] { new MethodParameter("文件名@file", "String", Alias: "file") }, null, "设置贴图", notes: ResourceFileNameNote);
        yield return Dvf("Device", "setEmmit", new[] { new MethodParameter("是否自发光@boolMatEmmit", "Bool", Alias: "boolMatEmmit"), new MethodParameter("发光系数@emmitRate", "Float", Alias: "emmitRate") }, null, "设置自发光");
        yield return Dvf("Device", "clearMat", Array.Empty<MethodParameter>(), null, "还原默认材质");
    }

    /// <summary>ObjDevice 自定义模型编辑器函数</summary>
    private static IEnumerable<MethodSignature> GetObjDeviceEditorFunctions()
    {
        // 顶点/法线/UV/索引列表操作
        yield return Dvf("ObjDevice", "setVertexIndex", new[] {
            new MethodParameter("坐标列表@posList", "List", "坐标列表", Alias: "posList"),
            new MethodParameter("法线列表@normalList", "List", "法线列表", Alias: "normalList"),
            new MethodParameter("索引列表@indexList", "List", "索引列表", Alias: "indexList"),
            new MethodParameter("UV列表@uvList", "List", "UV列表", Alias: "uvList")
        }, null, "设置顶点索引列表，顶点是局部坐标，索引列表最大6000，法线列表不填则自动计算法线");
        yield return Dvf("ObjDevice", "getVertics", Array.Empty<MethodParameter>(), "List", "得到顶点列表（坐标向量列表）");
        yield return Dvf("ObjDevice", "getNormals", Array.Empty<MethodParameter>(), "List", "得到法线列表（方向向量列表）");
        yield return Dvf("ObjDevice", "getUVs", Array.Empty<MethodParameter>(), "List", "得到UV列表");
        yield return Dvf("ObjDevice", "getTriangles", Array.Empty<MethodParameter>(), "List", "得到索引列表（序号列表）");

        // 镜像操作
        yield return Dvf("ObjDevice", "setBoolMirror", new[] { new MethodParameter(string.Empty, "Bool", Alias: "mirror") }, null, "设置镜像");
        yield return Dvf("ObjDevice", "isMirror", Array.Empty<MethodParameter>(), "Bool", "是否镜像");

        // 模型操作
        yield return Dvf("ObjDevice", "getModelName", Array.Empty<MethodParameter>(), "String", "得到自定义模型名");
        yield return Dvf("ObjDevice", "loadModel", new[] { new MethodParameter(string.Empty, "String", Alias: "modelName") }, null, "载入自定义模型");

        // 碰撞体操作
        yield return Dvf("ObjDevice", "getColliderInfo", Array.Empty<MethodParameter>(), "Struct", "得到碰撞体信息（type, center, size）",
            notes: "返回 Struct 字段：type(String)、center(Vector3)、size(Vector3)。SphereCollider 时 size=(radius,0,0)；CapsuleCollider 时 size=(radius,height,0)；MeshCollider 时仅 type 有值。");
        yield return Dvf("ObjDevice", "setCollider", new[] {
            new MethodParameter("局部坐标", "Vector3"),
            new MethodParameter("尺寸", "Vector3")
        }, null, "设置碰撞体（仅盒形、球形、胶囊形）");
    }
}
