using static Toolkit.Core.Sdk.BuiltinEnumTypes;

namespace Toolkit.Core.Sdk;

// ╔════════════════════════════════════════════════════════════════════════════════╗
// ║                     【禁止审查区域 - 原版 SDK 精确映射】                          ║
// ╠════════════════════════════════════════════════════════════════════════════════╣
// ║  本文件包含对原版 BeScript SDK 全局函数的精确硬编码映射。                          ║
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
/// 全局函数 (fun) 定义
/// </summary>
public static class GlobalFuncDefinitions
{
    public static IEnumerable<MethodSignature> GetAll()
    {
        foreach (MethodSignature sig in GetMapFunctions()) yield return sig;
        foreach (MethodSignature sig in GetMapMechFunctions()) yield return sig;
        foreach (MethodSignature sig in GetUIScreenInputFunctions()) yield return sig;
        foreach (MethodSignature sig in GetVarFunctions()) yield return sig;
        foreach (MethodSignature sig in GetSystemDebugFunctions()) yield return sig;
    }

    private static MethodSignature Fun(
        string name,
        MethodParameter[] parameters,
        string? returnType = null,
        string? summary = null,
        string? remarks = null,
        RecommendLevel recommend = RecommendLevel.Default,
        CompressionHint? compressionHint = null,
        SdkAsyncMode asyncMode = SdkAsyncMode.Sync,
        string? notes = null)
    {
        string? notRecommendedReason = BuiltinRecommendationRules.GetObsoleteReason(
            MethodCategory.GlobalFunc, null, name);
        bool isObsolete = notRecommendedReason is not null;
        return new MethodSignature(
            MethodCategory.GlobalFunc,
            null,
            name,
            parameters,
            returnType,
            summary,
            remarks,
            recommend,
            MethodScope.Universal,
            compressionHint,
            IsObsolete: isObsolete,
            NotRecommendedReason: notRecommendedReason,
            AsyncMode: asyncMode,
            Notes: notes);
    }

    private static IEnumerable<MethodSignature> FunNumericSuffixGroup(
        string baseName,
        MethodParameter[] baseParams,
        string? returnType,
        string summaryBase,
        params MethodParameter[][] extraParamSets)
    {
        CompressionHint hint = new(CompressionKind.NumericSuffix, baseName, Array.Empty<string>());
        yield return Fun(baseName, baseParams, returnType, summaryBase, compressionHint: hint);

        for (int i = 0; i < extraParamSets.Length; i++)
        {
            int suffix = i + 1;
            MethodParameter[] parameters = extraParamSets[i];
            string summary = $"{summaryBase}（{parameters.Length} 参数）";
            yield return Fun(baseName + suffix, parameters, returnType, summary, compressionHint: hint);
        }
    }

