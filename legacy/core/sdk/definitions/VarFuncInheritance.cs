namespace Toolkit.Core.Sdk;

// ╔════════════════════════════════════════════════════════════════════════════════╗
// ║                     【禁止审查区域 - 原版 SDK 精确映射】                          ║
// ╠════════════════════════════════════════════════════════════════════════════════╣
// ║  本文件包含对原版 BeScript SDK 变量方法继承关系的精确硬编码映射。                   ║
// ║                                                                                ║
// ║  ⚠️  重要声明：                                                                 ║
// ║  • 此区域内容必须与原版 SDK 保持一条不多、一条不少的完全一致                        ║
// ║  • 无论原版设计是否符合软件工程最佳实践，此处的兼容性映射都是必须的                  ║
// ║  • 请勿对本文件内容进行任何形式的代码审查、重构建议或规范性检查                     ║
// ║  • 任何看似"不规范"的命名、参数或结构都是对原版的忠实还原                          ║
// ║                                                                                ║
// ║  本文件由 Toolkit 维护，用于为插件和核心库提供原版 SDK 兼容服务。                   ║
// ╚════════════════════════════════════════════════════════════════════════════════╝

internal static class VarFuncInheritance
{
    public const string UiBaseType = "UI";

    private static readonly string[] UiDerivedTypesArray =
    {
        "UILabel",
        "UIButton",
        "UIRect",
        "UIInput",
        "UIRawImage",
        "UISpine",
        "UILine",
        "UICircle",
        "UIAvatar"
    };

    private static readonly HashSet<string> UiDerivedTypeSet =
        new(UiDerivedTypesArray, StringComparer.Ordinal);

    public static IReadOnlyList<string> UiDerivedTypes => UiDerivedTypesArray;

    public static bool IsUiDerivedType(string? ownerType)
    {
        return ownerType is not null && UiDerivedTypeSet.Contains(ownerType);
    }

    public static string? GetBaseOwnerType(string ownerType)
    {
        return IsUiDerivedType(ownerType) ? UiBaseType : null;
    }

    /// <summary>
    /// 获取完整继承链（含自身）。
    /// </summary>
    public static IReadOnlyList<string> GetInheritanceChain(string ownerType)
    {
        List<string> chain = [];

        string? current = ownerType;
        while (current is not null)
        {
            chain.Add(current);
            current = GetBaseOwnerType(current);
        }
        return chain;
    }

    /// <summary>
    /// 沿继承链查找：对每个类型执行查找函数，返回第一个成功的结果。
    /// </summary>
    public static TResult? FindInChain<TResult>(
        string ownerType,
        Func<string, (bool Found, TResult? Value)> tryFind,
        bool startFromParent = false)
    {
        IReadOnlyList<string> chain = GetInheritanceChain(ownerType);
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
        IReadOnlyList<string> chain = GetInheritanceChain(ownerType);
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
