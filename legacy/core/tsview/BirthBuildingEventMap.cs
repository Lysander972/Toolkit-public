namespace Toolkit.Core.TsView;

/// <summary>
/// 机械产生器(1110) 事件名映射表。
///
/// 三层概念：
///   机械产生器存储事件名（BirthBuilding 零件文件中实际存储的脚本组名）
///     → 机械核心事件名（CoreDevice 注入目标事件槽）
///
/// 注册块使用机械核心事件名，写回时映射回机械产生器存储事件名。
/// </summary>
internal static class BirthBuildingEventMap
{
    /// <summary>机械产生器存储事件名 → 注入目标核心事件名</summary>
    public static readonly IReadOnlyDictionary<string, string> ToCoreName =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "mechStart",           "start" },
            { "mechUpdate",          "update" },
            { "mechfixedUpdate",     "fixedUpdate" },
            { "mechPathFindFinishLE","pathFindFinish" },
            { "mechDie",             "mechDie" },
            { "mechDictChange",      "mechDictChange" },
            { "beHit",               "beHit" },
            { "deviceDie",           "deviceDie" },
        };

    /// <summary>注入目标核心事件名 → 机械产生器存储事件名（用于 parse 写回）</summary>
    public static readonly IReadOnlyDictionary<string, string> ToStorageName;

    static BirthBuildingEventMap()
    {
        Dictionary<string, string> reverse = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> kv in ToCoreName)
            reverse[kv.Value] = kv.Key;
        ToStorageName = reverse;
    }
}
