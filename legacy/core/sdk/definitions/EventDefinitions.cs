namespace Toolkit.Core.Sdk;

// ╔════════════════════════════════════════════════════════════════════════════════╗
// ║                     【禁止审查区域 - 原版 SDK 精确映射】                          ║
// ╠════════════════════════════════════════════════════════════════════════════════╣
// ║  本文件包含对原版 BeScript SDK 事件签名的精确硬编码映射。                          ║
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
/// 事件签名定义
/// 按 EventSignatures.md 文档定义所有事件
/// </summary>
public static class EventDefinitions
{
    public static IEnumerable<EventSignature> GetAll()
    {
        int[] funcDeviceIds = BuildAllowedTypeIds("FuncDevice", excludeOwnerTypes: new[] { "ClientMapLogicDevice", "BirthBuilding" });
        yield return Event("FuncDevice", "start", Array.Empty<EventParameter>(), "零件启动事件", funcDeviceIds);
        yield return Event("FuncDevice", "update", Array.Empty<EventParameter>(), "零件更新事件", funcDeviceIds);
        foreach (EventSignature sig in GetClientMapLogicDeviceEvents()) yield return sig;
        foreach (EventSignature sig in GetMapCoreDeviceEvents()) yield return sig;
        foreach (EventSignature sig in GetCoreDeviceEvents()) yield return sig;
        foreach (EventSignature sig in GetBirthBuildingEvents()) yield return sig;
        foreach (EventSignature sig in GetTriggerBuildingEvents()) yield return sig;
        foreach (EventSignature sig in GetLaserDeviceEvents()) yield return sig;
        foreach (EventSignature sig in GetSLGLogicDeviceEvents()) yield return sig;
    }

    private static EventSignature Event(
        string deviceType,
        string eventName,
        EventParameter[] parameters,
        string summary,
        int[]? allowedTypeIds = null,
        string? notes = null)
    {
        return new EventSignature(
            eventName,
            deviceType,
            parameters,
            summary,
            null,
            allowedTypeIds,
            Notes: notes);
    }

    private static EventParameter Param(string name, string typeName, string? description = null)
    {
        return new EventParameter(name, typeName, description);
    }

    /// <summary>
    /// 将原版 Action 归属继承链压平成事件注册的包含列表；允许/排除优先级仍由 EventSignature.IsAllowedForDeviceType 统一判定。
    /// </summary>
    private static int[] BuildAllowedTypeIds(string ownerType, IReadOnlyCollection<string> excludeOwnerTypes)
    {
        return DeviceTypeIds.GetActionOwnerTypeMappings()
            .Where(mapping =>
                !excludeOwnerTypes.Contains(mapping.ActionOwnerTypeName, StringComparer.Ordinal) &&
                DeviceActionInheritance.GetInheritanceChain(mapping.ActionOwnerTypeName, includeDevice: false)
                    .Contains(ownerType, StringComparer.Ordinal))
            .Select(static mapping => mapping.TypeId)
            .OrderBy(static id => id)
            .ToArray();
    }

    /// <summary>ClientMapLogicDevice 事件（type=1101）</summary>
    private static IEnumerable<EventSignature> GetClientMapLogicDeviceEvents()
    {
        int[] ids = new[] { DeviceTypeIds.ClientMapLogicDevice };

        yield return Event("ClientMapLogicDevice", "clientStart", new[]
        {
            Param("$mapIndex", "Float"),
            Param("$rule", "String")
        }, "客户端启动事件", ids);

        yield return Event("ClientMapLogicDevice", "clientUpdate", Array.Empty<EventParameter>(), "客户端更新事件", ids);

        yield return Event("ClientMapLogicDevice", "iap", new[]
        {
            Param("$id", "String")
        }, "内购事件", ids);

        yield return Event("ClientMapLogicDevice", "rewardAD", Array.Empty<EventParameter>(), "激励广告事件", ids);

        yield return Event("ClientMapLogicDevice", "talk", new[]
        {
            Param("$sender", "Player"),
            Param("$msg", "String")
        }, "聊天事件", ids);

        yield return Event("ClientMapLogicDevice", "rankInfo", new[]
        {
            Param("$rankList", "List"),
            Param("$rankIndex", "Long")
        }, "排行榜信息事件", ids);

        yield return Event("ClientMapLogicDevice", "onGetRankScore", new[]
        {
            Param("$playerID", "Long"),
            Param("$score", "Long"),
            Param("$rankIndex", "Long")
        }, "获取排名分数事件", ids);

        yield return Event("ClientMapLogicDevice", "myMechGenerate", new[]
        {
            Param("$mech", "Mech")
        }, "我的机械生成事件", ids);

        yield return Event("ClientMapLogicDevice", "onGetELOEnemySaveData", new[]
        {
            Param("$enemy", "Player"),
            Param("$enemyDataDict", "Dict"),
            Param("$enemyELO", "Float")
        }, "获取ELO敌人存档数据事件", ids);

        yield return Event("ClientMapLogicDevice", "onGetELO", new[]
        {
            Param("$score", "Float")
        }, "获取ELO分数事件", ids);

        yield return Event("ClientMapLogicDevice", "playerDictChange", new[]
        {
            Param("$player", "Player"),
            Param("$name", "String")
        }, "玩家字典变更事件", ids);

        yield return Event("ClientMapLogicDevice", "onCustomStateChange", new[]
        {
            Param("$name", "String")
        }, "自定义状态变更事件", ids);

        yield return Event("ClientMapLogicDevice", "onMailList", new[]
        {
            Param("$mail", "Dict")
        }, "邮件列表事件", ids);

        yield return Event("ClientMapLogicDevice", "onGuidTalk", new[]
        {
            Param("$word", "String"),
            Param("$attach", "Dict"),
            Param("$fromPlayer", "Player")
        }, "公会聊天事件", ids);

        yield return Event("ClientMapLogicDevice", "onGuidMail", new[]
        {
            Param("$mail", "Dict")
        }, "公会邮件事件", ids);

        yield return Event("ClientMapLogicDevice", "onPlayerShareVarChange", new[]
        {
            Param("$dict", "Dict")
        }, "玩家共享变量变更事件", ids);

        yield return Event("ClientMapLogicDevice", "onDanmu", new[]
        {
            new EventParameter("$type", "Enum", EnumType: BuiltinEnumTypes.EventCommentGiftLike),
            Param("$list", "List")
        }, "弹幕事件", ids);
    }