    /// <summary>Map 相关全局函数</summary>
    private static IEnumerable<MethodSignature> GetMapFunctions()
    {
        // 玩家/房间信息
        yield return Fun("getCurrentPlayer", [], "Player", "返回当前客户端对应的玩家");
        yield return Fun("getCurrentHost", [], "Player", "返回当前房间的房主");
        yield return Fun("isHost", [], "Bool", "判断当前客户端是否为房主");

        // 地图信息
        yield return Fun("map.maxPlayer", [], "Float", "返回地图最大玩家人数");
        yield return Fun("map.players", [], "List", "返回当前房间内所有玩家列表");
        yield return Fun("map.quit", [], null, "退出房间");
        yield return Fun("map.showHideUI", [new MethodParameter("是否显示@boolShow", "Bool", Alias: "boolShow")], null, "开关默认 UI");
        yield return Fun("map.mapName", [], "String", "获取当前地图名称");
        yield return Fun("map.mapDesc", [], "String", "获取当前地图描述");
        yield return Fun("map.mapRule", [], "String", "获取当前地图规则");
        yield return Fun("map.ManualLoadBundle",
        [
            new MethodParameter("文件名@filename", "String", Alias: "filename")
        ], "Float", "异步加载 Bundle", asyncMode: SdkAsyncMode.Async,
            notes: "加载的是工程发布时已包含的 Bundle，不是任意模型文件导入接口；模型资源导入应在工具链侧完成。");
        yield return Fun("map.ManualUnLoadBundle",
        [
            new MethodParameter("文件名@filename", "String", Alias: "filename")
        ], "Float", "异步卸载 Bundle", asyncMode: SdkAsyncMode.Async,
            notes: "只能卸载已经加载的 Bundle；这不是删除工程资源。");
        yield return Fun("map.BundleList", [], "List", "已加载 Bundle 列表");
        yield return Fun("map.restartGame", [], null, "重启玩法");
        yield return Fun("map.changeMap2",
        [
            new MethodParameter("地图名称@map name", "String", Alias: "map_name"),
            new MethodParameter("限制匹配@boolLimit", "Bool", Alias: "boolLimit"),
            new MethodParameter("规则@rule", "String", Alias: "rule"),
            new MethodParameter("人数限制@PlayerNum", "Long", Alias: "PlayerNum")
        ], null, "切换地图");

        // 全局数据
        yield return Fun("map.setGlobalData", [new MethodParameter("数据@Data", "Dict", Alias: "Data")], null, "设置全局持久化数据");
        yield return Fun("map.getGlobalDataAsync", [], "Dict", "异步读取全局持久化数据", asyncMode: SdkAsyncMode.Async);
        yield return Fun("map.getGlobalData",
        [
            new MethodParameter("通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true),
            new MethodParameter("通知参数@Attach", "Dict", Alias: "Attach")
        ], null, "以回调方式获取全局数据", notes: "回调模式：数据到达后调用同零件上 CallBackMethod 方法，Attach 附加到回调参数");

        // 阵营/PVP
        yield return Fun("map.setPTeam",
        [
            new MethodParameter(string.Empty, "Player", Alias: "player"),
            new MethodParameter("阵营编号@teamID", "Float", Alias: "teamID")
        ], null, "设置玩家阵营");
        yield return Fun("map.getPTeam", [new MethodParameter(string.Empty, "Player", Alias: "player")], "Float", "获取玩家阵营");
        yield return Fun("map.teamPlayers", [new MethodParameter("阵营序号teamID", "Float")], "List", "获取阵营玩家列表");
        yield return Fun("map.setAloneTeam", [new MethodParameter(string.Empty, "Player", Alias: "player")], null, "设置玩家为独立阵营");
        yield return Fun("majiangWin", [new MethodParameter("麻将列表", "List")], "Bool", "麻将胡牌",
            notes: "列表元素必须为 String。数牌格式为 1万/1条/1筒 等两字符；字牌支持 东/西/南/北/中/发/白。");
        yield return Fun("map.team.autoBalance",
        [
            new MethodParameter("阵营数@teamNum", "Float", Alias: "teamNum"),
            new MethodParameter("起始阵营序号@startTeamIndex", "Float", Alias: "startTeamIndex")
        ], null, "自动平衡阵营");

        // 社交/玩家查找
        yield return Fun("findPlayerAsync", [new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID")], "Player", "异步获取玩家信息", asyncMode: SdkAsyncMode.Async);
        yield return Fun("findPlayer",
        [
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true),
            new MethodParameter("通知参数@Attach", "Dict", Alias: "Attach")
        ], null, "以回调方式获取玩家信息", notes: "回调模式：数据到达后调用同零件上 CallBackMethod 方法，Attach 附加到回调参数");
        yield return Fun("findPlayersAsync", [new MethodParameter("玩家ID列表@PlayerIDs", "List", Alias: "PlayerIDs")], "List", "异步批量获取玩家信息", asyncMode: SdkAsyncMode.Async);

        // IAP/广告
        yield return Fun("map.showRewardAD", [new MethodParameter("播放结果回调方法", "String", IsScriptMethodName: true), new MethodParameter("用途跟踪", "String")], null, "展示激励广告");
        yield return Fun("map.showInterstitialAD", [new MethodParameter("用途跟踪", "String")], null, "展示插屏广告");

        // 自定义同步数据
        string[] mapCustomTypes =
        [
            "Bool", "Bytes", "Color", "Datetime", "Dict", "Float",
            "List", "Long", "Mech", "Player", "String", "Struct", "Vector3"
        ];
        CompressionHint mapGetCustomHint = new(CompressionKind.TypeSuffix, "map.getCustom", mapCustomTypes);
        CompressionHint mapSetCustomHint = new(CompressionKind.TypeSuffixParam, "map.setCustom", mapCustomTypes);
        yield return Fun("map.setCustomFloat",
        [
            new MethodParameter("名称@name", "String", Alias: "name"),
            new MethodParameter("值@value", "Float", Alias: "value")
        ], null, "设置同步数据小数", compressionHint: mapSetCustomHint);
        yield return Fun("map.getCustomFloat", [new MethodParameter("名称@name", "String", Alias: "name")], "Float", "获取同步数据小数", compressionHint: mapGetCustomHint);
        yield return Fun("map.setCustomLong",
        [
            new MethodParameter("名称@name", "String", Alias: "name"),
            new MethodParameter("值@value", "Long", Alias: "value")
        ], null, "设置同步数据整数", compressionHint: mapSetCustomHint);
        yield return Fun("map.getCustomLong", [new MethodParameter("名称@name", "String", Alias: "name")], "Long", "获取同步数据整数", compressionHint: mapGetCustomHint);
        yield return Fun("map.setCustomString",
        [
            new MethodParameter("名称@name", "String", Alias: "name"),
            new MethodParameter("值@value", "String", Alias: "value")
        ], null, "设置同步数据字符串", compressionHint: mapSetCustomHint);
        yield return Fun("map.getCustomString", [new MethodParameter("名称@name", "String", Alias: "name")], "String", "获取同步数据字符串", compressionHint: mapGetCustomHint);
        yield return Fun("map.existCustomData", [new MethodParameter("名称@name", "String", Alias: "name")], "Bool", "检查同步数据是否存在");

        // 按类型设置/获取同步数据
        yield return Fun("map.setCustomBool",
        [
            new MethodParameter("名称@name", "String", Alias: "name"),
            new MethodParameter("值@value", "Bool", Alias: "value")
        ], null, "设置同步数据布尔", compressionHint: mapSetCustomHint);
        yield return Fun("map.getCustomBool", [new MethodParameter("名称@name", "String", Alias: "name")], "Bool", "获取同步数据布尔", compressionHint: mapGetCustomHint);
        yield return Fun("map.setCustomVector3",
        [
            new MethodParameter("名称@name", "String", Alias: "name"),
            new MethodParameter("值@value", "Vector3", Alias: "value")
        ], null, "设置同步数据向量", compressionHint: mapSetCustomHint);
        yield return Fun("map.getCustomVector3", [new MethodParameter("名称@name", "String", Alias: "name")], "Vector3", "获取同步数据向量", compressionHint: mapGetCustomHint);
        yield return Fun("map.setCustomList",
        [
            new MethodParameter("名称@name", "String", Alias: "name"),
            new MethodParameter("值@value", "List", Alias: "value")
        ], null, "设置同步数据列表", compressionHint: mapSetCustomHint);
        yield return Fun("map.getCustomList", [new MethodParameter("名称@name", "String", Alias: "name")], "List", "获取同步数据列表", compressionHint: mapGetCustomHint);
        yield return Fun("map.setCustomDict",
        [
            new MethodParameter("名称@name", "String", Alias: "name"),
            new MethodParameter("值@value", "Dict", Alias: "value")
        ], null, "设置同步数据字典", compressionHint: mapSetCustomHint);
        yield return Fun("map.getCustomDict", [new MethodParameter("名称@name", "String", Alias: "name")], "Dict", "获取同步数据字典", compressionHint: mapGetCustomHint);
        yield return Fun("map.setCustomStruct",
        [
            new MethodParameter("名称@name", "String", Alias: "name"),
            new MethodParameter("值@value", "Struct", Alias: "value")
        ], null, "设置同步数据结构", compressionHint: mapSetCustomHint);
        yield return Fun("map.getCustomStruct", [new MethodParameter("名称@name", "String", Alias: "name")], "Struct", "获取同步数据结构", compressionHint: mapGetCustomHint);
        yield return Fun("map.setCustomColor",
        [
            new MethodParameter("名称@name", "String", Alias: "name"),
            new MethodParameter("值@value", "Color", Alias: "value")
        ], null, "设置同步数据颜色", compressionHint: mapSetCustomHint);
        yield return Fun("map.getCustomColor", [new MethodParameter("名称@name", "String", Alias: "name")], "Color", "获取同步数据颜色", compressionHint: mapGetCustomHint);
        yield return Fun("map.setCustomDatetime",
        [
            new MethodParameter("名称@name", "String", Alias: "name"),
            new MethodParameter("值@value", "Datetime", Alias: "value")
        ], null, "设置同步数据日期时间", compressionHint: mapSetCustomHint);
        yield return Fun("map.getCustomDatetime", [new MethodParameter("名称@name", "String", Alias: "name")], "Datetime", "获取同步数据日期时间", compressionHint: mapGetCustomHint);
        yield return Fun("map.setCustomMech",
        [
            new MethodParameter("名称@name", "String", Alias: "name"),
            new MethodParameter("值@value", "Mech", Alias: "value")
        ], null, "设置同步数据机械", compressionHint: mapSetCustomHint);
        yield return Fun("map.getCustomMech", [new MethodParameter("名称@name", "String", Alias: "name")], "Mech", "获取同步数据机械", compressionHint: mapGetCustomHint);
        yield return Fun("map.setCustomPlayer",
        [
            new MethodParameter("名称@name", "String", Alias: "name"),
            new MethodParameter("值@value", "Player", Alias: "value")
        ], null, "设置同步数据玩家", compressionHint: mapSetCustomHint);
        yield return Fun("map.getCustomPlayer", [new MethodParameter("名称@name", "String", Alias: "name")], "Player", "获取同步数据玩家", compressionHint: mapGetCustomHint);
        yield return Fun("map.setCustomBytes",
        [
            new MethodParameter("名称@name", "String", Alias: "name"),
            new MethodParameter("值@value", "Bytes", Alias: "value")
        ], null, "设置同步数据二进制", compressionHint: mapSetCustomHint);
        yield return Fun("map.getCustomBytes", [new MethodParameter("名称@name", "String", Alias: "name")], "Bytes", "获取同步数据二进制", compressionHint: mapGetCustomHint);
        yield return Fun("map.setCustomMesh",
        [
            new MethodParameter("名称@name", "String", Alias: "name"),
            new MethodParameter("值@value", "Mesh", Alias: "value")
        ], null, "设置自定义网格",
            notes: "保存的是运行时 Mesh 数据，常见来源是 ObjDevice.getMesh；不会生成或导入工程模型资源。");
        yield return Fun("map.getCustomMesh", [new MethodParameter("名称@name", "String", Alias: "name")], "Mesh", "获取自定义网格",
            notes: "读取由 map.setCustomMesh 保存的运行时 Mesh 数据；不是按模型文件名加载资源。");

        // 地图环境
        yield return Fun("map.center", [], "Vector3", "获取地图中心位置");
        yield return Fun("map.size", [], "Vector3", "获取地图大小");
        yield return Fun("map.setTerrainPos", [new MethodParameter("坐标@pos", "Vector3", Alias: "pos")], null, "设置地形坐标");
        yield return Fun("map.setWaterPos", [new MethodParameter("坐标@pos", "Vector3", Alias: "pos")], null, "设置水面坐标");
        yield return Fun("map.ground.color",
        [
            new MethodParameter("颜色@color", "Color", Alias: "color"),
            new MethodParameter("金属度@metallic", "Float", Alias: "metallic"),
            new MethodParameter("光泽度@glossiness", "Float", Alias: "glossiness")
        ], null, "设置简单地面颜色");
        yield return Fun("map.alphaMap",
        [
            new MethodParameter("世界坐标", "Vector3"),
            new MethodParameter("材质序号@matIndex", "Enum", EnumType: TerrainMaterialIndex, Alias: "matIndex")
        ], "Float", "获取地形材质比例");
        yield return Fun("map.getTerrainHeight", [new MethodParameter("坐标@pos", "Vector3", Alias: "pos")], "Float", "获取地形高度");
        yield return Fun("map.setTerrainHeight",
        [
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("力度@opacity", "Float", Alias: "opacity"),
            new MethodParameter("笔刷半径@radius", "Float", Alias: "radius"),
            new MethodParameter("笔刷图片文件@texure", "String", Alias: "texure")
        ], null, "设置地形高度");
        yield return Fun("map.getTerrainWidth", [], "Float", "获取地形宽度");
        yield return Fun("map.getTerrainHeightList", [], "List", "获取地形高度列表");
        yield return Fun("map.setTerrainHeightList",
        [
            new MethodParameter("高度列表@heightList", "List", Alias: "heightList")
        ], null, "设置地形高度列表");
        yield return Fun("map.setTerrainTexture",
        [
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("贴图序号@textureIndex", "Long", Alias: "textureIndex"),
            new MethodParameter("贴图透明度列表@alphaList", "List", Alias: "alphaList")
        ], null, "绘制地形纹理比例");
        yield return Fun("map.setTerrainMat",
        [
            new MethodParameter("序号", "Float"),
            new MethodParameter("颜色贴图文件名", "String"),
            new MethodParameter("法线贴图文件名", "String")
        ], null, "设置自定义地形材质");
        yield return Fun("map.getTerrainBytes", [], "Bytes", "获取地形二进制数据");
        yield return Fun("map.setTerrainBytes", [new MethodParameter("数据@bytes", "Bytes", Alias: "bytes")], null, "设置地形二进制数据");

        // 寻路
        yield return Fun("map.InitAstarPathFinder",
        [
            new MethodParameter("宽@width", "Long", Alias: "width"),
            new MethodParameter("长@height", "Long", Alias: "height"),
            new MethodParameter("格子缩放@nodeSize", "Float", Alias: "nodeSize"),
            new MethodParameter("中心点@center", "Vector3", Alias: "center"),
            new MethodParameter("类型@type", "Enum", EnumType: AstarGridType, Alias: "type")
        ], null, "初始化寻路");
        yield return Fun("map.AStarPathFinderSetWalkable",
        [
            new MethodParameter("x", "Long"),
            new MethodParameter("y", "Long"),
            new MethodParameter("可走@Walkable", "Bool", Alias: "Walkable")
        ], null, "设置寻路格子");
        yield return Fun("map.AStarPathFinderSetWalkableList",
        [
            new MethodParameter("列表@List", "List", Alias: "List")
        ], null, "设置寻路格子列表");

        // 存档
        const string playerArchiveNote = "玩家网络存档只属于当前玩家与当前地图；子地图和主地图共享同一份存档。不是关卡库，也不能按任意关卡名读写。";
        yield return Fun("map.save", [], null, "上传玩家存档", notes: playerArchiveNote);
        yield return Fun("map.load", [], "Dict", "获取玩家存档", notes: playerArchiveNote);
        yield return Fun("map.loadAsync", [], "Dict", "异步获取玩家存档", notes: playerArchiveNote);
        yield return Fun("map.localSave", [], null, "保存玩家本地存档", notes: "本地存档只属于当前玩家、当前地图与当前设备；子地图和主地图共享同一份本地存档。");
        yield return Fun("map.localLoad", [], "Dict", "获取玩家本地存档", notes: "本地存档只属于当前玩家、当前地图与当前设备；子地图和主地图共享同一份本地存档。");
        yield return Fun("map.loadOtherArchiveAsync", [new MethodParameter("玩家ID@PlayerIDs", "List", Alias: "PlayerIDs")], "List", "异步下载其他玩家存档", asyncMode: SdkAsyncMode.Async,
            notes: "按玩家ID列表读取这些玩家在当前地图下的网络存档；不是关卡库查询接口。");
        yield return Fun("map.loadOtherArchive2",
        [
            new MethodParameter("玩家ID@PlayerIDs", "List", Alias: "PlayerIDs"),
            new MethodParameter("通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true),
            new MethodParameter("通知参数@Attach", "Dict", Alias: "Attach")
        ], null, "下载其他玩家存档", notes: "按玩家ID列表读取这些玩家在当前地图下的网络存档；不是关卡库查询接口。CallBackMethod 对应的自定义脚本参数必须为 (players: BeList, archives: BeList, attach: BeDict)。");

        // 玩家共享存档
        string[] mapPlayerShareTypes =
        [
            "Bool", "Color", "Datetime", "Dict", "Float",
            "List", "Long", "Player", "String", "Vector3"
        ];
        CompressionHint mapSetPlayerShareHint = new(CompressionKind.TypeSuffixParam, "map.setPlayerShare", mapPlayerShareTypes);
        yield return Fun("map.setPlayerShareFloat",
        [
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("名称@Key", "String", Alias: "Key"),
            new MethodParameter("值@Value", "Float", Alias: "Value")
        ], null, "设置玩家共享数据小数", compressionHint: mapSetPlayerShareHint, notes: "作用于指定玩家ID下的共享变量；所有人都可以修改；Key 长度范围为 1~12。");
        yield return Fun("map.setPlayerShareLong",
        [
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("名称@Key", "String", Alias: "Key"),
            new MethodParameter("值@Value", "Long", Alias: "Value")
        ], null, "设置玩家共享数据整数", compressionHint: mapSetPlayerShareHint, notes: "作用于指定玩家ID下的共享变量；所有人都可以修改；Key 长度范围为 1~12。");
        yield return Fun("map.setPlayerShareString",
        [
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("名称@Key", "String", Alias: "Key"),
            new MethodParameter("值@Value", "String", Alias: "Value")
        ], null, "设置玩家共享数据字符串", compressionHint: mapSetPlayerShareHint, notes: "作用于指定玩家ID下的共享变量；所有人都可以修改；Key 长度范围为 1~12。");
        yield return Fun("map.setPlayerShareBool",
        [
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("名称@Key", "String", Alias: "Key"),
            new MethodParameter("值@Value", "Bool", Alias: "Value")
        ], null, "设置玩家共享数据布尔", compressionHint: mapSetPlayerShareHint, notes: "作用于指定玩家ID下的共享变量；所有人都可以修改；Key 长度范围为 1~12。");
        yield return Fun("map.setPlayerShareVector3",
        [
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("名称@Key", "String", Alias: "Key"),
            new MethodParameter("值@Value", "Vector3", Alias: "Value")
        ], null, "设置玩家共享数据向量", compressionHint: mapSetPlayerShareHint, notes: "作用于指定玩家ID下的共享变量；所有人都可以修改；Key 长度范围为 1~12。");
        yield return Fun("map.setPlayerShareColor",
        [
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("名称@Key", "String", Alias: "Key"),
            new MethodParameter("值@Value", "Color", Alias: "Value")
        ], null, "设置玩家共享数据颜色", compressionHint: mapSetPlayerShareHint, notes: "作用于指定玩家ID下的共享变量；所有人都可以修改；Key 长度范围为 1~12。");
        yield return Fun("map.setPlayerShareDatetime",
        [
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("名称@Key", "String", Alias: "Key"),
            new MethodParameter("值@Value", "Datetime", Alias: "Value")
        ], null, "设置玩家共享数据日期时间", compressionHint: mapSetPlayerShareHint, notes: "作用于指定玩家ID下的共享变量；所有人都可以修改；Key 长度范围为 1~12。");
        yield return Fun("map.setPlayerShareList",
        [
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("名称@Key", "String", Alias: "Key"),
            new MethodParameter("值@Value", "List", Alias: "Value")
        ], null, "设置玩家共享数据列表", compressionHint: mapSetPlayerShareHint, notes: "作用于指定玩家ID下的共享变量；所有人都可以修改；Key 长度范围为 1~12。");
        yield return Fun("map.setPlayerShareDict",
        [
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("名称@Key", "String", Alias: "Key"),
            new MethodParameter("值@Value", "Dict", Alias: "Value")
        ], null, "设置玩家共享数据字典", compressionHint: mapSetPlayerShareHint, notes: "作用于指定玩家ID下的共享变量；所有人都可以修改；Key 长度范围为 1~12。");
        yield return Fun("map.setPlayerSharePlayer",
        [
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("名称@Key", "String", Alias: "Key"),
            new MethodParameter("值@Value", "Player", Alias: "Value")
        ], null, "设置玩家共享数据玩家", compressionHint: mapSetPlayerShareHint, notes: "作用于指定玩家ID下的共享变量；所有人都可以修改；Key 长度范围为 1~12。");
        yield return Fun("map.addPlayerShareLong",
        [
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("名称@Key", "String", Alias: "Key"),
            new MethodParameter("值@Value", "Long", Alias: "Value")
        ], null, "玩家共享整数加法", notes: "作用于指定玩家ID下的共享变量；所有人都可以修改；Key 长度范围为 1~12。");
        yield return Fun("map.delPlayerShareVar",
        [
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("名称@Key", "String", Alias: "Key")
        ], null, "删除玩家共享数据", notes: "作用于指定玩家ID下的共享变量；所有人都可以修改；Key 长度范围为 1~12。");
        yield return Fun("map.getAllPlayerShareVarAsync", [new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID")], "Dict", "异步获取玩家全部共享数据", asyncMode: SdkAsyncMode.Async,
            notes: "读取指定玩家ID下的共享数据；共享数据所有人都可以修改。返回 Dict 的键为共享变量名(String)，值为对应类型的变量。");
        yield return Fun("map.getAllPlayerShareVar",
        [
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true),
            new MethodParameter("通知参数@Attach", "Dict", Alias: "Attach")
        ], null, "获取玩家全部共享数据", notes: "读取指定玩家ID下的共享数据；共享数据所有人都可以修改。CallBackMethod 对应的自定义脚本参数必须为 (playerID: BeLong, vars: BeDict, attach: BeDict)。");

        // 排行榜
        yield return Fun("map.setRank",
        [
            new MethodParameter("数据@score", "Number", Alias: "score"),
            new MethodParameter("附加信息@dict", "Dict", Alias: "dict"),
            new MethodParameter("类型@type", "Enum", EnumType: RankValueType, Alias: "type"),
            new MethodParameter("排行榜编号(0~4)@RankIndex(0~4)", "Long", Alias: "RankIndex")
        ], null, "上传排行榜成绩（RankIndex: 0~4）");
        yield return Fun("map.downloadRank",
        [
            new MethodParameter("类型@type", "Enum", EnumType: RankValueType, Alias: "type"),
            new MethodParameter("排行榜编号(0~4)@RankIndex(0~4)", "Long", Alias: "RankIndex"),
            new MethodParameter("一页数量@NumPerPage", "Float", Alias: "NumPerPage"),
            new MethodParameter("页码@Page", "Float", Alias: "Page")
        ], null, "下载排行榜（RankIndex: 0~4）");
        yield return Fun("map.ShowMiniGameRank",
        [
            new MethodParameter("图片@rawImage", "UIRawImage", Alias: "rawImage"),
            new MethodParameter("开关@OnOff", "Bool", Alias: "OnOff")
        ], null, "开关小游戏排行榜");
        yield return Fun("map.ReportMiniGameRank",
        [
            new MethodParameter("分数@score", "Long", Alias: "score")
        ], null, "上传小游戏排行榜数据");
        yield return Fun("map.WechatShare",
        [
            new MethodParameter("标题@title", "String", Alias: "title"),
            new MethodParameter("动作@action", "String", Alias: "action"),
            new MethodParameter("背景图URL@imageURL", "String", Alias: "imageURL")
        ], null, "微信分享");
        yield return Fun("map.WechatPage", [], null, "微信游戏圈");
        yield return Fun("map.addRankScore",
        [
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("分数增量@AddScore", "Long", Alias: "AddScore"),
            new MethodParameter("排行榜编号(0~4)@RankIndex(0~4)", "Long", Alias: "RankIndex")
        ], null, "增加排行榜数值（RankIndex: 0~4）");
        yield return Fun("map.clearRank", [new MethodParameter("排行榜编号@RankIndex", "Long", Alias: "RankIndex")], null, "清空排行榜");
        yield return Fun("map.getRank",
        [
            new MethodParameter("类型@type", "Enum", EnumType: RankValueType, Alias: "type"),
            new MethodParameter("排行榜编号(0~4)@RankIndex(0~4)", "Long", Alias: "RankIndex"),
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID")
        ], null, "获取排行榜分数（RankIndex: 0~4）");

        // 等级分
        yield return Fun("map.setELO2",
        [
            new MethodParameter("类型@Type", "String", Alias: "Type"),
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("值@Value", "Long", Alias: "Value")
        ], null, "设置玩家新等级分");
        yield return Fun("map.getELOAsync",
        [
            new MethodParameter("类型@Type", "String", Alias: "Type"),
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID")
        ], "Long", "异步获取玩家新等级分", asyncMode: SdkAsyncMode.Async);
        yield return Fun("map.getELO2",
        [
            new MethodParameter("类型@Type", "String", Alias: "Type"),
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true),
            new MethodParameter("通知参数@Attach", "Dict", Alias: "Attach")
        ], null, "获取玩家新等级分", notes: "回调模式：数据到达后调用同零件上 CallBackMethod 方法，Attach 附加到回调参数");
        yield return Fun("shader.setGlobalVector",
        [
            new MethodParameter("属性名@name", "String", Alias: "name"),
            new MethodParameter("数值@value", "Vector3", Alias: "value")
        ], null, "设置全局 Shader 向量参数");
        yield return Fun("shader.setGlobalFloat",
        [
            new MethodParameter("属性名@name", "String", Alias: "name"),
            new MethodParameter("数值@value", "Float", Alias: "value")
        ], null, "设置全局 Shader 小数参数");
        yield return Fun("shader.setGlobalColor",
        [
            new MethodParameter("属性名@name", "String", Alias: "name"),
            new MethodParameter("数值@value", "Color", Alias: "value")
        ], null, "设置全局 Shader 颜色参数");
        yield return Fun("shader.setGlobalTex",
        [
            new MethodParameter("属性名@name", "String", Alias: "name"),
            new MethodParameter("贴图@tex", "UIRawImage", Alias: "tex")
        ], null, "设置全局 Shader 贴图参数");
        yield return Fun("map.searchELOAsync",
        [
            new MethodParameter("类型@Type", "String", Alias: "Type"),
            new MethodParameter("随机范围@Range", "Long", Alias: "Range")
        ], "Dict", "异步搜索新等级分接近的玩家", asyncMode: SdkAsyncMode.Async,
            notes: "返回 Dict 字段：$player(Player)、$elo(Long)、$data(Dict，玩家存档)、$error(String)。");
        yield return Fun("map.searchELO2",
        [
            new MethodParameter("类型@Type", "String", Alias: "Type"),
            new MethodParameter("随机范围@Range", "Long", Alias: "Range"),
            new MethodParameter("通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true),
            new MethodParameter("通知参数@Attach", "Dict", Alias: "Attach")
        ], null, "搜索新等级分接近的玩家", notes: "回调模式：数据到达后调用同零件上 CallBackMethod 方法，Attach 附加到回调参数");
        yield return Fun("map.clearAllELO2", [new MethodParameter("类型@Type", "String", Alias: "Type")], null, "清空全部新等级分");

        // 交易所
        yield return Fun("map.marketSet",
        [
            new MethodParameter("类型@Type", "String", Alias: "Type"),
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("序号@Index", "Long", Alias: "Index"),
            new MethodParameter("标题@Title", "String", Alias: "Title"),
            new MethodParameter("数据@Data", "Dict", Alias: "Data"),
            new MethodParameter("属性列表@Attrs", "List", Alias: "Attrs")
        ], null, "设置交易所商品");
        yield return Fun("map.marketDel",
        [
            new MethodParameter("类型@Type", "String", Alias: "Type"),
            new MethodParameter("ID@ID", "Long", Alias: "ID")
        ], null, "删除交易所商品");
        yield return Fun("map.marketSearchAsync",
        [
            new MethodParameter("类型@Type", "String", Alias: "Type"),
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("属性最小值列表@Mins", "List", Alias: "Mins"),
            new MethodParameter("属性最大值列表@Mins", "List", Alias: "Maxs"),
            new MethodParameter("分页@Page", "Float", Alias: "Page"),
            new MethodParameter("最大数量@Count", "Float", Alias: "Count")
        ], "List", "异步交易所搜索", asyncMode: SdkAsyncMode.Async);
        yield return Fun("map.marketSearch",
        [
            new MethodParameter("通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true),
            new MethodParameter("类型@Type", "String", Alias: "Type"),
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("属性最小值列表@Mins", "List", Alias: "Mins"),
            new MethodParameter("属性最大值列表@Mins", "List", Alias: "Maxs"),
            new MethodParameter("通知参数@Attach", "Dict", Alias: "Attach"),
            new MethodParameter("分页@Page", "Float", Alias: "Page"),
            new MethodParameter("最大数量@Count", "Float", Alias: "Count")
        ], null, "交易所搜索", notes: "回调模式：数据到达后调用同零件上 CallBackMethod 方法，Attach 附加到回调参数");
        yield return Fun("map.marketClearAll", [new MethodParameter("类型@Type", "String", Alias: "Type")], null, "交易所清空");

        // 表格
        yield return Fun("map.excelGetColNameList", [new MethodParameter("Excel文件名@Excel File", "String", Alias: "Excel_File")], "List", "获取表格列名列表");
        yield return Fun("map.excelGetColTypeList2",
        [
            new MethodParameter("列名@alias", "String", Alias: "alias"),
            new MethodParameter("Excel文件名@Excel File", "String", Alias: "Excel_File")
        ], "String", "获取表格列类型列表");

        // 其他地图功能
        yield return Fun("map.PVPEventReq", [new MethodParameter("消息@msg", "String", Alias: "msg")], null, "上传用户行为日志");
        yield return Fun("map.addToCollection", [], null, "收藏地图");
        yield return Fun("map.setRoomState", [new MethodParameter("是否允许@state", "Bool", Alias: "state")], null, "开关玩家加入房间");
        yield return Fun("map.ByteGameAddShotcut", [], null, "抖音小游戏添加到桌面", notes: "抖音小游戏平台专用");
        yield return Fun("map.ByteGameExistShotcut", [], "Bool", "抖音小游戏是否已添加到桌面", asyncMode: SdkAsyncMode.Async, notes: "抖音小游戏平台专用");
        yield return Fun("map.ByteGameSidebarExist", [], "Bool", "抖音小游戏是否支持侧边栏", asyncMode: SdkAsyncMode.Async, notes: "抖音小游戏平台专用");
        yield return Fun("map.ByteGameIsFromSidebar", [], "Bool", "抖音小游戏是否从侧边栏加载", asyncMode: SdkAsyncMode.Async, notes: "抖音小游戏平台专用");
        yield return Fun("map.ByteGameNavigateToSidebar", [], null, "抖音小游戏跳转侧边栏", notes: "抖音小游戏平台专用");
        yield return Fun("map.ByteGameJoinGroup", [new MethodParameter("id@id", "String", Alias: "id")], null, "抖音小游戏加入粉丝群", notes: "抖音小游戏平台专用");
        yield return Fun("map.MiniGameGetUserInfo", [], "Dict", "微信得到用户信息", asyncMode: SdkAsyncMode.Async, notes: "微信小游戏平台专用；返回含 nickname:String、avatarURL:String 的 Dict");
        yield return Fun("VolumeFogSet",
        [
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("半径@radius", "Float", Alias: "radius"),
            new MethodParameter("浓度@alpha", "Float", Alias: "alpha"),
            new MethodParameter("过度时间@duration", "Float", Alias: "duration")
        ], null, "体积雾轨迹");
    }

    /// <summary>MapMech 相关全局函数</summary>
    private static IEnumerable<MethodSignature> GetMapMechFunctions()
    {
        // 机械生成与列表
        yield return Fun("createPlayerMech",
        [
            new MethodParameter("机械产生器别名@alias", "String", Alias: "alias"),
            new MethodParameter("玩家@player", "Player", Alias: "player"),
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("朝向@forward", "Vector3", Alias: "forward"),
            new MethodParameter("对齐方式@posType", "Enum", EnumType: CreateMechPositionAlignment, Alias: "posType")
        ], "Mech", "产生玩家机械", notes: "alias 为地图中机械产生器的逻辑名称，非机械文件名；产生器需事先在地图中放置，可用 changeMech 切换机械文件；产生后用 getGenerateMech 获取引用；position/forward 参数就是生成位姿，返回后不要在同一调用栈立刻用 Mech.move/Mech.rot 或 Device.setPosKinematic/Device.setRotKinematic 二次覆盖");

        yield return Fun("createAIMech",
        [
            new MethodParameter("机械产生器别名@alias", "String", Alias: "alias"),
            new MethodParameter("阵营@team", "Float", Alias: "team"),
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("朝向@forward", "Vector3", Alias: "forward"),
            new MethodParameter("对齐方式@posType", "Enum", EnumType: CreateMechPositionAlignment, Alias: "posType"),
            new MethodParameter("本地模拟@ManualSync", "Bool", Alias: "ManualSync")
        ], "Mech", "产生AI机械", notes: "alias 为地图中机械产生器的逻辑名称，非机械文件名；产生器需事先在地图中放置，可用 changeMech 切换机械文件；产生后用 getGenerateMech 获取引用；position/forward 参数就是生成位姿，返回后不要在同一调用栈立刻用 Mech.move/Mech.rot 或 Device.setPosKinematic/Device.setRotKinematic 二次覆盖");

        yield return Fun("getMechSnapshot",
        [
            new MethodParameter("机械产生器别名@alias", "String", Alias: "alias"),
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos")
        ], "UIRawImage", "获取机械截图");

        yield return Fun("map.getMapMech", [], "Mech", "获取地图机械");
        yield return Fun("map.mechList", [], "List", "获取机械列表");
        yield return Fun("map.delAllMechs", [], null, "删除所有机械");
        yield return Fun("map.setMechNumLimit", [new MethodParameter("个数@numLimit", "Float", Alias: "numLimit")], null, "设置最大机械数");
        yield return Fun("map.excelGetIDList2", [new MethodParameter("Excel文件名@Excel File", "String", Alias: "Excel_File")], "List", "获取表格 ID 列表");


        // 表格读取/搜索（readExcel2_* / findExcel2_*）
        // 注意：findExcel2 的 key 参数禁止使用 "id"，该 API 返回的就是行 ID，用 id 搜索属于逻辑错误
        string[] readExcel2Types = ["Bool", "Color", "Float", "Long", "String", "Vector3"];
        string[] findExcel2Types = ["Bool", "Color", "Float", "Long", "String", "Vector3"];
        CompressionHint readExcel2Hint = new(CompressionKind.TypeSuffix, "readExcel2", readExcel2Types);
        CompressionHint findExcel2Hint = new(CompressionKind.TypeSuffixParam, "findExcel2", findExcel2Types);
        yield return Fun("readExcel2_Long",
        [
            new MethodParameter("ID", "Long"),
            new MethodParameter("列名@alias", "String", Alias: "alias"),
            new MethodParameter("Excel文件名@Excel File", "String", Alias: "Excel_File")
        ], "Long", "读取表格（Long）", compressionHint: readExcel2Hint);
        yield return Fun("readExcel2_Float",
        [
            new MethodParameter("ID", "Long"),
            new MethodParameter("列名@alias", "String", Alias: "alias"),
            new MethodParameter("Excel文件名@Excel File", "String", Alias: "Excel_File")
        ], "Float", "读取表格（Float）", compressionHint: readExcel2Hint);
        yield return Fun("readExcel2_String",
        [
            new MethodParameter("ID", "Long"),
            new MethodParameter("列名@alias", "String", Alias: "alias"),
            new MethodParameter("Excel文件名@Excel File", "String", Alias: "Excel_File")
        ], "String", "读取表格（String）", compressionHint: readExcel2Hint);
        yield return Fun("readExcel2_Bool",
        [
            new MethodParameter("ID", "Long"),
            new MethodParameter("列名@alias", "String", Alias: "alias"),
            new MethodParameter("Excel文件名@Excel File", "String", Alias: "Excel_File")
        ], "Bool", "读取表格（Bool）", compressionHint: readExcel2Hint);
        yield return Fun("readExcel2_Vector3",
        [
            new MethodParameter("ID", "Long"),
            new MethodParameter("列名@alias", "String", Alias: "alias"),
            new MethodParameter("Excel文件名@Excel File", "String", Alias: "Excel_File")
        ], "Vector3", "读取表格（Vector3）", compressionHint: readExcel2Hint);
        yield return Fun("readExcel2_Color",
        [
            new MethodParameter("ID", "Long"),
            new MethodParameter("列名@alias", "String", Alias: "alias"),
            new MethodParameter("Excel文件名@Excel File", "String", Alias: "Excel_File")
        ], "Color", "读取表格（Color）", compressionHint: readExcel2Hint);
        yield return Fun("findExcel2_Long",
        [
            new MethodParameter("列名@key", "String", Alias: "key"),
            new MethodParameter("值@value", "Long", Alias: "value"),
            new MethodParameter("Excel文件名@Excel File", "String", Alias: "Excel_File")
        ], "Long", "搜索表格并返回行 ID（Long）。注意：key 禁止用 'id'，该 API 返回的就是行 ID", compressionHint: findExcel2Hint);
        yield return Fun("findExcel2_Float",
        [
            new MethodParameter("列名@key", "String", Alias: "key"),
            new MethodParameter("值@value", "Float", Alias: "value"),
            new MethodParameter("Excel文件名@Excel File", "String", Alias: "Excel_File")
        ], "Long", "搜索表格并返回行 ID（Float）。注意：key 禁止用 'id'，该 API 返回的就是行 ID", compressionHint: findExcel2Hint);
        yield return Fun("findExcel2_String",
        [
            new MethodParameter("列名@key", "String", Alias: "key"),
            new MethodParameter("值@value", "String", Alias: "value"),
            new MethodParameter("Excel文件名@Excel File", "String", Alias: "Excel_File")
        ], "Long", "搜索表格并返回行 ID（String）。注意：key 禁止用 'id'，该 API 返回的就是行 ID", compressionHint: findExcel2Hint);
        yield return Fun("findExcel2_Bool",
        [
            new MethodParameter("列名@key", "String", Alias: "key"),
            new MethodParameter("值@value", "Bool", Alias: "value"),
            new MethodParameter("Excel文件名@Excel File", "String", Alias: "Excel_File")
        ], "Long", "搜索表格并返回行 ID（Bool）。注意：key 禁止用 'id'，该 API 返回的就是行 ID", compressionHint: findExcel2Hint);
        yield return Fun("findExcel2_Vector3",
        [
            new MethodParameter("列名@key", "String", Alias: "key"),
            new MethodParameter("值@value", "Vector3", Alias: "value"),
            new MethodParameter("Excel文件名@Excel File", "String", Alias: "Excel_File")
        ], "Long", "搜索表格并返回行 ID（Vector3）。注意：key 禁止用 'id'，该 API 返回的就是行 ID", compressionHint: findExcel2Hint);
        yield return Fun("findExcel2_Color",
        [
            new MethodParameter("列名@key", "String", Alias: "key"),
            new MethodParameter("值@value", "Color", Alias: "value"),
            new MethodParameter("Excel文件名@Excel File", "String", Alias: "Excel_File")
        ], "Long", "搜索表格并返回行 ID（Color）。注意：key 禁止用 'id'，该 API 返回的就是行 ID", compressionHint: findExcel2Hint);

        // 阵营机械检索
        yield return Fun("mech.teamMechs", [new MethodParameter("阵营序号@teamIndex", "Float", Alias: "teamIndex")], "List", "获取阵营机械列表");
        yield return Fun("mech.getNearestTeamMech",
        [
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("阵营序号@teamIndex", "Float", Alias: "teamIndex")
        ], "Mech", "获取距离最近的阵营机械");
        yield return Fun("mech.getNearestEnemyMech",
        [
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("排除阵营@exceptTeam", "Float", Alias: "exceptTeam")
        ], "Mech", "获取距离最近的敌方机械");

        // 编辑器 Mod
        yield return Fun("mech.deviceNum", [], "Float", "得到零件个数");
        yield return Fun("mech.getDevice", [new MethodParameter("序号@index", "Float", Alias: "index")], "Device", "得到零件");
        yield return Fun("mech.focusDevice", [], "Device", "当前选中零件");
        yield return Fun("mech.deviceIs", [new MethodParameter("零件@device", "Device", Alias: "device"), new MethodParameter("类型ID", "Float")], "Bool", "零件是否指定类型");
        yield return Fun("mech.deviceNotNull", [new MethodParameter("零件@device", "Device", Alias: "device")], "Bool", "零件存在");
        yield return Fun("mech.deviceEqual", [new MethodParameter("零件1@device1", "Device", Alias: "device1"), new MethodParameter("零件2@device2", "Device", Alias: "device2")], "Bool", "是否同一个零件");
        yield return Fun("mech.addDevice", [new MethodParameter("类型@type", "Float", Alias: "type"), new MethodParameter("坐标@pos", "Vector3", Alias: "pos")], "Device", "生成新零件", notes: "向当前编辑中的机械添加零件，编辑器专用");
        yield return Fun("mech.delDevice", [new MethodParameter("零件@device", "Device", Alias: "device")], "Device", "删除零件", notes: "核心零件不可删除；编辑器专用");
        yield return Fun("mech.hideDevice", [new MethodParameter("零件@device", "Device", Alias: "device")], null, "隐藏零件", notes: "彻底禁用零件（渲染/物理/脚本全部停止），与 Device.show 仅关闭渲染不同");
        yield return Fun("mech.showDevice", [new MethodParameter("零件@device", "Device", Alias: "device")], null, "显示零件", notes: "恢复被 hideDevice 禁用的零件");
        yield return Fun("mech.showHideAllDevice", [], null, "开关全部零件", notes: "整机级别，影响机械内所有零件");
        yield return Fun("mech.raycast", [], "Bool", "是否触摸到零件");
        yield return Fun("mech.raycastPos", [], "Vector3", "得到触摸到零件的表面坐标");
        yield return Fun("mech.raycastNormal", [], "Vector3", "得到触摸到零件的表面方向");
        yield return Fun("mech.canClickDevice", [new MethodParameter("开关@OnOff", "Bool", Alias: "OnOff")], null, "开关点击选择零件");
        yield return Fun("mech.addStep", [], null, "保存可撤销状态");
        yield return Fun("mech.undo", [], null, "撤销一步");
        yield return Fun("mech.clone", [new MethodParameter("零件@device", "Device", Alias: "device")], "Device", "复制零件");
        yield return Fun("mech.addDeviceLogic",
        [
            new MethodParameter("零件@device", "Device", Alias: "device"),
            new MethodParameter("事件名@logicName", "String", Alias: "logicName"),
            new MethodParameter("代码文字@Code", "String", Alias: "Code")
        ], null, "添加零件逻辑代码");
        yield return Fun("mech.addSelection", [new MethodParameter("零件@device", "Device", Alias: "device")], null, "添加到多选列表");
        yield return Fun("mech.removeSelection", [new MethodParameter("零件@device", "Device", Alias: "device")], null, "从多选列表中去除");
        yield return Fun("mech.selectionNum", [], "Float", "得到多选个数");
        yield return Fun("mech.selectionGetIndex", [new MethodParameter("序号@index", "Float", Alias: "index")], "Device", "得到多选列表中的零件");
        yield return Fun("mech.clearSelection", [], null, "清空多选列表");
        yield return Fun("editorAnchor.lock", [new MethodParameter("是否禁止@boolLock", "Bool", Alias: "boolLock")], null, "锁定视角");
        yield return Fun("editorEvent.doEvent", [new MethodParameter("事件名@eventName", "String", Alias: "eventName")], null, "执行编辑器事件");

        // 动态拼装
        yield return Fun("map.getTemplateMech", [], "Mech", "获取模板机械");
        yield return Fun("map.dynAddDevice",
        [
            new MethodParameter("目标机械@targetMech", "Mech", Alias: "targetMech"),
            new MethodParameter("模板零件序号@index", "Float", Alias: "index"),
            new MethodParameter("位置@pos", "Vector3", Alias: "pos"),
            new MethodParameter("向前方向@forward", "Vector3", Alias: "forward"),
            new MethodParameter("向上方向@up", "Vector3", Alias: "up"),
            new MethodParameter("缩放@scale", "Float", Alias: "scale"),
            new MethodParameter("黏连零件@attachDevice", "Device", Alias: "attachDevice")
        ], "Device", "动态拼装创建零件");

        yield return Fun("dyn.save", [new MethodParameter("目标机械@targetMech", "Mech", Alias: "targetMech")], "List", "保存动态拼装信息");
        yield return Fun("dyn.load",
        [
            new MethodParameter("目标机械@targetMech", "Mech", Alias: "targetMech"),
            new MethodParameter("数据@data", "List", Alias: "data")
        ], null, "加载动态拼装信息",
            notes: "加载的是 dyn.save 产生的动态拼装数据，用于恢复运行时零件拼装；不是模型资源加载。");
        yield return Fun("dyn.load_remote",
        [
            new MethodParameter("目标机械@targetMech", "Mech", Alias: "targetMech"),
            new MethodParameter("数据@data", "List", Alias: "data"),
            new MethodParameter("creatorID", "Long")
        ], null, "动态拼装数据同步", "多人同步相关接口，通常不需要直接调用；data 可能较大，注意调用频率。",
            notes: "同步的是动态拼装数据；不是模型资源加载。");
        yield return Fun("dyn.getSnapshot",
        [
            new MethodParameter("目标机械@targetMech", "Mech", Alias: "targetMech"),
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("距离系数@distanceRate", "Float", Alias: "distanceRate"),
            new MethodParameter("分辨率@resolution", "Vector3", Alias: "resolution")
        ], "UIRawImage", "获取动态拼装截图");

        // 邮件
        yield return Fun("mail.send",
        [
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("类型@Type", "Long", Alias: "Type"),
            new MethodParameter("正文@Content", "String", Alias: "Content"),
            new MethodParameter("附件@Attach", "Dict", Alias: "Attach")
        ], null, "发送邮件");
        yield return Fun("mail.getListAsync", [], "List", "异步获取邮件列表", asyncMode: SdkAsyncMode.Async);
        yield return Fun("mail.del", [new MethodParameter("邮件ID@MailID", "Long", Alias: "MailID")], null, "删除邮件");
        yield return Fun("mail.clear", [], null, "清空邮箱");

        // 公会
        yield return Fun("guid.createAsync",
        [
            new MethodParameter("名称@name", "String", Alias: "name")
        ], "Struct", "异步创建公会", asyncMode: SdkAsyncMode.Async,
            notes: "返回 Struct 字段：guidID(Long)、error(String)。");
        yield return Fun("guid.getStateAsync", [new MethodParameter("公会ID@GuidID", "Long", Alias: "GuidID")], "Struct", "异步获取公会状态", asyncMode: SdkAsyncMode.Async,
            notes: "返回 Struct 字段：name(String)、slogan(String)、shareData(Dict)、members(Float)。");
        yield return Fun("guid.sendMail",
        [
            new MethodParameter("公会ID@GuidID", "Long", Alias: "GuidID"),
            new MethodParameter("类型@type", "Long", Alias: "type"),
            new MethodParameter("正文@Word", "String", Alias: "Word"),
            new MethodParameter("附件@attach", "Dict", Alias: "attach")
        ], null, "发送公会邮件");
        yield return Fun("guid.talk",
        [
            new MethodParameter("公会ID@GuidID", "Long", Alias: "GuidID"),
            new MethodParameter("正文@Word", "String", Alias: "Word"),
            new MethodParameter("附件@attach", "Dict", Alias: "attach")
        ], null, "公会频道聊天");
        yield return Fun("guid.getAllAsync",
        [
            new MethodParameter("分页@Page", "Float", Alias: "Page"),
            new MethodParameter("每页数量@Limit", "Float", Alias: "Limit")
        ], "Struct", "异步获取所有公会列表", asyncMode: SdkAsyncMode.Async,
            notes: "返回 Struct 字段：$list(List，元素为 Struct 含 guidID:Long, name:String, members:Long, desc:Dict)、$TotalNum(Float)。");
    }

    /// <summary>UI/Screen/Input 相关全局函数</summary>
    private static IEnumerable<MethodSignature> GetUIScreenInputFunctions()
    {
        // UI 控件创建
        yield return Fun("ui.button",
        [
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("尺寸@size", "Vector3", Alias: "size"),
            new MethodParameter("颜色@color", "Color", Alias: "color"),
            new MethodParameter("标题@title", "String", AllowImplicitToString: true, Alias: "title"),
            new MethodParameter("圆角@radius", "Float", Alias: "radius"),
            new MethodParameter("厚度@thickness", "Float", Alias: "thickness")
        ], "UIButton", "创建按钮",
            notes: "pos 为 0~1 屏幕坐标；size 为 UI 布局尺寸，不是屏幕像素。");

        yield return Fun("ui.text",
        [
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("尺寸@size", "Float", Alias: "size"),
            new MethodParameter("颜色@color", "Color", Alias: "color"),
            new MethodParameter("标题@title", "String", AllowImplicitToString: true, Alias: "title"),
            new MethodParameter("类型@type", "Enum", EnumType: UiTextType, Alias: "type")
        ], "UILabel", "创建文本",
            notes: "pos 为 0~1 屏幕坐标；size 为字号，不是 UI 布局尺寸。高级特效类型不支持生僻字且消耗更高。");

        yield return Fun("ui.inputField",
        [
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("尺寸@size", "Vector3", Alias: "size"),
            new MethodParameter("颜色@color", "Color", Alias: "color"),
            new MethodParameter("标题@title", "String", AllowImplicitToString: true, Alias: "title")
        ], "UIInput", "创建输入框",
            notes: "pos 为 0~1 屏幕坐标；size 为 UI 布局尺寸，不是屏幕像素。");

        yield return Fun("ui.rect",
        [
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("尺寸@size", "Vector3", Alias: "size"),
            new MethodParameter("圆角半径@radius", "Float", Alias: "radius"),
            new MethodParameter("厚度@thickness", "Float", Alias: "thickness"),
            new MethodParameter("颜色@color", "Color", Alias: "color")
        ], "UIRect", "创建矩形",
            notes: "pos 为 0~1 屏幕坐标；size 为 UI 布局尺寸，不是屏幕像素。");

        yield return Fun("ui.line",
        [
            new MethodParameter("起点@startPos", "Vector3", Alias: "startPos"),
            new MethodParameter("终点@endPos", "Vector3", Alias: "endPos"),
            new MethodParameter("宽度@thickness", "Float", Alias: "thickness"),
            new MethodParameter("颜色@color", "Color", Alias: "color")
        ], "UILine", "创建线条",
            notes: "startPos/endPos 为 0~1 屏幕坐标；thickness 为 UI 布局宽度，不是屏幕像素。");

        yield return Fun("ui.rawImage",
        [
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("尺寸@size", "Vector3", Alias: "size"),
            new MethodParameter("分辨率@pixels", "Vector3", Alias: "pixels")
        ], "UIRawImage", "创建图片",
            notes: "pos 为 0~1 屏幕坐标；size 为 UI 布局尺寸，不是屏幕像素；pixels 为图片纹理分辨率。");

        yield return Fun("ui.newAvatar",
        [
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("尺寸@size", "Float", Alias: "size"),
            new MethodParameter("玩家@player", "Player", Alias: "player")
        ], "UIAvatar", "创建玩家头像",
            notes: "pos 为 0~1 屏幕坐标；size 为 UI 布局边长，不是屏幕像素。");
        yield return Fun("ui.newGuidAvatar",
        [
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("尺寸@size", "Float", Alias: "size"),
            new MethodParameter("公会ID@GuidID", "Long", Alias: "GuidID")
        ], "UIRawImage", "创建公会旗帜",
            notes: "pos 为 0~1 屏幕坐标；size 为 UI 布局边长，不是屏幕像素。");
        yield return Fun("ui.newDanmuAvatar",
        [
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("尺寸@size", "Float", Alias: "size"),
            new MethodParameter("玩家UID@UID", "String", Alias: "UID")
        ], "UIRawImage", "创建弹幕观众头像",
            notes: "pos 为 0~1 屏幕坐标；size 为 UI 布局边长，不是屏幕像素。");
        yield return Fun("findCanvas", [new MethodParameter(string.Empty, "String", Alias: "name")], "Canvas", "查找 UI 布局", notes: "查找静态 Canvas 布局资源；返回的 Canvas 不是已经显示在屏幕上的 UI 实例。");
        yield return Fun("ui.2dmode",
        [
            new MethodParameter("开启@enable", "Bool", Alias: "enable"),
            new MethodParameter("3DUI距离", "Float")
        ], null, "切换 UI 2D 模式", notes: "同时有强制设置 3D UI 距离的副作用，注意管理 3D UI 距离。");

        // Screen/Camera
        yield return Fun("screen.size", [], "Vector3", "获取屏幕分辨率", notes: "返回当前 Screen.width/height 像素尺寸。它不是 UI 布局坐标系尺寸。");
        yield return Fun("screen.uiSize", [], "Vector3", "获取 UI 尺寸", notes: "返回覆盖全屏所需的 UI 布局尺寸，由当前 Canvas 高度和屏幕宽高比计算；不是屏幕分辨率。");
        yield return Fun("screen.toPixel", [new MethodParameter("坐标@pos", "Vector3", Alias: "pos")], "Vector3", "屏幕坐标转像素", notes: "输入为 0~1 屏幕坐标，返回 Screen.width/height 像素坐标。");
        yield return Fun("screen.fromPixel", [new MethodParameter("像素坐标@pixelPos", "Vector3", Alias: "pixelPos")], "Vector3", "像素转屏幕坐标", notes: "输入为 Screen.width/height 像素坐标，返回 0~1 屏幕坐标。mouse.pos 返回像素坐标，传给 UI.isPosInside/UI.posToLocal 前应先用此方法转换。");
        yield return Fun("screen.WorldToScreenPoint", [new MethodParameter("世界坐标@pos", "Vector3", Alias: "pos")], "Vector3", "世界坐标转屏幕坐标", notes: "返回像素坐标，不是 0~1 屏幕坐标。");
        yield return Fun("screen.ScreenToWorldPoint", [new MethodParameter("屏幕坐标@screenPos", "Vector3", Alias: "screenPos")], "Vector3", "屏幕坐标转世界坐标", notes: "输入为像素坐标，不是 0~1 屏幕坐标。");
        yield return Fun("screen.setResolution",
        [
            new MethodParameter("宽度@Width", "Float", Alias: "Width"),
            new MethodParameter("高度@Height", "Float", Alias: "Height")
        ], null, "设置分辨率", notes: "Width/Height 为屏幕像素尺寸，只设置 Screen 分辨率；不会把 UI 布局尺寸改成同样数值。");
        yield return Fun("screen.RenderScale", [new MethodParameter("比例@scale", "Float", Alias: "scale")], null, "设置渲染比例", notes: "scale 范围 [0.1,1]。只影响渲染比例，不改变屏幕分辨率或 UI 布局坐标系。");
        yield return Fun("screen.shake",
        [
            new MethodParameter("力度@force", "Float", Alias: "force"),
            new MethodParameter("弹性@spring", "Float", Alias: "spring"),
            new MethodParameter("阻尼@damping", "Float", Alias: "damping")
        ], null, "屏幕震动");
        yield return Fun("shakePhone", [new MethodParameter("秒数@seconds", "Float", Alias: "seconds")], null, "手机震动");

        yield return Fun("camera.pos", [], "Vector3", "获取摄像机位置");
        yield return Fun("camera.foward", [], "Vector3", "获取摄像机前向");
        yield return Fun("camera.2d",
        [
            new MethodParameter("开关@onOff", "Bool", Alias: "onOff"),
            new MethodParameter("大小@size", "Float", Alias: "size"),
            new MethodParameter("摄像机序号@index", "Float", Alias: "index")
        ], null, "设置摄像机 2D 模式", notes: "size 直接写入正交相机的 orthographicSize，表示摄像机局部 Y 方向从中心到上下边缘的半高度；完整竖向视野高度为 size * 2，横向视野宽度为 size * 2 * 摄像机宽高比。它不是完整宽度，也不是屏幕像素尺寸。");
        yield return Fun("cameraClipPlane",
        [
            new MethodParameter("近平面@nearClipPlane", "Float", Alias: "nearClipPlane"),
            new MethodParameter("远平面@farClipPlane", "Float", Alias: "farClipPlane"),
            new MethodParameter("视角@fieldOfView", "Float", Alias: "fieldOfView"),
            new MethodParameter("摄像机序号@index", "Float", Alias: "index")
        ], null, "设置摄像机裁剪面");
        yield return Fun("cameraShot",
        [
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("尺寸@size", "Vector3", Alias: "size")
        ], "UIRawImage", "摄像机截图",
            notes: "pos 为 0~1 屏幕坐标；size 为 UI 布局尺寸，不是屏幕像素。");

        // 物理检测
        yield return Fun("overlapBoxDevice",
        [
            new MethodParameter("中心点@center", "Vector3", Alias: "center"),
            new MethodParameter("尺寸@size", "Vector3", Alias: "size"),
            new MethodParameter("向前方向@forward", "Vector3", Alias: "forward"),
            new MethodParameter("向上方向@up", "Vector3", Alias: "up"),
            new MethodParameter("每个机械只判断一次@Once Per Mech", "Bool", Alias: "Once_Per_Mech"),
            new MethodParameter("检测层列表", "List", Alias: "layers")
        ], "List", "盒形区域检测零件",
            notes: "检测层列表元素必须为 Float（平台层编号，有效范围 0-23）；空列表沿用当前检测层设置。");
        yield return Fun("overlapSphereDevice",
        [
            new MethodParameter("中心点@center", "Vector3", Alias: "center"),
            new MethodParameter("半径@radius", "Float", Alias: "radius"),
            new MethodParameter("每个机械只判断一次@Once Per Mech", "Bool", Alias: "Once_Per_Mech"),
            new MethodParameter("检测层列表", "List", Alias: "layers")
        ], "List", "球形区域检测零件",
            notes: "检测层列表元素必须为 Float（平台层编号，有效范围 0-23）；空列表沿用当前检测层设置。");
        yield return Fun("overlapCircleDevice",
        [
            new MethodParameter("中心点@center", "Vector3", Alias: "center"),
            new MethodParameter("半径@radius", "Float", Alias: "radius"),
            new MethodParameter("每个机械只判断一次@Once Per Mech", "Bool", Alias: "Once_Per_Mech"),
            new MethodParameter("检测层列表", "List", Alias: "layers")
        ], "List", "圆形区域检测零件",
            notes: "检测层列表元素必须为 Float（平台层编号，有效范围 0-23）；空列表沿用当前检测层设置。");
        yield return Fun("overlapCapsuleDevice",
        [
            new MethodParameter("点1@point1", "Vector3", Alias: "point0"),
            new MethodParameter("点2@point2", "Vector3", Alias: "point1"),
            new MethodParameter("半径@radius", "Float", Alias: "radius"),
            new MethodParameter("每个机械只判断一次@Once Per Mech", "Bool", Alias: "Once_Per_Mech"),
            new MethodParameter("检测层列表", "List", Alias: "layers")
        ], "List", "胶囊区域检测零件",
            notes: "检测层列表元素必须为 Float（平台层编号，有效范围 0-23）；空列表沿用当前检测层设置。");
        yield return Fun("raycast2",
        [
            new MethodParameter("射线起点@origin", "Vector3", Alias: "origin"),
            new MethodParameter("射线方向@direction", "Vector3", Alias: "direction"),
            new MethodParameter("检测层列表", "List", Alias: "layers")
        ], "Struct", "射线检测（返回位置和法线）",
            notes: "返回 Struct 字段：pos(Vector3)、normal(Vector3)。检测层列表元素必须为 Float（平台层编号，有效范围 0-23）；空列表沿用当前检测层设置。");
        yield return Fun("raycastDevice2",
        [
            new MethodParameter("射线起点@origin", "Vector3", Alias: "origin"),
            new MethodParameter("射线方向@direction", "Vector3", Alias: "direction"),
            new MethodParameter("检测层列表", "List", Alias: "layers")
        ], "Struct", "射线检测零件（返回位置、法线和零件）",
            notes: "返回 Struct 字段：pos(Vector3)、normal(Vector3)、device(Device)。未命中时 pos 为起点沿方向无限远处，device 为空。检测层列表元素必须为 Float（平台层编号，有效范围 0-23）；空列表沿用当前检测层设置。");
        yield return Fun("raycast2DDevice",
        [
            new MethodParameter("起点@start", "Vector3", Alias: "start"),
            new MethodParameter("终点@end", "Vector3", Alias: "end"),
            new MethodParameter("检测层列表", "List", Alias: "layers")
        ], "Device", "2D 射线检测零件",
            notes: "检测层列表元素必须为 Float（平台层编号，有效范围 0-23）；空列表沿用当前检测层设置。");
        yield return Fun("plane/raycast",
        [
            new MethodParameter("射线起点@origin", "Vector3", Alias: "origin"),
            new MethodParameter("射线方向@direction", "Vector3", Alias: "direction"),
            new MethodParameter("平面上的一个点@inPoint", "Vector3", Alias: "inPoint"),
            new MethodParameter("与平面垂直方向@inNormal", "Vector3", Alias: "inNormal")
        ], "Vector3", "射线与平面交点");
        yield return Fun("edgeDetect",
        [
            new MethodParameter("开关@onOff", "Bool", Alias: "onOff"),
            new MethodParameter("厚度@thick", "Float", Alias: "thick"),
            new MethodParameter("颜色@color", "Color", Alias: "color"),
            new MethodParameter("层列表@layers", "List", Alias: "layers")
        ], null, "开关边缘检测",
            notes: "层列表元素必须为 Float（平台层编号，有效范围 0-23）；空列表表示不匹配任何层。");

        // Input
        yield return Fun("input.Location", [], "Vector3", "经纬度", asyncMode: SdkAsyncMode.Async,
            notes: "返回 Vector3：x=经度，y=纬度，z=0；平台获取失败时返回 (0,0,0)。");
        yield return Fun("input.touchPos", [], "Vector3", "获取触摸/鼠标位置", notes: "返回 0~1 屏幕坐标。不要与 mouse.pos 的像素坐标混用。");
        yield return Fun("input.touchCount", [], "Float", "获取触摸数量");
        yield return Fun("input.touchPosByIndex", [new MethodParameter("序号@index", "Float", Alias: "index")], "Vector3", "获取指定触摸位置", notes: "返回 0~1 屏幕坐标。");
        yield return Fun("input.pressPosByIndex", [new MethodParameter("序号@index", "Float", Alias: "index")], "Vector3", "获取指定触摸点按下位置", notes: "返回 0~1 屏幕坐标。");
        yield return Fun("input.deltaByIndex", [new MethodParameter("序号@index", "Float", Alias: "index")], "Vector3", "获取指定触摸点拖动增量", notes: "返回按屏幕宽高归一化后的拖动增量。");
        yield return Fun("input.press", [], "Bool", "当帧是否有按下");
        yield return Fun("input.release", [], "Bool", "当帧是否有松开");
        yield return Fun("input.overUI", [new MethodParameter("序号@index", "Float", Alias: "index")], "Bool", "触摸是否在 UI 上", notes: "index 是触摸点序号，从 0 开始；不接收屏幕坐标。");
        yield return Fun("input.enableGyro", [new MethodParameter("开启@enable", "Bool", Alias: "enable")], null, "开关陀螺仪");
        yield return Fun("input.gravity", [], "Vector3", "获取重力方向");
        yield return Fun("input.attitude", [new MethodParameter("局部方向@Dir", "Vector3", Alias: "Dir")], "Vector3", "手机局部方向变换到世界方向");
        // 注意：这里的"设备"指物理设备（手机），不是 Device（零件）
        yield return Fun("input.userAcceleration", [], "Vector3", "获取设备加速度", "需要先开启陀螺仪（input.enableGyro）。");
        yield return Fun("input.Gyroscope", [], "Vector3", "获取设备姿态角", "需要先开启陀螺仪（input.enableGyro）。");
        yield return Fun("input.setTouchCB", [new MethodParameter("回调方法", "String", IsScriptMethodName: true)], null, "设置触摸回调");

        // Keyboard
        yield return Fun("keyboard.anyKeyDown", [], "Float", "获取任意按下的键");
        yield return Fun("keyboard.keyDown", [new MethodParameter("键值@keycode", "Float", Alias: "keycode")], "Bool", "键值按下瞬间");
        yield return Fun("keyboard.keyHeld", [new MethodParameter("键值@keycode", "Float", Alias: "keycode")], "Bool", "键值按住");
        yield return Fun("keyboard.keyUp", [new MethodParameter("键值@keycode", "Float", Alias: "keycode")], "Bool", "键值松开瞬间");
        yield return Fun("keyboard.keyDown2", [new MethodParameter("键值@keycode", "Enum", EnumType: KeyCode, Alias: "keycode")], "Bool", "检测按键按下");
        yield return Fun("keyboard.keyHeld2", [new MethodParameter("键值@keycode", "Enum", EnumType: KeyCode, Alias: "keycode")], "Bool", "检测按键持续按下");
        yield return Fun("keyboard.keyUp2", [new MethodParameter("键值@keycode", "Enum", EnumType: KeyCode, Alias: "keycode")], "Bool", "检测按键释放");
        yield return Fun("keyboard.keyName", [new MethodParameter("键值@keycode", "Float", Alias: "keycode")], "String", "获取按键名称");
        yield return Fun("keyboard.sysEnable", [new MethodParameter("开关@enable", "Bool", Alias: "enable")], null, "开关内置键盘控制");

        // Mouse
        yield return Fun("mouse.pos", [], "Vector3", "获取鼠标位置", notes: "返回像素坐标，不是 0~1 屏幕坐标。传给 UI.isPosInside/UI.posToLocal 前应先用 screen.fromPixel 转换。");
        yield return Fun("mouse.mouseMove", [], "Vector3", "获取鼠标移动量", notes: "返回 Unity 输入轴增量，不是屏幕坐标。");
        yield return Fun("mouse.scroll", [], "Float", "获取滚轮增量");
        yield return Fun("mouse.enable", [new MethodParameter("开关@visible", "Bool", Alias: "visible")], null, "显示/隐藏鼠标");
        yield return Fun("mouse.lock", [new MethodParameter("类型@type", "Enum", EnumType: MouseLockMode, Alias: "type")], null, "设置鼠标锁定模式");

        // Joystick
        yield return Fun("joystick.move",
        [
            new MethodParameter("类型@type", "Enum", EnumType: JoystickInputType, Alias: "type"),
            new MethodParameter("手柄序号@JoysickIndex", "Float", Alias: "JoysickIndex")
        ], "Vector3", "获取手柄输入");
        yield return Fun("joystick.vibrate",
        [
            new MethodParameter("左@Left", "Float", Alias: "Left"),
            new MethodParameter("右@Right", "Float", Alias: "Right"),
            new MethodParameter("手柄序号@JoysickIndex", "Float", Alias: "JoysickIndex")
        ], null, "手柄震动");
        yield return Fun("joystick.count", [], "Float", "获取手柄数量");

    }

    /// <summary>Var 相关全局函数</summary>
    private static IEnumerable<MethodSignature> GetVarFunctions()
    {
        // Bool 逻辑运算
        CompressionHint boolAndHint = new(CompressionKind.NumericSuffix, "bool.and", Array.Empty<string>());
        yield return Fun("bool.and",
        [
            new MethodParameter("A", "Bool"),
            new MethodParameter("B", "Bool")
        ], "Bool", "逻辑与", compressionHint: boolAndHint);
        yield return Fun("bool.and1",
        [
            new MethodParameter("A", "Bool"),
            new MethodParameter("B", "Bool"),
            new MethodParameter("C", "Bool")
        ], "Bool", "逻辑与（3 参数）", compressionHint: boolAndHint);
        yield return Fun("bool.and2",
        [
            new MethodParameter("A", "Bool"),
            new MethodParameter("B", "Bool"),
            new MethodParameter("C", "Bool"),
            new MethodParameter("D", "Bool")
        ], "Bool", "逻辑与（4 参数）", compressionHint: boolAndHint);
        yield return Fun("bool.and3",
        [
            new MethodParameter("A", "Bool"),
            new MethodParameter("B", "Bool"),
            new MethodParameter("C", "Bool"),
            new MethodParameter("D", "Bool"),
            new MethodParameter("E", "Bool")
        ], "Bool", "逻辑与（5 参数）", compressionHint: boolAndHint);
        yield return Fun("bool.and4",
        [
            new MethodParameter("A", "Bool"),
            new MethodParameter("B", "Bool"),
            new MethodParameter("C", "Bool"),
            new MethodParameter("D", "Bool"),
            new MethodParameter("E", "Bool"),
            new MethodParameter("F", "Bool")
        ], "Bool", "逻辑与（6 参数）", compressionHint: boolAndHint);
        CompressionHint boolOrHint = new(CompressionKind.NumericSuffix, "bool.or", Array.Empty<string>());
        yield return Fun("bool.or",
        [
            new MethodParameter("A", "Bool"),
            new MethodParameter("B", "Bool")
        ], "Bool", "逻辑或", compressionHint: boolOrHint);
        yield return Fun("bool.or1",
        [
            new MethodParameter("A", "Bool"),
            new MethodParameter("B", "Bool"),
            new MethodParameter("C", "Bool")
        ], "Bool", "逻辑或（3 参数）", compressionHint: boolOrHint);
        yield return Fun("bool.or2",
        [
            new MethodParameter("A", "Bool"),
            new MethodParameter("B", "Bool"),
            new MethodParameter("C", "Bool"),
            new MethodParameter("D", "Bool")
        ], "Bool", "逻辑或（4 参数）", compressionHint: boolOrHint);
        yield return Fun("bool.or3",
        [
            new MethodParameter("A", "Bool"),
            new MethodParameter("B", "Bool"),
            new MethodParameter("C", "Bool"),
            new MethodParameter("D", "Bool"),
            new MethodParameter("E", "Bool")
        ], "Bool", "逻辑或（5 参数）", compressionHint: boolOrHint);
        yield return Fun("bool.or4",
        [
            new MethodParameter("A", "Bool"),
            new MethodParameter("B", "Bool"),
            new MethodParameter("C", "Bool"),
            new MethodParameter("D", "Bool"),
            new MethodParameter("E", "Bool"),
            new MethodParameter("F", "Bool")
        ], "Bool", "逻辑或（6 参数）", compressionHint: boolOrHint);
        yield return Fun("bool.not", [new MethodParameter(string.Empty, "Bool", Alias: "value")], "Bool", "逻辑非。专用于 if 条件取反：if (G.bool.not(cond)) 等同于 if (!cond)，禁止滥用 bool.equal 或 bool.and 做条件判断");
        yield return Fun("bool.equal",
        [
            new MethodParameter("A", "Bool"),
            new MethodParameter("B", "Bool")
        ], "Bool", "布尔相等");
        yield return Fun("bool.xor",
        [
            new MethodParameter("A", "Bool"),
            new MethodParameter("B", "Bool")
        ], "Bool", "逻辑异或");

        // 变量创建
        yield return Fun("create.float", [new MethodParameter(string.Empty, "Float", Alias: "value")], "Float", "创建浮点变量");
        yield return Fun("create.bool", [new MethodParameter(string.Empty, "Bool", Alias: "value")], "Bool", "创建布尔变量。专用于 if 条件：if (G.create.bool(cond)) 等同于 if (cond)，禁止滥用 bool.equal 或 bool.and 做条件判断");
        yield return Fun("create.color", [new MethodParameter(string.Empty, "Color", Alias: "value")], "Color", "创建颜色变量");
        yield return Fun("create.string", [new MethodParameter(string.Empty, "String", AllowImplicitToString: true, Alias: "value")], "String", "创建字符串变量");
        yield return Fun("creatVariable.Vector3",
        [
            new MethodParameter("x", "Float"),
            new MethodParameter("y", "Float"),
            new MethodParameter("z", "Float")
        ], "Vector3", "创建向量变量");
        yield return Fun("creatVariable.device", [new MethodParameter(string.Empty, "Device", Alias: "value")], "Device", "创建零件变量", notes: "创建引用变量，不是创建新零件");
        yield return Fun("creatVariable.mech", [new MethodParameter(string.Empty, "Mech", Alias: "value")], "Mech", "创建机械变量", notes: "创建引用变量，不是创建新机械");
        yield return Fun("creatVariable.player", [new MethodParameter(string.Empty, "Player", Alias: "value")], "Player", "创建玩家变量", notes: "创建引用变量，不是创建新玩家");
        CompressionHint createListHint = new(CompressionKind.NumericSuffix, "create.list", Array.Empty<string>());
        yield return Fun("create.list", [], "List", "创建空列表", compressionHint: createListHint);
        yield return Fun("create.list1", [new MethodParameter("数据@data", "Any", Alias: "data")], "List", "创建列表（1 元素）", compressionHint: createListHint);
        yield return Fun("create.list2",
        [
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data")
        ], "List", "创建列表（2 元素）", compressionHint: createListHint);
        yield return Fun("create.list3",
        [
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data")
        ], "List", "创建列表（3 元素）", compressionHint: createListHint);
        yield return Fun("create.list4",
        [
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data")
        ], "List", "创建列表（4 元素）", compressionHint: createListHint);
        yield return Fun("create.list5",
        [
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data")
        ], "List", "创建列表（5 元素）", compressionHint: createListHint);
        yield return Fun("create.list6",
        [
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data")
        ], "List", "创建列表（6 元素）", compressionHint: createListHint);
        yield return Fun("create.list7",
        [
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data")
        ], "List", "创建列表（7 元素）", compressionHint: createListHint);
        yield return Fun("create.list8",
        [
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data")
        ], "List", "创建列表（8 元素）", compressionHint: createListHint);
        yield return Fun("create.list9",
        [
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data")
        ], "List", "创建列表（9 元素）", compressionHint: createListHint);
        yield return Fun("create.list10",
        [
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data"),
            new MethodParameter("数据@data", "Any", Alias: "data")
        ], "List", "创建列表（10 元素）", compressionHint: createListHint);
        yield return Fun("create.dict", [], "Dict", "创建空字典");
        yield return Fun("create.struct", [], "Struct", "创建空结构体");
        yield return Fun("create.long", [new MethodParameter(string.Empty, "Long", Alias: "value")], "Long", "创建整数变量");
        yield return Fun("create.icon", [new MethodParameter(string.Empty, "Icon", Alias: "value")], "Icon", "创建图标变量");
        yield return Fun("create.canvas", [new MethodParameter(string.Empty, "Canvas", Alias: "value")], "Canvas", "创建画布变量", notes: "创建引用变量，不是创建新画布");
        yield return Fun("create.bytes", [new MethodParameter(string.Empty, "Bytes", Alias: "value")], "Bytes", "创建二进制流变量");

        // 小数运算
        CompressionHint floatAddHint = new(CompressionKind.NumericSuffix, "float.add", Array.Empty<string>());
        yield return Fun("float.add",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("B", "Float")
        ], "Float", "小数加法", compressionHint: floatAddHint);
        yield return Fun("float.add1",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("B", "Float"),
            new MethodParameter("C", "Float")
        ], "Float", "小数加法（3 参数）", compressionHint: floatAddHint);
        yield return Fun("float.add2",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("B", "Float"),
            new MethodParameter("C", "Float"),
            new MethodParameter("D", "Float")
        ], "Float", "小数加法（4 参数）", compressionHint: floatAddHint);
        yield return Fun("float.add3",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("B", "Float"),
            new MethodParameter("C", "Float"),
            new MethodParameter("D", "Float"),
            new MethodParameter("E", "Float")
        ], "Float", "小数加法（5 参数）", compressionHint: floatAddHint);
        CompressionHint floatMinusHint = new(CompressionKind.NumericSuffix, "float.minus", Array.Empty<string>());
        yield return Fun("float.minus",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("B", "Float")
        ], "Float", "小数减法", compressionHint: floatMinusHint);
        yield return Fun("float.minus1",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("B", "Float"),
            new MethodParameter("C", "Float")
        ], "Float", "小数减法（3 参数）", compressionHint: floatMinusHint);
        yield return Fun("float.minus2",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("B", "Float"),
            new MethodParameter("C", "Float"),
            new MethodParameter("D", "Float")
        ], "Float", "小数减法（4 参数）", compressionHint: floatMinusHint);
        CompressionHint floatMaxHint = new(CompressionKind.NumericSuffix, "float.max", Array.Empty<string>());
        CompressionHint floatMinHint = new(CompressionKind.NumericSuffix, "float.min", Array.Empty<string>());
        foreach (MethodSignature sig in FunNumericSuffixGroup(
                     "float.multiply",
                     [
                         new MethodParameter("A", "Float"),
                         new MethodParameter("B", "Float")
                     ],
                     "Float",
                     "小数乘法",
                     [
                         new MethodParameter("A", "Float"),
                         new MethodParameter("B", "Float"),
                         new MethodParameter("C", "Float")
                     ],
                     [
                         new MethodParameter("A", "Float"),
                         new MethodParameter("B", "Float"),
                         new MethodParameter("C", "Float"),
                         new MethodParameter("D", "Float")
                     ]))
        {
            yield return sig;
        }
        yield return Fun("float.division",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("B", "Float")
        ], "Float", "小数除法", notes: "除数 B 约等于 0 时原版报错；不是返回 Infinity 或极大值。");
        yield return Fun("float.pow",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("次方@pow", "Float", Alias: "pow")
        ], "Float", "幂运算");
        yield return Fun("float.root",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("次方@pow", "Float", Alias: "pow")
        ], "Float", "开方运算");
        yield return Fun("float.Approximately",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("B", "Float")
        ], "Bool", "浮点约等于", notes: "小数没有 float.equal/float.equals； equal/equals/等于/相等 这些语义均对应此方法。");
        yield return Fun("float.notEqual",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("B", "Float")
        ], "Bool", "浮点不等于（约等于的反）", notes: "这是 float.Approximately 的反；不是逐位精确不等。");
        yield return Fun("float.gt",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("B", "Float")
        ], "Bool", "大于");
        yield return Fun("float.lt",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("B", "Float")
        ], "Bool", "小于");
        yield return Fun("float.gte",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("B", "Float")
        ], "Bool", "大于等于");
        yield return Fun("float.lte",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("B", "Float")
        ], "Bool", "小于等于");
        yield return Fun("float.inRange",
        [
            new MethodParameter("输入@input", "Float", Alias: "input"),
            new MethodParameter("最小@min", "Float", Alias: "min"),
            new MethodParameter("最大@max", "Float", Alias: "max")
        ], "Bool", "是否在区间内", notes: "闭区间 [min,max]，包含两端边界。");
        yield return Fun("float.min",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("B", "Float")
        ], "Float", "取最小值", compressionHint: floatMinHint);
        yield return Fun("float.min1",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("B", "Float"),
            new MethodParameter("C", "Float")
        ], "Float", "取最小值（3 参数）", compressionHint: floatMinHint);
        yield return Fun("float.min2",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("B", "Float"),
            new MethodParameter("C", "Float"),
            new MethodParameter("D", "Float")
        ], "Float", "取最小值（4 参数）", compressionHint: floatMinHint);
        yield return Fun("float.max",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("B", "Float")
        ], "Float", "取最大值", compressionHint: floatMaxHint);
        yield return Fun("float.max1",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("B", "Float"),
            new MethodParameter("C", "Float")
        ], "Float", "取最大值（3 参数）", compressionHint: floatMaxHint);
        yield return Fun("float.max2",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("B", "Float"),
            new MethodParameter("C", "Float"),
            new MethodParameter("D", "Float")
        ], "Float", "取最大值（4 参数）", compressionHint: floatMaxHint);
        yield return Fun("float.clamp",
        [
            new MethodParameter("输入@input", "Float", Alias: "input"),
            new MethodParameter("最小@min", "Float", Alias: "min"),
            new MethodParameter("最大@max", "Float", Alias: "max")
        ], "Float", "限制范围");
        yield return Fun("math.rand",
        [
            new MethodParameter("最小值@min", "Float", Alias: "min"),
            new MethodParameter("最大值@max", "Float", Alias: "max")
        ], "Float", "随机数");
        yield return Fun("float.abs", [new MethodParameter(string.Empty, "Float", Alias: "value")], "Float", "绝对值");
        yield return Fun("float.round",
        [
            new MethodParameter("数值@value", "Float", Alias: "value"),
            new MethodParameter("有效位数@digits", "Float", Alias: "digits")
        ], "Float", "四舍五入", notes: "digits 是保留的小数位数；取整用变量方法 floor/ceiling/round，转 Long 用 .toLong()。");
        yield return Fun("float.mod",
        [
            new MethodParameter(string.Empty, "Float", Alias: "a"),
            new MethodParameter(string.Empty, "Float", Alias: "b")
        ], "Float", "取余", notes: "小数余数/取模；除数 b 约等于 0 时原版报错。");
        yield return Fun("float.log",
        [
            new MethodParameter("a", "Float"),
            new MethodParameter("N", "Float")
        ], "Float", "对数");
        yield return Fun("float.lerp",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("B", "Float"),
            new MethodParameter("系数@rate", "Float", Alias: "rate")
        ], "Float", "线性插值", notes: "rate 常用 0~1；原版按 a + (b-a)*rate 计算，不限制 rate，超出会外推。");
        yield return Fun("float.SmoothStep",
        [
            new MethodParameter("A", "Float"),
            new MethodParameter("B", "Float"),
            new MethodParameter("系数@rate", "Float", Alias: "rate")
        ], "Float", "平滑插值", notes: "rate 常用 0~1；原版调用 Unity Mathf.SmoothStep。");
        yield return Fun("float.factorial", [new MethodParameter(string.Empty, "Float", Alias: "value")], "Float", "阶乘（值过大可能溢出/耗时）");
        yield return Fun("float.pi", [], "Float", "圆周率");
        yield return Fun("float.e", [], "Float", "自然常数");
        yield return Fun("float.perlinNoise",
        [
            new MethodParameter("x@x", "Float", Alias: "x"),
            new MethodParameter("y@y", "Float", Alias: "y")
        ], "Float", "柏林噪声");

        // 三角函数
        yield return Fun("float.triangle.sin", [new MethodParameter("角度@angle", "Float", Alias: "angle")], "Float", "正弦", notes: "输入 angle 单位为度，非弧度。");
        yield return Fun("float.triangle.cos", [new MethodParameter("角度@angle", "Float", Alias: "angle")], "Float", "余弦", notes: "输入 angle 单位为度，非弧度。");
        yield return Fun("float.triangle.tan", [new MethodParameter("角度@angle", "Float", Alias: "angle")], "Float", "正切", notes: "输入 angle 单位为度，非弧度。");
        yield return Fun("float.triangle.asin", [new MethodParameter("f", "Float")], "Float", "反正弦", notes: "返回角度单位为度，非弧度。");
        yield return Fun("float.triangle.acos", [new MethodParameter("f", "Float")], "Float", "反余弦", notes: "返回角度单位为度，非弧度。");
        yield return Fun("float.triangle.atan", [new MethodParameter("f", "Float")], "Float", "反正切", notes: "返回角度单位为度，非弧度。");

        // 整数运算
        CompressionHint longAddHint = new(CompressionKind.NumericSuffix, "long.add", Array.Empty<string>());
        yield return Fun("long.add",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("B", "Long")
        ], "Long", "整数加法", compressionHint: longAddHint);
        yield return Fun("long.add1",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("B", "Long"),
            new MethodParameter("C", "Long")
        ], "Long", "整数加法（3 参数）", compressionHint: longAddHint);
        yield return Fun("long.add2",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("B", "Long"),
            new MethodParameter("C", "Long"),
            new MethodParameter("D", "Long")
        ], "Long", "整数加法（4 参数）", compressionHint: longAddHint);
        yield return Fun("long.add3",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("B", "Long"),
            new MethodParameter("C", "Long"),
            new MethodParameter("D", "Long"),
            new MethodParameter("E", "Long")
        ], "Long", "整数加法（5 参数）", compressionHint: longAddHint);
        CompressionHint longMinusHint = new(CompressionKind.NumericSuffix, "long.minus", Array.Empty<string>());
        yield return Fun("long.minus",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("B", "Long")
        ], "Long", "整数减法", compressionHint: longMinusHint);
        yield return Fun("long.minus1",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("B", "Long"),
            new MethodParameter("C", "Long")
        ], "Long", "整数减法（3 参数）", compressionHint: longMinusHint);
        yield return Fun("long.minus2",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("B", "Long"),
            new MethodParameter("C", "Long"),
            new MethodParameter("D", "Long")
        ], "Long", "整数减法（4 参数）", compressionHint: longMinusHint);
        foreach (MethodSignature sig in FunNumericSuffixGroup(
                     "long.multiply",
                     [
                         new MethodParameter("A", "Long"),
                         new MethodParameter("B", "Long")
                     ],
                     "Long",
                     "整数乘法",
                     [
                         new MethodParameter("A", "Long"),
                         new MethodParameter("B", "Long"),
                         new MethodParameter("C", "Long")
                     ],
                     [
                         new MethodParameter("A", "Long"),
                         new MethodParameter("B", "Long"),
                         new MethodParameter("C", "Long"),
                         new MethodParameter("D", "Long")
                     ]))
        {
            yield return sig;
        }
        yield return Fun("long.division",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("B", "Long")
        ], "Float", "整数除法", notes: "不是整除；Long/Long 返回 Float。搜索 整除/整数除法 时注意没有单独 Long 商函数，需要整数商再对结果 .toLong()，需要余数用 long.mod；如果非常需要精准整除则应自行实现移位相减方法；除数 B 为 0 报错。");
        yield return Fun("long.gt",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("B", "Long")
        ], "Bool", "大于");
        yield return Fun("long.lt",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("B", "Long")
        ], "Bool", "小于");
        yield return Fun("long.gte",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("B", "Long")
        ], "Bool", "大于等于");
        yield return Fun("long.lte",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("B", "Long")
        ], "Bool", "小于等于");
        yield return Fun("long.equal",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("B", "Long")
        ], "Bool", "等于");
        yield return Fun("long.notEqual",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("B", "Long")
        ], "Bool", "不等于");
        yield return Fun("long.min",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("B", "Long")
        ], "Long", "取最小值");
        yield return Fun("long.max",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("B", "Long")
        ], "Long", "取最大值");
        yield return Fun("long.clamp",
        [
            new MethodParameter("输入@input", "Long", Alias: "input"),
            new MethodParameter("最小@min", "Long", Alias: "min"),
            new MethodParameter("最大@max", "Long", Alias: "max")
        ], "Long", "限制范围");
        yield return Fun("long.inRange",
        [
            new MethodParameter("输入@input", "Long", Alias: "input"),
            new MethodParameter("最小@min", "Long", Alias: "min"),
            new MethodParameter("最大@max", "Long", Alias: "max")
        ], "Bool", "是否在区间内", notes: "闭区间 [min,max]，包含两端边界。");
        yield return Fun("long.abs", [new MethodParameter(string.Empty, "Long", Alias: "value")], "Long", "绝对值");
        yield return Fun("long.mod",
        [
            new MethodParameter(string.Empty, "Long", Alias: "a"),
            new MethodParameter(string.Empty, "Long", Alias: "b")
        ], "Long", "求余", notes: "整数余数/取模；用于取得整数除法余数，除数 b 为 0 时原版报错。");
        yield return Fun("long.pow",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("次方@pow", "Long", Alias: "pow")
        ], "Long", "乘方（值过大可能溢出/耗时）");
        yield return Fun("long.factorial", [new MethodParameter(string.Empty, "Long", Alias: "value")], "Long", "阶乘（值过大可能溢出/耗时）");
        yield return Fun("long.LeftShift",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("B", "Long")
        ], "Long", "左移");
        yield return Fun("long.RightShift",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("B", "Long")
        ], "Long", "右移");
        yield return Fun("long.BitAnd",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("B", "Long")
        ], "Long", "位与");
        yield return Fun("long.BitOr",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("B", "Long")
        ], "Long", "位或");
        yield return Fun("long.BitXOr",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("B", "Long")
        ], "Long", "位异或");
        yield return Fun("long.BitNot", [new MethodParameter("输入", "Long")], "Long", "位非");
        yield return Fun("long.rand",
        [
            new MethodParameter("最小值@min", "Long", Alias: "min"),
            new MethodParameter("最大值@max", "Long", Alias: "max")
        ], "Long", "随机整数", notes: "原版是按int处理 min/max 的。");
        yield return Fun("long.lerp",
        [
            new MethodParameter("A", "Long"),
            new MethodParameter("B", "Long"),
            new MethodParameter("系数@rate", "Float", Alias: "rate")
        ], "Long", "线性插值（结果取整）", notes: "rate 常用 0~1；原版是按小数算再截取出整数。");

        // 文字操作
        yield return Fun("string.AddColor",
        [
            new MethodParameter("文字@str", "String", AllowImplicitToString: true, Alias: "str"),
            new MethodParameter("颜色@color", "Color", Alias: "color")
        ], "String", "添加颜色格式");
        CompressionHint stringAddHint = new(CompressionKind.NumericSuffix, "string.add", Array.Empty<string>());
        yield return Fun("string.add",
        [
            new MethodParameter("A", "String", AllowImplicitToString: true),
            new MethodParameter("B", "String", AllowImplicitToString: true)
        ], "String", "字符串拼接", compressionHint: stringAddHint);
        yield return Fun("string.equal",
        [
            new MethodParameter("A", "String"),
            new MethodParameter("B", "String")
        ], "Bool", "字符串相等");
        yield return Fun("string.notEqual",
        [
            new MethodParameter("A", "String"),
            new MethodParameter("B", "String")
        ], "Bool", "字符串不相等");
        yield return Fun("string.popNotify",
        [
            new MethodParameter("内容@word", "String", AllowImplicitToString: true, Alias: "word"),
            new MethodParameter("关闭时间@delay", "Float", Alias: "delay")
        ], null, "弹出提示");
        yield return Fun("str2ClipBoard", [new MethodParameter("内容@str", "String", AllowImplicitToString: true, Alias: "str")], null, "复制到剪贴板");
        yield return Fun("clipBoard2Str", [], "String", "从剪贴板读取");
        yield return Fun("string.translate", [new MethodParameter(string.Empty, "String", Alias: "text")], "String", "本地化翻译",
            notes: "按工程语言表 name 查当前语言，key 不带 @；key 为空或长度超过 256 报错，条目不存在报错。");
        yield return Fun("string.dirtyReplace", [new MethodParameter("输入@input", "String", Alias: "input")], "String", "脏词过滤", asyncMode: SdkAsyncMode.Async, notes: "这是异步的");
        yield return Fun("string.md5", [new MethodParameter("内容@content", "String", Alias: "content")], "String", "MD5");
        yield return Fun("string.add1",
        [
            new MethodParameter("A", "String", AllowImplicitToString: true),
            new MethodParameter("B", "String", AllowImplicitToString: true),
            new MethodParameter("C", "String", AllowImplicitToString: true)
        ], "String", "字符串拼接（3 参数）", compressionHint: stringAddHint);
        yield return Fun("string.add2",
        [
            new MethodParameter("A", "String", AllowImplicitToString: true),
            new MethodParameter("B", "String", AllowImplicitToString: true),
            new MethodParameter("C", "String", AllowImplicitToString: true),
            new MethodParameter("D", "String", AllowImplicitToString: true)
        ], "String", "字符串拼接（4 参数）", compressionHint: stringAddHint);
        yield return Fun("string.add3",
        [
            new MethodParameter("A", "String", AllowImplicitToString: true),
            new MethodParameter("B", "String", AllowImplicitToString: true),
            new MethodParameter("C", "String", AllowImplicitToString: true),
            new MethodParameter("D", "String", AllowImplicitToString: true),
            new MethodParameter("E", "String", AllowImplicitToString: true)
        ], "String", "字符串拼接（5 参数）", compressionHint: stringAddHint);
        yield return Fun("string.join",
        [
            new MethodParameter(string.Empty, "List", Alias: "list"),
            new MethodParameter("间隔符@separator", "String", Alias: "separator")
        ], "String", "列表拼接为字符串");
        CompressionHint formatHint = new(CompressionKind.NumericSuffix, "string.Format", Array.Empty<string>());
        yield return Fun("string.Format1",
        [
            new MethodParameter("格式@format", "String", Alias: "format"),
            new MethodParameter("参数01@param01", "Any", Alias: "data1")
        ], "String", "高级拼接", compressionHint: formatHint);
        yield return Fun("string.Format2",
        [
            new MethodParameter("格式@format", "String", Alias: "format"),
            new MethodParameter("参数01@param01", "Any", Alias: "data1"),
            new MethodParameter("参数11@param11", "Any", Alias: "data2")
        ], "String", "高级拼接", compressionHint: formatHint);
        yield return Fun("string.Format3",
        [
            new MethodParameter("格式@format", "String", Alias: "format"),
            new MethodParameter("参数01@param01", "Any", Alias: "data1"),
            new MethodParameter("参数11@param11", "Any", Alias: "data2"),
            new MethodParameter("参数21@param21", "Any", Alias: "data3")
        ], "String", "高级拼接", compressionHint: formatHint);
        yield return Fun("string.Format4",
        [
            new MethodParameter("格式@format", "String", Alias: "format"),
            new MethodParameter("参数01@param01", "Any", Alias: "data1"),
            new MethodParameter("参数11@param11", "Any", Alias: "data2"),
            new MethodParameter("参数21@param21", "Any", Alias: "data3"),
            new MethodParameter("参数31@param31", "Any", Alias: "data4")
        ], "String", "高级拼接", compressionHint: formatHint);
        yield return Fun("string.Format5",
        [
            new MethodParameter("格式@format", "String", Alias: "format"),
            new MethodParameter("参数01@param01", "Any", Alias: "data1"),
            new MethodParameter("参数11@param11", "Any", Alias: "data2"),
            new MethodParameter("参数21@param21", "Any", Alias: "data3"),
            new MethodParameter("参数31@param31", "Any", Alias: "data4"),
            new MethodParameter("参数41@param41", "Any", Alias: "data5")
        ], "String", "高级拼接", compressionHint: formatHint);
        CompressionHint translateExHint = new(CompressionKind.NumericSuffix, "TranslateEx", Array.Empty<string>());
        const string translateExNote = "按工程语言表 name 查当前语言，content 不带 @；返回文本中的 %1..%5 会被参数替换；String 参数以 @ 开头时先按静态翻译 key 解析。content 为空或长度超过 256 报错，条目不存在报错。";
        yield return Fun("TranslateEx1",
        [
            new MethodParameter("内容@content", "String", Alias: "content"),
            new MethodParameter("参数01@param01", "Any", Alias: "data1")
        ], "String", "高级翻译", compressionHint: translateExHint, notes: translateExNote);
        yield return Fun("TranslateEx2",
        [
            new MethodParameter("内容@content", "String", Alias: "content"),
            new MethodParameter("参数01@param01", "Any", Alias: "data1"),
            new MethodParameter("参数11@param11", "Any", Alias: "data2")
        ], "String", "高级翻译", compressionHint: translateExHint, notes: translateExNote);
        yield return Fun("TranslateEx3",
        [
            new MethodParameter("内容@content", "String", Alias: "content"),
            new MethodParameter("参数01@param01", "Any", Alias: "data1"),
            new MethodParameter("参数11@param11", "Any", Alias: "data2"),
            new MethodParameter("参数21@param21", "Any", Alias: "data3")
        ], "String", "高级翻译", compressionHint: translateExHint, notes: translateExNote);
        yield return Fun("TranslateEx4",
        [
            new MethodParameter("内容@content", "String", Alias: "content"),
            new MethodParameter("参数01@param01", "Any", Alias: "data1"),
            new MethodParameter("参数11@param11", "Any", Alias: "data2"),
            new MethodParameter("参数21@param21", "Any", Alias: "data3"),
            new MethodParameter("参数31@param31", "Any", Alias: "data4")
        ], "String", "高级翻译", compressionHint: translateExHint, notes: translateExNote);
        yield return Fun("TranslateEx5",
        [
            new MethodParameter("内容@content", "String", Alias: "content"),
            new MethodParameter("参数01@param01", "Any", Alias: "data1"),
            new MethodParameter("参数11@param11", "Any", Alias: "data2"),
            new MethodParameter("参数21@param21", "Any", Alias: "data3"),
            new MethodParameter("参数31@param31", "Any", Alias: "data4"),
            new MethodParameter("参数41@param41", "Any", Alias: "data5")
        ], "String", "高级翻译", compressionHint: translateExHint, notes: translateExNote);

        // 向量运算
        CompressionHint vec3AddHint = new(CompressionKind.NumericSuffix, "vector3.add", Array.Empty<string>());
        CompressionHint vec3MinusHint = new(CompressionKind.NumericSuffix, "vector3.minus", Array.Empty<string>());
        CompressionHint vec3MultiplyHint = new(CompressionKind.NumericSuffix, "vector3.multilply", Array.Empty<string>());
        CompressionHint vec3DivisionHint = new(CompressionKind.NumericSuffix, "vector3.division", Array.Empty<string>());
        const string vector3MultiplyNote = "原版函数名拼作 multilply；搜索 multiply/乘法/缩放 时使用这个。";
        yield return Fun("vector3.add",
        [
            new MethodParameter("A", "Vector3"),
            new MethodParameter("B", "Vector3")
        ], "Vector3", "向量加法", compressionHint: vec3AddHint);
        yield return Fun("vector3.minus",
        [
            new MethodParameter("A", "Vector3"),
            new MethodParameter("B", "Vector3")
        ], "Vector3", "向量减法", compressionHint: vec3MinusHint);
        yield return Fun("vector3.multilply",
        [
            new MethodParameter("V", "Vector3", Alias: "v"),
            new MethodParameter("A", "Float", Alias: "data1")
        ], "Vector3", "向量缩放", compressionHint: vec3MultiplyHint, notes: vector3MultiplyNote);
        yield return Fun("vector3.division",
        [
            new MethodParameter("V", "Vector3", Alias: "v"),
            new MethodParameter("A", "Float", Alias: "data1")
        ], "Vector3", "向量除法", compressionHint: vec3DivisionHint, notes: "向量除以 Float；原版这里不走 float.division 的除零报错。");
        yield return Fun("vector3.dividVec",
        [
            new MethodParameter("A", "Vector3"),
            new MethodParameter("B", "Vector3")
        ], "Vector3", "向量对应分量相除");
        yield return Fun("vector3.dot",
        [
            new MethodParameter("A", "Vector3"),
            new MethodParameter("B", "Vector3")
        ], "Float", "点积");
        yield return Fun("vector3.cross",
        [
            new MethodParameter("A", "Vector3"),
            new MethodParameter("B", "Vector3")
        ], "Vector3", "叉积");
        yield return Fun("vector3.distance",
        [
            new MethodParameter("A", "Vector3"),
            new MethodParameter("B", "Vector3")
        ], "Float", "两点距离", notes: "两个坐标之间的距离；单个向量长度/模长用变量方法 v.length()。");
        yield return Fun("vector3.angle",
        [
            new MethodParameter("A", "Vector3"),
            new MethodParameter("B", "Vector3")
        ], "Float", "夹角", notes: "返回角度单位为度，结果始终为正数。");
        yield return Fun("vector3.signedAngle",
        [
            new MethodParameter("A", "Vector3"),
            new MethodParameter("B", "Vector3"),
            new MethodParameter("转轴@axis", "Vector3", Alias: "axis")
        ], "Float", "有符号夹角", notes: "返回角度单位为度；从 A 转到 B，正负方向由 axis 决定。");
        yield return Fun("vector3.lerp",
        [
            new MethodParameter("A", "Vector3"),
            new MethodParameter("B", "Vector3"),
            new MethodParameter("系数@rate", "Float", Alias: "rate")
        ], "Vector3", "线性插值", notes: "rate 常用 0~1；原版按 a + (b-a)*rate 计算，不限制 rate，超出会外推。");
        yield return Fun("vector3.slerp",
        [
            new MethodParameter("A", "Vector3"),
            new MethodParameter("B", "Vector3"),
            new MethodParameter("系数@rate", "Float", Alias: "rate")
        ], "Vector3", "球面插值", notes: "方向插值；原版调用 Unity Vector3.Slerp。");
        yield return Fun("vector3.rotateAround",
        [
            new MethodParameter("当前方向@current", "Vector3", Alias: "current"),
            new MethodParameter("旋转轴@axis", "Vector3", Alias: "axis"),
            new MethodParameter("角度@radians", "Float", Alias: "radians")
        ], "Vector3", "绕轴旋转", notes: "角度单位为度，非弧度；参数名 radians 是原版命名。");
        yield return Fun("vector3.reflect",
        [
            new MethodParameter("入射方向@inDirection", "Vector3", Alias: "inDirection"),
            new MethodParameter("平面垂直方向@inNormal", "Vector3", Alias: "inNormal")
        ], "Vector3", "反射");
        yield return Fun("vector3.bezier",
        [
            new MethodParameter("起点@p0", "Vector3", Alias: "p0"),
            new MethodParameter("控制点@p1", "Vector3", Alias: "p1"),
            new MethodParameter("终点@p2", "Vector3", Alias: "p2"),
            new MethodParameter("系数@t", "Float", Alias: "t")
        ], "Vector3", "贝塞尔曲线");
        yield return Fun("vector3.add1",
        [
            new MethodParameter("A", "Vector3"),
            new MethodParameter("B", "Vector3"),
            new MethodParameter("C", "Vector3")
        ], "Vector3", "向量加法", compressionHint: vec3AddHint);
        yield return Fun("vector3.add2",
        [
            new MethodParameter("A", "Vector3"),
            new MethodParameter("B", "Vector3"),
            new MethodParameter("C", "Vector3"),
            new MethodParameter("D", "Vector3")
        ], "Vector3", "向量加法", compressionHint: vec3AddHint);
        yield return Fun("vector3.add3",
        [
            new MethodParameter("A", "Vector3"),
            new MethodParameter("B", "Vector3"),
            new MethodParameter("C", "Vector3"),
            new MethodParameter("D", "Vector3"),
            new MethodParameter("E", "Vector3")
        ], "Vector3", "向量加法", compressionHint: vec3AddHint);
        yield return Fun("vector3.minus1",
        [
            new MethodParameter("A", "Vector3"),
            new MethodParameter("B", "Vector3"),
            new MethodParameter("C", "Vector3")
        ], "Vector3", "向量减法", compressionHint: vec3MinusHint);
        yield return Fun("vector3.multilply1",
        [
            new MethodParameter("V", "Vector3", Alias: "v"),
            new MethodParameter("A", "Float", Alias: "data1"),
            new MethodParameter("B", "Float", Alias: "data2")
        ], "Vector3", "向量缩放", compressionHint: vec3MultiplyHint, notes: vector3MultiplyNote);
        yield return Fun("vector3.multilply2",
        [
            new MethodParameter("V", "Vector3", Alias: "v"),
            new MethodParameter("A", "Float", Alias: "data1"),
            new MethodParameter("B", "Float", Alias: "data2"),
            new MethodParameter("C", "Float", Alias: "data3")
        ], "Vector3", "向量缩放", compressionHint: vec3MultiplyHint, notes: vector3MultiplyNote);
        yield return Fun("vector3.division1",
        [
            new MethodParameter("V", "Vector3", Alias: "v"),
            new MethodParameter("A", "Float", Alias: "data1"),
            new MethodParameter("B", "Float", Alias: "data2")
        ], "Vector3", "向量除法", compressionHint: vec3DivisionHint, notes: "向量连续除以 Float；原版这里不走 float.division 的除零报错。");
        yield return Fun("vector3.equal",
        [
            new MethodParameter("A", "Vector3"),
            new MethodParameter("B", "Vector3")
        ], "Bool", "向量约等于", notes: "Unity Vector3 == 的约等于；搜索 equal/equals/等于/相等/比较 时使用这个。");
        yield return Fun("vector3.notEqual",
        [
            new MethodParameter("A", "Vector3"),
            new MethodParameter("B", "Vector3")
        ], "Bool", "向量不等于", notes: "Unity Vector3 !=；是 vector3.equal 的反。");
        yield return Fun("vector3.rotateAroundPos",
        [
            new MethodParameter("当前坐标@current", "Vector3", Alias: "current"),
            new MethodParameter("转轴坐标@Pivot", "Vector3", Alias: "Pivot"),
            new MethodParameter("转轴方向@axis", "Vector3", Alias: "axis"),
            new MethodParameter("角度@radians", "Float", Alias: "radians")
        ], "Vector3", "绕点旋转", notes: "角度单位为度，非弧度；参数名 radians 是原版命名。");
        yield return Fun("vector3.rotateTo",
        [
            new MethodParameter("当前方向@current", "Vector3", Alias: "current"),
            new MethodParameter("目标方向@target", "Vector3", Alias: "target"),
            new MethodParameter("最大角度@maxRadiansDelta", "Float", Alias: "maxRadiansDelta")
        ], "Vector3", "向目标方向旋转", notes: "最大角度单位为度，非弧度；参数名 maxRadiansDelta 是原版命名。");
    }

    /// <summary>System/Debug 相关全局函数</summary>
    private static IEnumerable<MethodSignature> GetSystemDebugFunctions()
    {
        // 特效
        yield return Fun("audio.explode",
        [
            new MethodParameter("坐标@position", "Vector3", Alias: "position"),
            new MethodParameter("缩放@scale", "Float", Alias: "scale"),
            new MethodParameter("音量大小@volume", "Float", Alias: "volume")
        ], null, "播放爆炸特效");

        // 系统
        yield return Fun("AppID", [], "String", "得到 AppID");

        // 环境
        yield return Fun("gravity", [new MethodParameter("G", "Vector3")], null, "设置重力");
        yield return Fun("light", [new MethodParameter("亮度@intensity", "Float", Alias: "intensity")], null, "设置亮度");
        yield return Fun("setDirLight",
        [
            new MethodParameter("方向@intensity", "Vector3", Alias: "direction"),
            new MethodParameter("颜色@color", "Color", Alias: "color"),
            new MethodParameter("亮度@intensity", "Float", Alias: "intensity")
        ], null, "设置阳光");
        yield return Fun("setRefectionIntensity", [new MethodParameter("亮度@intensity", "Float", Alias: "intensity")], null, "设置反光亮度");
        yield return Fun("setShadow", [new MethodParameter("最大距离@Distance", "Float", Alias: "Distance")], null, "设置阴影");
        yield return Fun("skyReflect", [new MethodParameter("反光类型@reflectType", "Enum", EnumType: SkyReflectType, Alias: "reflectType")], null, "设置反光类型");
        yield return Fun("getGravity", [], "Vector3", "获取重力");
        yield return Fun("skyTint", [new MethodParameter("亮度@intensity", "Float", Alias: "intensity")], null, "设置天空盒亮度");
        yield return Fun("waterHeight", [new MethodParameter("高度@height", "Float", Alias: "height")], null, "设置水面高度");
        yield return Fun("getWaterParams", [], "List", "获取水面属性列表");
        yield return Fun("setWaterColor",
        [
            new MethodParameter("属性@property", "String", Alias: "property"),
            new MethodParameter("颜色@Color", "Color", Alias: "Color")
        ], null, "设置水面颜色参数",
            notes: "需地图开启水面；property 应来自 getWaterParams 返回的属性名。");
        yield return Fun("setWaterFloat",
        [
            new MethodParameter("属性@property", "String", Alias: "property"),
            new MethodParameter("数值@Value", "Float", Alias: "Value")
        ], null, "设置水面小数参数",
            notes: "需地图开启水面；property 应来自 getWaterParams 返回的属性名。");
        yield return Fun("xRayShader",
        [
            new MethodParameter("开关@onOff", "Bool", Alias: "onOff"),
            new MethodParameter("层@layer", "Float", Alias: "layer")
        ], null, "开关透视X光特效");
        yield return Fun("sky", [new MethodParameter("序号@index", "Float", Alias: "index")], null, "设置天空盒");
        yield return Fun("solidColorSky", [new MethodParameter("颜色@color", "Color", Alias: "color")], null, "设置纯色天空");
        yield return Fun("customSky", [new MethodParameter("文件名列表@Filenames", "List", Alias: "Filenames")], null, "设置自定义天空盒",
            notes: "文件名列表必须 6 项，顺序为前、后、左、右、上、下；使用工程内图片资源。");
        yield return Fun("setMaxBullet", [new MethodParameter("数量@num", "Long", Alias: "num")], null, "设置最大子弹数");
        yield return Fun("setFixedFrame", [new MethodParameter("帧数@fixedFrame", "Float", Alias: "fixedFrame")], null, "设置物理帧率");
        yield return Fun("setTimeScale", [new MethodParameter("速度@speed", "Float", Alias: "speed")], null, "设置时间缩放");
        yield return Fun("setSlowTime",
        [
            new MethodParameter("流速@speed", "Float", Alias: "speed"),
            new MethodParameter("持续时间@Elapse", "Float", Alias: "Elapse")
        ], null, "设置缓慢时间");
        yield return Fun("setDefaultPhysicsMat",
        [
            new MethodParameter("弹性@bounciness", "Float", Alias: "bounciness"),
            new MethodParameter("动摩擦@dynamicFriction", "Float", Alias: "dynamicFriction"),
            new MethodParameter("静摩擦@staticFriction", "Float", Alias: "staticFriction")
        ], null, "设置默认物理材质");
        yield return Fun("setPlantsCulling", [new MethodParameter("系数@rate", "Float", Alias: "rate")], null, "设置植物剔除距离");
        yield return Fun("loadTerrain", [new MethodParameter(string.Empty, "String", Alias: "name")], null, "加载地形",
            notes: "按工程内已有地形图片资源加载；只在地图地面类型匹配时生效，不导入新资源。");
        yield return Fun("loadVoxel", [new MethodParameter(string.Empty, "String", Alias: "name")], null, "加载体素",
            notes: "按工程内已有体素数据资源加载；只在地图地面类型匹配时生效，不导入新资源。");
        yield return Fun("loadPlants", [new MethodParameter(string.Empty, "String", Alias: "name")], null, "加载植物",
            notes: "按工程内已有植物数据资源加载；只在地形地图上生效，不导入新资源。");
        yield return Fun("fog",
        [
            new MethodParameter("是否开启@enable", "Bool", Alias: "enable"),
            new MethodParameter("开始距离@startDistance", "Float", Alias: "startDistance"),
            new MethodParameter("结束距离@endDistance", "Float", Alias: "endDistance"),
            new MethodParameter("颜色@color", "Color", Alias: "color")
        ], null, "设置雾");
        yield return Fun("HDRBloom",
        [
            new MethodParameter("开关HDR@OnOff", "Bool", Alias: "OnOff"),
            new MethodParameter("光晕阈值@Threshold", "Float", Alias: "Threshold"),
            new MethodParameter("光晕亮度@Threshold", "Float", Alias: "Intensity")
        ], null, "HDR 光晕");
        yield return Fun("colorAdjustments",
        [
            new MethodParameter("开关@OnOff", "Bool", Alias: "OnOff"),
            new MethodParameter("曝光@Exposure", "Float", Alias: "Exposure"),
            new MethodParameter("对比度@Contrast", "Float", Alias: "Contrast"),
            new MethodParameter("饱和度@Saturation", "Float", Alias: "Saturation")
        ], null, "色彩校正");

        // 文件系统
        yield return Fun("dir.listFiles", [new MethodParameter("路径@dir", "String", Alias: "dir")], "List", "列出目录文件",
            notes: "游玩中路径按数据目录解释；编辑器中按传入路径解释。");
        yield return Fun("dir.listDirs", [new MethodParameter("路径@dir", "String", Alias: "dir")], "List", "列出子目录",
            notes: "游玩中路径按数据目录解释；编辑器中按传入路径解释。");
        yield return Fun("dir.currentProject", [], "String", "获取工程目录", "通常仅在编辑器环境有意义。",
            notes: "返回工程目录字符串，主要用于编辑器内文件操作。");
        yield return Fun("file.readText",
        [
            new MethodParameter("文件名@filename", "String", Alias: "filename"),
            new MethodParameter("数据目录@dataPath", "Bool", Alias: "dataPath")
        ], "String", "读取文本文件",
            notes: "dataPath=true 时按运行时数据目录下的相对文件名读取；false 时按传入路径读取。");
        yield return Fun("file.writeText",
        [
            new MethodParameter("文件名@filename", "String", Alias: "filename"),
            new MethodParameter("内容@content", "String", Alias: "content"),
            new MethodParameter("是否追加@append", "Bool", Alias: "append"),
            new MethodParameter("数据目录@dataPath", "Bool", Alias: "dataPath")
        ], null, "写入文本文件",
            notes: "dataPath=true 时写到运行时数据目录；append=true 覆盖写入，append=false 追加写入。");
        yield return Fun("file.readBin",
        [
            new MethodParameter("文件名@filename", "String", Alias: "filename"),
            new MethodParameter("数据目录@dataPath", "Bool", Alias: "dataPath")
        ], "Bytes", "读取二进制文件",
            notes: "dataPath=true 时按运行时数据目录下的相对文件名读取；文件不存在时返回空 Bytes。");
        yield return Fun("file.writeBin",
        [
            new MethodParameter("文件名@filename", "String", Alias: "filename"),
            new MethodParameter("内容@content", "Bytes", Alias: "content")
        ], null, "写入二进制文件",
            notes: "游玩中写到运行时数据目录；编辑器中按传入路径写入。");
        yield return Fun("file.delete", [], null, "删除文件",
            notes: "不用于工程资源管理；删除工程资源应走工具链侧能力。");
        yield return Fun("file.copy",
        [
            new MethodParameter("源文件名@filename", "String", Alias: "filename"),
            new MethodParameter("目标文件@newName", "String", Alias: "newName")
        ], null, "复制文件",
            notes: "游玩中源和目标都按运行时数据目录解释；编辑器中按传入路径解释。");
        yield return Fun("file.move",
        [
            new MethodParameter("源文件名@filename", "String", Alias: "filename"),
            new MethodParameter("目标文件@newName", "String", Alias: "newName")
        ], null, "移动文件",
            notes: "游玩中源和目标都按运行时数据目录解释；编辑器中按传入路径解释。");
        yield return Fun("dir.new", [new MethodParameter("路径@dir", "String", Alias: "dir")], null, "创建目录",
            notes: "游玩中按运行时数据目录创建；编辑器中按传入路径创建。");
        yield return Fun("dir.del",
        [
            new MethodParameter("路径@dir", "String", Alias: "dir"),
            new MethodParameter("删除子目录@recursive", "Bool", Alias: "recursive")
        ], null, "删除目录",
            notes: "游玩中按运行时数据目录删除；编辑器中按传入路径删除。");
        yield return Fun("dir.exist", [new MethodParameter("路径@path", "String", Alias: "path")], "Bool", "目录是否存在",
            notes: "游玩中路径按运行时数据目录解释；编辑器中按传入路径解释。");
        yield return Fun("file.exist", [new MethodParameter("路径@path", "String", Alias: "path")], "Bool", "文件是否存在",
            notes: "游玩中路径按运行时数据目录解释；编辑器中按传入路径解释。");
        yield return Fun("file.info", [new MethodParameter("路径@path", "String", Alias: "path")], "Struct", "获取文件信息",
            notes: "返回 Struct 字段：Length、CreationTime、LastWriteTime。游玩中路径按运行时数据目录解释。");
        yield return Fun("file.selectFile",
        [
            new MethodParameter("路径@path", "String", Alias: "path"),
            new MethodParameter("提示信息@Title", "String", Alias: "Title"),
            new MethodParameter("扩展名@exts", "String", Alias: "exts")
        ], "String", "选择文件对话框", asyncMode: SdkAsyncMode.Async,
            notes: "主要用于选择图片文件；返回选中的文件名或路径字符串，具体形态随平台文件选择器而定。");

        // 层与可见性
        yield return Fun("layer.enableCollide",
        [
            new MethodParameter("层1@Layer1", "Float", Alias: "Layer1"),
            new MethodParameter("层2@Layer2", "Float", Alias: "Layer2"),
            new MethodParameter("开关碰撞@OnOff", "Bool", Alias: "OnOff")
        ], null, "开关层碰撞",
            notes: "Layer1/Layer2 使用平台层编号，有效范围 0-23。");
        yield return Fun("layer.getCollide",
        [
            new MethodParameter("层1@Layer1", "Float", Alias: "Layer1"),
            new MethodParameter("层2@Layer2", "Float", Alias: "Layer2")
        ], "Bool", "获取层碰撞状态",
            notes: "Layer1/Layer2 使用平台层编号，有效范围 0-23。");
        yield return Fun("layer.enableOverlap", [new MethodParameter("层列表@layers", "List", Alias: "layers")], null, "设置检测层列表",
            notes: "列表元素必须为 Float（平台层编号，有效范围 0-23）。");
        yield return Fun("layer.getOverlap", [], "List", "获取检测层列表");
        yield return Fun("layer.visible", [new MethodParameter("层列表@layers", "List", Alias: "layers")], null, "设置可见层",
            notes: "列表元素必须为 Float（平台层编号，有效范围 0-23）。");
        yield return Fun("layer.setLightMask", [new MethodParameter("层列表@layers", "List", Alias: "layers")], null, "设置光照层",
            notes: "列表元素必须为 Float（平台层编号，有效范围 0-23）。");

        // 摄像机逻辑控制
        yield return Fun("camera.setpos",
        [
            new MethodParameter("目标点@targetPos", "Vector3", Alias: "targetPos"),
            new MethodParameter("角度@angle", "Vector3", Alias: "angle"),
            new MethodParameter("距离@distance", "Float", Alias: "distance"),
            new MethodParameter("遮挡前移速率@distanceLerpSpeed", "Float", Alias: "distanceLerpSpeed")
        ], null, "设置摄像机目标和角度");
        yield return Fun("camera.setposdir",
        [
            new MethodParameter("目标点@targetPos", "Vector3", Alias: "targetPos"),
            new MethodParameter("朝向@forward", "Vector3", Alias: "forward"),
            new MethodParameter("遮挡前移速率@distanceLerpSpeed", "Float", Alias: "distanceLerpSpeed")
        ], null, "设置摄像机目标和朝向");
        yield return Fun("camera.setpos2",
        [
            new MethodParameter("位置@pos", "Vector3", Alias: "pos"),
            new MethodParameter("向前方向@forward", "Vector3", Alias: "forward"),
            new MethodParameter("向上方向@up", "Vector3", Alias: "up"),
            new MethodParameter("摄像机序号@index", "Float", Alias: "index")
        ], null, "设置摄像机位置和朝向");
        yield return Fun("camera.localToWorldDir",
        [
            new MethodParameter("局部方向@Local Dir", "Vector3", Alias: "Local_Dir"),
            new MethodParameter("摄像机序号@index", "Float", Alias: "index")
        ], "Vector3", "摄像机局部转世界方向");
        yield return Fun("camera.localToWorldPos",
        [
            new MethodParameter("局部坐标@Local Pos", "Vector3", Alias: "Local_Pos"),
            new MethodParameter("摄像机序号@index", "Float", Alias: "index")
        ], "Vector3", "摄像机局部转世界坐标");
        yield return Fun("camera.setSubCameras",
        [
            new MethodParameter("序号@Index", "Float", Alias: "Index"),
            new MethodParameter("x偏移@x", "Float", Alias: "x"),
            new MethodParameter("y偏移@y", "Float", Alias: "y"),
            new MethodParameter("宽@width", "Float", Alias: "width"),
            new MethodParameter("高@height", "Float", Alias: "height")
        ], null, "设置子摄像机");
        yield return Fun("mechFollowCamera",
        [
            new MethodParameter(string.Empty, "Mech", Alias: "mech")
        ], null, "机械跟随摄像机");

        // 屏幕与射线
        yield return Fun("screen.getResolutions", [], "List", "获取屏幕分辨率列表", notes: "返回字符串列表，元素格式为 宽X高。");
        yield return Fun("screen.systemShakeRate", [new MethodParameter("力度@shakeRate", "Float", Alias: "shakeRate")], null, "设置系统震动频率", notes: "和主动 screen.shake 效果强度无关。");

        // 音频
        yield return Fun("audio.playEffect",
        [
            new MethodParameter("音效@soundEffect", "Enum", EnumType: SoundEffect, Alias: "soundEffect"),
            new MethodParameter("音量@volume", "Float", Alias: "volume")
        ], null, "播放音效");
        yield return Fun("audio.setRolloffMaxDis", [new MethodParameter(string.Empty, "Float", Alias: "distance")], null, "设置音效衰减距离");
        yield return Fun("audio.set3dAudioSource",
        [
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("开关@OnOff", "Bool", Alias: "OnOff")
        ], null, "设置 3D 音效");
        yield return Fun("audio.playMusical",
        [
            new MethodParameter("音符@midiIndex", "Float", Alias: "midiIndex"),
            new MethodParameter("乐器@patch", "Float", Alias: "patch"),
            new MethodParameter("时长@duration", "Float", Alias: "duration"),
            new MethodParameter("力度@velocity", "Float", Alias: "velocity")
        ], null, "播放 MIDI 音符");
        yield return Fun("audio.getMidiPatches", [], "List", "获取 MIDI 乐器列表", asyncMode: SdkAsyncMode.Async);
        yield return Fun("audio.playLocalMidiFile",
        [
            new MethodParameter("文件名@fileName", "String", Alias: "fileName"),
            new MethodParameter("音量@volume", "Float", Alias: "volume"),
            new MethodParameter("循环@Loop", "Bool", Alias: "Loop")
        ], null, "播放 MIDI 文件",
            notes: "需工程启用 MIDI；fileName 传基础名，运行时会查找对应 .mid 文件。");
        yield return Fun("audio.stopMidiMusic", [], null, "停止 MIDI 播放");
        yield return Fun("audio.playAudioFile",
        [
            new MethodParameter("音频文件名@AudioName", "String", Alias: "AudioName"),
            new MethodParameter("坐标@position", "Vector3", Alias: "position"),
            new MethodParameter("音量大小@volume", "Float", Alias: "volume"),
            new MethodParameter("3D模式@3D", "Bool", Alias: "3D"),
            new MethodParameter("循环@loop", "Bool", Alias: "loop")
        ], "Struct", "播放音频文件",
            notes: "按工程内音频文件名播放，支持 wav/ogg；返回 Struct 字段：id、time。");
        yield return Fun("audio.setVolume", [new MethodParameter("音量大小@volume", "Float", Alias: "volume")], null, "设置全局音量");
        yield return Fun("audio.stopAudioFile", [new MethodParameter("音频ID@ID", "Long", Alias: "ID")], null, "停止音频播放");

        // 网络
        yield return Fun("net.post",
        [
            new MethodParameter("网址", "String"),
            new MethodParameter("请求头", "Dict"),
            new MethodParameter("请求体", "Dict")
        ], "Dict", "网络 Post 请求", asyncMode: SdkAsyncMode.Async,
            notes: "请求体 Dict 会作为表单字段发送；返回内容按 JSON 解析为 Dict。");
        yield return Fun("net.postSign",
        [
            new MethodParameter("网址", "String"),
            new MethodParameter("请求头", "Dict"),
            new MethodParameter("请求体", "Dict"),
            new MethodParameter("秘钥", "String")
        ], "Dict", "网络签名 Post 请求", asyncMode: SdkAsyncMode.Async,
            notes: "在请求体字段基础上计算签名并随请求发送；返回内容按 JSON 解析为 Dict。");
        yield return Fun("net.get",
        [
            new MethodParameter("网址", "String"),
            new MethodParameter("请求头", "Dict")
        ], "Dict", "网络 Get 请求", asyncMode: SdkAsyncMode.Async,
            notes: "返回内容按 JSON 解析为 Dict，同时在 bytes 字段保留原始响应二进制。");

        // 调试
        yield return Fun("debug.print",
        [
            new MethodParameter("内容@str", "String", AllowImplicitToString: true, Alias: "str"),
            new MethodParameter("颜色@color", "Color", Alias: "color"),
            new MethodParameter("调用堆栈@boolAppend", "Bool", Alias: "boolAppend")
        ], null, "输出调试日志");
        yield return Fun("debug.enable", [new MethodParameter("是否开启@boolDebug", "Bool", Alias: "boolDebug")], null, "开关调试模式");
        yield return Fun("debug.enableNetLog", [new MethodParameter("是否开启@enable", "Bool", Alias: "enable")], null, "开关网络日志");
        yield return Fun("debug.enableUIMemLog", [new MethodParameter("是否开启@enable", "Bool", Alias: "enable")], null, "开关 UI 内存日志");
        yield return Fun("debug.reportLevel",
        [
            new MethodParameter("关卡@level", "String", Alias: "level"),
            new MethodParameter("状态@status", "String", Alias: "status"),
            new MethodParameter("剩余进度@left", "Long", Alias: "left"),
            new MethodParameter("关卡进度@levelID", "Long", Alias: "levelID")
        ], null, "上报进出关卡",
            notes: "status：0=进入关卡，1=成功，2=失败，3=主动退出。");
        yield return Fun("debug.methodProfile", [], null, "开始方法耗时统计",
            notes: "开始新的方法耗时统计，并清空上一次统计结果。");
        yield return Fun("debug.finishMethodProfile", [], "List", "结束方法耗时统计",
            notes: "结束统计并返回列表；每项包含方法、次数、耗时。");
        yield return Fun("debug.setColor", [new MethodParameter("颜色@color", "Color", Alias: "color")], null, "设置调试颜色");
        yield return Fun("debug.clear", [], null, "清空调试列表");
        yield return Fun("debug.pause", [new MethodParameter("是否暂停@boolPause", "Bool", Alias: "boolPause")], null, "暂停调试输出");
        yield return Fun("debug.maxRunTime", [new MethodParameter("次数@maxRuntime", "Float", Alias: "maxRuntime")], null, "设置最大循环次数");
        yield return Fun("debug.errorStop", [new MethodParameter("是否停止@boolBreak", "Bool", Alias: "boolBreak")], null, "设置错误时是否停止");
        yield return Fun("debug.focusUILog", [new MethodParameter(string.Empty, "UI", Alias: "target")], null, "跟踪 UI 日志");
        yield return Fun("debug.enableMethodCallLog", [new MethodParameter("方法名@MethodName", "String", Alias: "MethodName", IsScriptMethodName: true)], null, "方法调用日志",
            notes: "用于定位指定方法的调用位置。");
        yield return Fun("debug.enablePlayerVarLog",
        [
            new MethodParameter(string.Empty, "Player", Alias: "player"),
            new MethodParameter("名称@key", "String", Alias: "key")
        ], null, "跟踪玩家数据读写日志",
            notes: "key 为空时跟踪该玩家全部自定义数据读写。");
        yield return Fun("debug.enableMechVarLog",
        [
            new MethodParameter(string.Empty, "Mech", Alias: "mech"),
            new MethodParameter("名称@key", "String", Alias: "key")
        ], null, "跟踪机械数据读写日志",
            notes: "key 为空时跟踪该机械全部自定义数据读写。");
        yield return Fun("debug.enableStructLog",
        [
            new MethodParameter(string.Empty, "Struct", Alias: "target"),
            new MethodParameter("名称@key", "String", Alias: "key")
        ], null, "跟踪结构读写日志",
            notes: "key 为空时跟踪该结构全部成员读写。");
        yield return Fun("debug.enableAStarDebug", [new MethodParameter("是否开启@enable", "Bool", Alias: "enable")], null, "开关寻路调试");
        yield return Fun("simulate_comment",
        [
            new MethodParameter("type", "String"),
            new MethodParameter("word", "String")
        ], null, "模拟弹幕事件",
            notes: "type 可用 live_comment、live_gift、live_like；word 为对应弹幕事件内容。");

        // 系统与控制流
        yield return Fun("time.time", [], "Float", "获取真实时间");
        yield return Fun("time.scaleTime", [], "Float", "获取游戏时间");
        yield return Fun("time.datetime", [], "Datetime", "获取当前日期时间");
        yield return Fun("time.deltaTime", [], "Float", "获取帧间隔时间");
        yield return Fun("time.setMaxDeltaTime", [new MethodParameter(string.Empty, "Float", Alias: "maxDeltaTime")], null, "设置最大帧间隔");
        yield return Fun("time.targetFrameRate", [], "Float", "获取目标帧率");
        yield return Fun("time.SetTargetFrameRate", [new MethodParameter(string.Empty, "Long", Alias: "frameRate")], null, "设置目标帧率",
            notes: "实际帧率仍受平台和设备限制。");
        yield return Fun("time.FixedFrame", [], "Float", "获取物理帧率");
        yield return Fun("time.SetFixedFrame", [new MethodParameter(string.Empty, "Long", Alias: "fixedFrameRate")], null, "设置物理帧率",
            notes: "影响物理更新频率，和图形目标帧率不是同一个设置。");
        yield return Fun("pingTime", [], "Float", "获取网络延迟");
        yield return Fun("break", [], null, "跳过", notes: "在 while 内：跳出循环继续执行循环后语句；不在 while 内：结束整个方法（等同 return）");
        yield return Fun("continue", [], null, "下次循环", notes: "在 while 内：跳过当前迭代进入下一次循环；不在 while 内：结束整个方法");
        yield return Fun("script.skip", [], null, "禁用当前方法", notes: "设置 boolSkip=true，停止执行当前方法且后续不再被调用（不是一次性退出）");
        yield return Fun("webview.open", [new MethodParameter("网址@url", "String", Alias: "url")], null, "打开网页",
            notes: "打开外部网页或平台支持的网页视图；不是内嵌 UI 布局。");
        yield return Fun("platform", [], "String", "获取平台名称",
            notes: "可能值：抖音 / 抖音.移动端 / 快手 / TikTok / WindowsPlayer / IPhonePlayer / Android / 微信小游戏 / 抖音小游戏");
        yield return Fun("identifier", [], "String", "获取包名");
        yield return Fun("version", [], "String", "获取版本号");
        yield return Fun("getGameUploadTime", [], "Datetime", "得到玩法发布时间");
        yield return Fun("dynamicCallRet",
        [
            new MethodParameter("命令@Key", "String", Alias: "Key"),
            new MethodParameter("数据@Data", "Dict", Alias: "Data")
        ], "Dict", "动态指令（带返回）", asyncMode: SdkAsyncMode.Async,
            notes: "向平台动态指令通道发送 Key/Data，并等待返回 Dict。Key 必须是平台已支持的指令(具体见Skills文档)。");
        yield return Fun("dynamicCall",
        [
            new MethodParameter("命令@Key", "String", Alias: "Key"),
            new MethodParameter("数据@Data", "Dict", Alias: "Data")
        ], null, "动态指令", asyncMode: SdkAsyncMode.TsAsync,
            notes: "向平台动态指令通道发送 Key/Data，不等待返回值。Key 必须是平台已支持的指令(具体见Skills文档)。");

        // 弹幕
        yield return Fun("danmu.addRank", [new MethodParameter("数据列表@list", "List", Alias: "list")], "List", "上传弹幕排行榜成绩", asyncMode: SdkAsyncMode.Async,
            notes: "list 每项为 Dict，字段：uid、score、wins；返回分数变化列表。");
        yield return Fun("danmu.getByID", [new MethodParameter("玩家ID@uid", "String", Alias: "uid")], "Struct", "获取玩家弹幕排行榜成绩", asyncMode: SdkAsyncMode.Async,
            notes: "返回 Struct 字段：uid、rank、score、wins、lastGiftTime。");
        yield return Fun("danmu.clearRank", [], null, "清空弹幕排行榜",
            notes: "清空当前地图的弹幕排行榜数据。");
        yield return Fun("danmu.setDict",
        [
            new MethodParameter("玩家ID@uid", "String", Alias: "uid"),
            new MethodParameter("数据", "Dict")
        ], null, "设置弹幕玩家数据",
            notes: "写入指定弹幕观众的跨直播间数据。");
        yield return Fun("danmu.getDict", [new MethodParameter("玩家ID@uid", "String", Alias: "uid")], "Dict", "获取弹幕玩家数据", asyncMode: SdkAsyncMode.Async,
            notes: "读取指定弹幕观众的跨直播间数据。");
        yield return Fun("danmu.getDicts", [new MethodParameter("玩家ID列表@ids", "List", Alias: "ids")], "List", "批量获取弹幕玩家数据", asyncMode: SdkAsyncMode.Async,
            notes: "批量读取多个弹幕观众的跨直播间数据。");

        // 公会
        yield return Fun("guid.create",
        [
            new MethodParameter("名称@name", "String", Alias: "name"),
            new MethodParameter("通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true),
            new MethodParameter("通知参数@Attach", "Dict", Alias: "Attach")
        ], null, "创建公会", notes: "回调模式：数据到达后调用同零件上 CallBackMethod 方法，Attach 附加到回调参数");
        yield return Fun("guid.delAsync", [new MethodParameter("公会ID@ID", "Long", Alias: "ID")], "String", "异步解散公会", asyncMode: SdkAsyncMode.Async);
        yield return Fun("guid.del",
        [
            new MethodParameter("公会ID@ID", "Long", Alias: "ID"),
            new MethodParameter("通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true),
            new MethodParameter("通知参数@Attach", "Dict", Alias: "Attach")
        ], null, "解散公会", notes: "回调模式：数据到达后调用同零件上 CallBackMethod 方法，Attach 附加到回调参数");
        yield return Fun("guid.changeSlogan",
        [
            new MethodParameter("公会ID@ID", "Long", Alias: "ID"),
            new MethodParameter("描述@slogan", "String", Alias: "slogan")
        ], null, "修改公会描述");
        yield return Fun("guid.changeAvatar",
        [
            new MethodParameter("公会ID@ID", "Long", Alias: "ID"),
            new MethodParameter("旗帜图片@flagPic", "UIRawImage", Alias: "flagPic")
        ], null, "修改公会头像");
        yield return Fun("guid.changeData",
        [
            new MethodParameter("公会ID@ID", "Long", Alias: "ID"),
            new MethodParameter("存档@Data", "Dict", Alias: "Data")
        ], null, "修改公会数据");
        yield return Fun("guid.changeShortData",
        [
            new MethodParameter("公会ID@ID", "Long", Alias: "ID"),
            new MethodParameter("摘要@Desc", "Dict", Alias: "Desc")
        ], null, "修改公会简短数据");
        yield return Fun("guid.changeName",
        [
            new MethodParameter("公会ID@ID", "Long", Alias: "ID"),
            new MethodParameter("名称@Name", "String", Alias: "Name")
        ], null, "修改公会名称");
        yield return Fun("guid.addMember",
        [
            new MethodParameter("公会ID@GuidID", "Long", Alias: "GuidID"),
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("职位@Role", "Long", Alias: "Role")
        ], null, "添加公会成员");
        yield return Fun("guid.leaveAsync",
        [
            new MethodParameter("公会ID@GuidID", "Long", Alias: "GuidID"),
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID")
        ], "Struct", "异步离开公会", asyncMode: SdkAsyncMode.Async,
            notes: "返回 Struct 字段：guidID(Long)、error(String)。");
        yield return Fun("guid.leave",
        [
            new MethodParameter("公会ID@GuidID", "Long", Alias: "GuidID"),
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true),
            new MethodParameter("通知参数@Attach", "Dict", Alias: "Attach")
        ], null, "离开公会", notes: "回调模式：数据到达后调用同零件上 CallBackMethod 方法，Attach 附加到回调参数");
        yield return Fun("guid.getState",
        [
            new MethodParameter("公会ID@GuidID", "Long", Alias: "GuidID"),
            new MethodParameter("通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true),
            new MethodParameter("通知参数@Attach", "Dict", Alias: "Attach"),
            new MethodParameter("强制刷新", "Bool")
        ], null, "获取公会状态", notes: "回调模式：数据到达后调用同零件上 CallBackMethod 方法，Attach 附加到回调参数");
        yield return Fun("guid.membersAsync", [new MethodParameter("公会ID@GuidID", "Long", Alias: "GuidID")], "List", "异步获取公会成员", asyncMode: SdkAsyncMode.Async);
        yield return Fun("guid.members",
        [
            new MethodParameter("公会ID@GuidID", "Long", Alias: "GuidID"),
            new MethodParameter("通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true),
            new MethodParameter("通知参数@Attach", "Dict", Alias: "Attach")
        ], null, "获取公会成员", notes: "回调模式：数据到达后调用同零件上 CallBackMethod 方法，Attach 附加到回调参数");
        yield return Fun("guid.getMailsAsync", [new MethodParameter("公会ID@GuidID", "Long", Alias: "GuidID")], "List", "异步获取公会邮件", asyncMode: SdkAsyncMode.Async);
        yield return Fun("guid.getMails",
        [
            new MethodParameter("公会ID@GuidID", "Long", Alias: "GuidID"),
            new MethodParameter("通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true),
            new MethodParameter("通知参数@Attach", "Dict", Alias: "Attach")
        ], null, "获取公会邮件", notes: "回调模式：数据到达后调用同零件上 CallBackMethod 方法，Attach 附加到回调参数");
        yield return Fun("guid.delMailAsync",
        [
            new MethodParameter("公会ID@GuidID", "Long", Alias: "GuidID"),
            new MethodParameter("公告ID@MailID", "Long", Alias: "MailID")
        ], "String", "异步删除公会邮件", asyncMode: SdkAsyncMode.Async);
        yield return Fun("guid.delMail",
        [
            new MethodParameter("公会ID@GuidID", "Long", Alias: "GuidID"),
            new MethodParameter("公告ID@MailID", "Long", Alias: "MailID"),
            new MethodParameter("通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true),
            new MethodParameter("通知参数@Attach", "Dict", Alias: "Attach")
        ], null, "删除公会邮件", notes: "回调模式：数据到达后调用同零件上 CallBackMethod 方法，Attach 附加到回调参数");
        yield return Fun("guid.setPlayerType",
        [
            new MethodParameter("公会ID@GuidID", "Long", Alias: "GuidID"),
            new MethodParameter("玩家ID@PlayerID", "Long", Alias: "PlayerID"),
            new MethodParameter("类型@type", "Long", Alias: "type")
        ], null, "设置公会成员类型");
        yield return Fun("guid.getAll",
        [
            new MethodParameter("通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true),
            new MethodParameter("通知参数@Attach", "Dict", Alias: "Attach"),
            new MethodParameter("分页@Page", "Float", Alias: "Page"),
            new MethodParameter("每页数量@Limit", "Float", Alias: "Limit")
        ], null, "获取所有公会", notes: "回调模式：数据到达后调用同零件上 CallBackMethod 方法，Attach 附加到回调参数");

        // 邮件
        yield return Fun("mail.getList",
        [
            new MethodParameter("通知方法@CallBackMethod", "String", Alias: "CallBackMethod", IsScriptMethodName: true),
            new MethodParameter("通知参数@Attach", "Dict", Alias: "Attach")
        ], null, "获取邮件列表", notes: "回调模式：数据到达后调用同零件上 CallBackMethod 方法，Attach 附加到回调参数");

        // UI
        yield return Fun("ui.icon",
        [
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("尺寸@size", "Vector3", Alias: "size"),
            new MethodParameter(string.Empty, "Icon", Alias: "icon"),
            new MethodParameter("颜色@color", "Color", Alias: "color")
        ], "UIRawImage", "创建图标",
            notes: "pos 为 0~1 屏幕坐标；size 为 UI 布局尺寸，不是屏幕像素。");
        yield return Fun("ui.deviceSprite",
        [
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("尺寸@size", "Vector3", Alias: "size"),
            new MethodParameter("零件ID@deviceID", "Float", Alias: "deviceID")
        ], "UIRect", "创建零件精灵图",
            notes: "pos 为 0~1 屏幕坐标；size 为 UI 布局尺寸，不是屏幕像素。deviceID 为是模板机械中的(可通关setting.json指定)零件类型 ID。");
        yield return Fun("ui.deviceSprite1",
        [
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("尺寸@size", "Vector3", Alias: "size"),
            new MethodParameter("零件@device", "Device", Alias: "device")
        ], "UIRect", "创建零件精灵图（带零件）",
            notes: "pos 为 0~1 屏幕坐标；size 为 UI 布局尺寸，不是屏幕像素。");
        yield return Fun("echoMesh",
        [
            new MethodParameter("模型形状@primitiveType", "Enum", EnumType: EchoMeshPrimitiveType, Alias: "primitiveType"),
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("朝向方向@rot", "Vector3", Alias: "rot"),
            new MethodParameter("向上方向@rotUp", "Vector3", Alias: "rotUp"),
            new MethodParameter("缩放@scale", "Vector3", Alias: "scale"),
            new MethodParameter("颜色@color", "Color", Alias: "color"),
            new MethodParameter("光泽度@gloss", "Float", Alias: "gloss"),
            new MethodParameter("金属度@metal", "Float", Alias: "metal"),
            new MethodParameter("自发光@emmit", "Float", Alias: "emmit")
        ], null, "创建网格 UI",
            notes: "临时显示基础网格形状，只产生当前显示效果，不创建工程资源。");
        yield return Fun("echoMesh2",
        [
            new MethodParameter("模型文件名@FileName", "String", Alias: "FileName"),
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("朝向方向@rot", "Vector3", Alias: "rot"),
            new MethodParameter("向上方向@rotUp", "Vector3", Alias: "rotUp"),
            new MethodParameter("缩放@scale", "Vector3", Alias: "scale")
        ], null, "创建网格 UI",
            notes: "按工程内已有模型文件名临时显示模型，只产生当前显示效果，不导入资源。");

        // GPU 实例化
        yield return Fun("gpui.getAddSoldierParam", [], "Dict", "获取添加小兵参数模板",
            notes: "返回 Dict 包含字段：碰撞层(Long,1), 阵型宽(Long,10), 阵型长(Long,8), 阵型间距(Vector3,(1,0,1)), 移动速度(Float,1), 攻击力(Float,1), 攻击距离(Float,1), 追踪距离(Float,4), 攻击间隔(Float,2), 伤害延迟(Float,1), 攻击动画速率(Float,2), 血量(Float,1), 缩放(Float,1), 子弹类型(String), 击飞力(Float,0), 向上击飞力(Float,1), 击飞重力(Float,1), 死亡回调(String), 颜色(Color,white), 子弹最大高度(Float,3), 子弹速度(Float,5), 最多攻击次数(Long,Max), 移动动画速率(Float,1), 死亡动画速率(Float,1), 到目标死亡(Bool,false), 目标相对坐标(Bool,false), 存活时间(Float,Max), 敌对阵营(Long,-1)");
        yield return Fun("gpui.addSoldier",
        [
            new MethodParameter("类型", "String"),
            new MethodParameter("玩家ID", "String"),
            new MethodParameter("阵营", "Long"),
            new MethodParameter("坐标", "Vector3"),
            new MethodParameter("目标点", "Vector3"),
            new MethodParameter("其他参数", "Dict")
        ], "Long", "添加小兵",
            notes: "按阵型宽×阵型长批量创建，返回的 ID 每次 addSoldier 自增。碰撞层需>0，阵型宽/长需>0");
        yield return Fun("gpui.getChangeSoldierParam", [], "Dict", "获取修改小兵参数模板",
            notes: "返回 Dict 包含字段：目标点(Vector3), 移动速度(Float,1), 攻击力(Float,1), 攻击间隔(Float,2), 子弹最大高度(Float,3), 子弹速度(Float,5), 到目标死亡(Bool,false), 目标相对坐标(Bool,false), 存活时间(Float,Max)");
        yield return Fun("gpui.changeSoldier",
        [
            new MethodParameter("ID", "Long"),
            new MethodParameter("其他参数", "Dict")
        ], null, "修改小兵参数");
        yield return Fun("gpui.setHero",
        [
            new MethodParameter("机械", "Mech"),
            new MethodParameter("类型", "String"),
            new MethodParameter("碰撞层", "Long"),
            new MethodParameter("阵营", "Long"),
            new MethodParameter("玩家ID", "String"),
            new MethodParameter("伤害回调方法", "String", IsScriptMethodName: true),
            new MethodParameter("缩放", "Float")
        ], null, "设置英雄单位",
            notes: "将机械绑定为英雄（可被小兵攻击）。伤害回调参数：attacker(String), pos(Vector3), damage(Float)");
        yield return Fun("gpui.setDamageRange",
        [
            new MethodParameter("机械", "Mech"),
            new MethodParameter("阵营", "Long"),
            new MethodParameter("击飞力", "Float"),
            new MethodParameter("击飞重力", "Float"),
            new MethodParameter("玩家ID", "String"),
            new MethodParameter("半径", "Float"),
            new MethodParameter("攻击力", "Float")
        ], null, "设置炮弹单位");
        yield return Fun("gpui.delDamageRange", [new MethodParameter("机械", "Mech")], "Long", "删除炮弹");
        yield return Fun("gpui.setEventCB", [new MethodParameter("小兵被击杀", "String", IsScriptMethodName: true)], null, "设置事件回调",
            notes: "回调参数：$attacker(击杀者玩家ID), $victim(被杀者玩家ID), $pos(死亡位置)");

        // 其他
        yield return Fun("voxel.paint",
        [
            new MethodParameter("坐标@pos", "Vector3", Alias: "pos"),
            new MethodParameter("半径@Radius", "Float", Alias: "Radius"),
            new MethodParameter("力度@Modify", "Float", Alias: "Modify"),
            new MethodParameter("贴图序号@Index", "Long", Alias: "BuildingIndex"),
            new MethodParameter("植被序号@Index", "Long", Alias: "PlantIndex")
        ], null, "绘制体素");
        yield return Fun("recycleBullets", [], null, "回收子弹");
        yield return Fun("map.dynAddDevice_remote",
        [
            new MethodParameter("目标机械@targetMech", "Mech", Alias: "targetMech"),
            new MethodParameter("模板零件序号@index", "Float", Alias: "index"),
            new MethodParameter("位置@pos", "Vector3", Alias: "pos"),
            new MethodParameter("向前方向@forward", "Vector3", Alias: "forward"),
            new MethodParameter("向上方向@up", "Vector3", Alias: "up"),
            new MethodParameter("缩放@scale", "Float", Alias: "scale"),
            new MethodParameter("黏连零件@attachDevice", "Device", Alias: "attachDevice"),
            new MethodParameter("pvpID", "Long")
        ], null, "远程添加零件",
            notes: "动态拼装同步接口，通常由 map.dynAddDevice 间接触发，不需要直接调用。");

        // AI
        yield return Fun("ai.chat", [new MethodParameter("提示词", "String")], "String", "AI 对话", asyncMode: SdkAsyncMode.Async);
        yield return Fun("ai.getAI2ModelParam", [], "Dict", "获取 AI 模型参数模板",
            notes: "参数模板可传给 ai.tripo。Dict 字段：反向提示词(String)、模型种子(Long)、贴图种子(Long)、面数限制(Long)、生成贴图(Bool)、PBR贴图(Bool)、贴图强化(Bool)、智能低模(Bool)、参考图(Bytes)。");
        yield return Fun("ai.tripo",
        [
            new MethodParameter("提示词@Prompt", "String", Alias: "Prompt"),
            new MethodParameter("模型", "Enum", EnumType: TripoModel),
            new MethodParameter("其他参数@Params", "Dict", Alias: "Params")
        ], "Struct", "AI 生成 3D 模型", asyncMode: SdkAsyncMode.Async,
            notes: "返回 Struct 字段：error(String)、data(Bytes，模型二进制)。模型二进制可传给 ObjDevice.replaceMesh 做运行时替换；不会自动导入工程资源。");
        yield return Fun("ai.tripoImage",
        [
            new MethodParameter("提示词@Prompt", "String", Alias: "Prompt"),
            new MethodParameter("模型", "Enum", EnumType: TripoImageModel),
            new MethodParameter("参考图@image", "Bytes", Alias: "image")
        ], "Struct", "AI 生成图片", asyncMode: SdkAsyncMode.Async,
            notes: "返回 Struct 字段：error(String)、data(Bytes，图片二进制)；不会自动导入工程图片资源。");
    }
}
