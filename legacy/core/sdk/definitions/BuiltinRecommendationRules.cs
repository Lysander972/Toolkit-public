namespace Toolkit.Core.Sdk;

// ╔════════════════════════════════════════════════════════════════════════════════╗
// ║                     【禁止审查区域 - 原版 SDK 精确映射】                          ║
// ╠════════════════════════════════════════════════════════════════════════════════╣
// ║  本文件包含对原版 BeScript SDK 已作废方法规则的精确硬编码映射。                     ║
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
/// 内置已作废方法规则：用于标记 NotRecommended/Obsolete 方法。
/// </summary>
internal static class BuiltinRecommendationRules
{
    internal const string DefaultObsoleteReason = "已作废";

    // Key 规则：Category:OwnerType:MethodName（OwnerType 可为空字符串）
    private static readonly HashSet<string> ObsoleteMethodKeys = new(StringComparer.Ordinal)
    {
        "GlobalFunc::audio.playEffect",
        "GlobalFunc::map.loadAsync",
        "VarFunc:Datetime:addSec",
        "VarFunc:UIButton:addClickCB",
        "VarFunc:UIButton:addPointDownCB",
        "VarFunc:UIButton:addPointEnterCB",
        "VarFunc:UIButton:addPointExitCB",
        "VarFunc:UIButton:addPointUpCB",
        "VarFunc:UIRawImage:setVideo",
        "VarFunc:UIRawImage:SetURLPic",
        "VarFunc:List:insertVecs",
        "VarFunc:List:inserFloats",
    };

    internal static string? GetObsoleteReason(
        MethodCategory category,
        string? ownerType,
        string methodName)
    {
        string key = $"{category}:{ownerType}:{methodName}";
        if (ObsoleteMethodKeys.Contains(key))
            return DefaultObsoleteReason;
        return null;
    }
}