    /// <summary>MapCoreDevice 事件（type=0，地图专属）</summary>
    private static IEnumerable<EventSignature> GetMapCoreDeviceEvents()
    {
        int[] ids = new[] { DeviceTypeIds.MapCoreDevice };

        yield return Event("MapCoreDevice", "start", Array.Empty<EventParameter>(), "地图启动事件", ids);
        yield return Event("MapCoreDevice", "update", Array.Empty<EventParameter>(), "地图更新事件", ids);

        yield return Event("MapCoreDevice", "playerJoin", new[]
        {
            Param("$player", "Player")
        }, "玩家加入事件", ids);

        yield return Event("MapCoreDevice", "playerLeave", new[]
        {
            Param("$player", "Player")
        }, "玩家离开事件", ids);

        yield return Event("MapCoreDevice", "playerDictChange", new[]
        {
            Param("$player", "Player"),
            Param("$name", "String")
        }, "玩家字典变更事件", ids);

        yield return Event("MapCoreDevice", "mechDictChange", new[]
        {
            Param("$mech", "Mech"),
            Param("name", "String")  // 原版第二个参数无 $ 前缀
        }, "机械字典变更事件", ids);

        yield return Event("MapCoreDevice", "mechDie", new[]
        {
            Param("$victim", "Mech"),
            Param("$attacker", "Mech")
        }, "机械死亡事件", ids);

        yield return Event("MapCoreDevice", "onCustomStateChange", new[]
        {
            Param("$name", "String")
        }, "自定义状态变更事件（客户端）", ids);
    }


    /// <summary>CoreDevice 事件（type=1）</summary>
    private static IEnumerable<EventSignature> GetCoreDeviceEvents()
    {
        int[] ids = new[] { DeviceTypeIds.CoreDevice };

        yield return Event("CoreDevice", "fixedUpdate", Array.Empty<EventParameter>(), "固定更新事件", ids);

        yield return Event("CoreDevice", "mechDie", new[]
        {
            Param("$attacker", "Mech")
        }, "机械死亡事件", ids);

        yield return Event("CoreDevice", "beHit", new[]
        {
            Param("$attacker", "Device"),
            Param("$self", "Device"),
            Param("$force", "Vector3"),
            new EventParameter("$type", "Enum", EnumType: BuiltinEnumTypes.EventBulletDeviceOther),
            Param("$pos", "Vector3"),
            Param("$impulse", "Vector3")
        }, "被击中事件", ids);

        yield return Event("CoreDevice", "mechDictChange", new[]
        {
            Param("$name", "String")
        }, "机械字典变更事件", ids);

        yield return Event("CoreDevice", "deviceDie", new[]
        {
            Param("$device", "Device")
        }, "零件死亡事件", ids);

        yield return Event("CoreDevice", "pathFindFinish", new[]
        {
            Param("$count", "Float")
        }, "寻路完成事件", ids);
    }

    /// <summary>
    /// BirthBuilding 事件（type=1110）。
    /// 事件作用于生成的机械；与 CoreDevice 的部分事件同名，但签名独立定义。
    /// </summary>
    private static IEnumerable<EventSignature> GetBirthBuildingEvents()
    {
        int[] ids = new[] { DeviceTypeIds.BirthBuilding };

        yield return Event("BirthBuilding", "mechStart", Array.Empty<EventParameter>(), "机械启动事件", ids);
        yield return Event("BirthBuilding", "mechUpdate", Array.Empty<EventParameter>(), "机械更新事件", ids);
        yield return Event("BirthBuilding", "mechfixedUpdate", Array.Empty<EventParameter>(), "机械固定更新事件", ids);

        yield return Event("BirthBuilding", "mechPathFindFinishLE", new[]
        {
            Param("$count", "Float")
        }, "机械寻路完成事件", ids);

        yield return Event("BirthBuilding", "mechDie", new[]
        {
            Param("$attacker", "Mech")
        }, "机械死亡事件", ids);

        yield return Event("BirthBuilding", "beHit", new[]
        {
            Param("$attacker", "Device"),
            Param("$self", "Device"),
            Param("$force", "Vector3"),
            new EventParameter("$type", "Enum", EnumType: BuiltinEnumTypes.EventBulletDeviceOther),
            Param("$pos", "Vector3"),
            Param("$impulse", "Vector3")
        }, "被击中事件", ids);

        yield return Event("BirthBuilding", "mechDictChange", new[]
        {
            Param("$name", "String")
        }, "机械字典变更事件", ids);

        yield return Event("BirthBuilding", "deviceDie", new[]
        {
            Param("$device", "Device")
        }, "零件死亡事件", ids);
    }

    /// <summary>TriggerBuilding 事件（type=1100）</summary>
    private static IEnumerable<EventSignature> GetTriggerBuildingEvents()
    {
        int[] ids = new[] { DeviceTypeIds.TriggerBuilding };

        yield return Event("TriggerBuilding", "enterTrigger", new[]
        {
            Param("$trigger", "Device")
        }, "进入触发器事件", ids);
        yield return Event("TriggerBuilding", "leaveTrigger", new[]
        {
            Param("$trigger", "Device")
        }, "离开触发器事件", ids);
    }

    /// <summary>LaserDevice 事件（type=243/317）</summary>
#pragma warning disable TK006 // 与其他 Get*Events 方法对称
    private static IEnumerable<EventSignature> GetLaserDeviceEvents()
#pragma warning restore TK006
    {
        int[] ids = new[] { DeviceTypeIds.LaserDeviceRed, DeviceTypeIds.LaserDeviceBlue };

        yield return Event("LaserDevice", "laserDetectEnemy", new[]
        {
            Param("$distance", "Float"),
            Param("$target", "Device")
        }, "激光检测敌人事件", ids);
    }


    /// <summary>SLGLogicDevice 事件（type=1103）</summary>
    private static IEnumerable<EventSignature> GetSLGLogicDeviceEvents()
    {
        int[] ids = new[] { DeviceTypeIds.SLGLogicDevice };

        yield return Event("SLGLogicDevice", "start", Array.Empty<EventParameter>(), "启动事件", ids);
        yield return Event("SLGLogicDevice", "update", Array.Empty<EventParameter>(), "更新事件", ids);

        yield return Event("SLGLogicDevice", "enterWorld", new[]
        {
            Param("$mainItemID", "Long"),
            Param("$gridNum", "Long"),
            Param("$gridSize", "Long"),
            Param("$name", "String"),
            Param("$globalData", "Dict")
        }, "进入世界事件", ids);

        yield return Event("SLGLogicDevice", "worldEnterArea", new[]
        {
            Param("$itemID", "Long"),
            Param("$x", "Long"),
            Param("$y", "Long"),
            Param("$type", "Long"),
            Param("$info", "Dict"),
            Param("$owner", "Player")
        }, "进入区域事件", ids);

        yield return Event("SLGLogicDevice", "worldLeaveArea", new[]
        {
            Param("$itemID", "Long")
        }, "离开区域事件", ids);

        yield return Event("SLGLogicDevice", "onWorldMoveItem", new[]
        {
            Param("$itemID", "Long"),
            Param("$x", "Long"),
            Param("$y", "Long")
        }, "世界物品移动事件", ids);

        yield return Event("SLGLogicDevice", "worldChangeItem", new[]
        {
            Param("$itemID", "Long"),
            Param("$info", "Dict"),
            Param("$owner", "Player")
        }, "世界物品变更事件", ids);

        yield return Event("SLGLogicDevice", "onWorldMoveFocus", new[]
        {
            Param("$x", "Long"),
            Param("$y", "Long")
        }, "世界焦点移动事件", ids);

        yield return Event("SLGLogicDevice", "worldExit", new[]
        {
            Param("$name", "String")
        }, "退出世界事件", ids);

        yield return Event("SLGLogicDevice", "worldTalk", new[]
        {
            Param("$from", "Long"),
            Param("$word", "String"),
            Param("$attach", "Dict")
        }, "世界聊天事件", ids);

        yield return Event("SLGLogicDevice", "onWorldGlobalData", new[]
        {
            Param("$dict", "Dict")
        }, "世界全局数据事件", ids);
    }
}
