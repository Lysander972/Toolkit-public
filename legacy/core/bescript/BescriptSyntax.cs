using Toolkit.Core.Infra;

namespace Toolkit.Core.Bescript;

/// <summary>
/// BeScript 语法常量定义
/// </summary>
public static class BescriptSyntax
{
    // ========== BES 格式 ==========

    public const string BeginCodePrefix = "begincode";
    public const string BeginCodeV2 = "begincodeV2";
    public const string EndCodePrefix = "endcode";

    public const string NamePrefix = "name:";
    public const string CommentPrefix = "comment:";
    public const string GuidPrefix = "guid:";
    public const string ColorPrefix = "color:";
    public const string ParamsPrefix = "params:";
    public const string ReturnPrefix = "return:";
    public const string LinesPrefix = "lines:";

    public const string DebugFlagPrefix = "debug";
    public const string SkipFlagPrefix = "skip";
    public const string PrivateFlagPrefix = "private";

    /// <summary>
    /// BES 禁用行前缀: `/*`[缩进][content]
    /// 示例: `/*//离场动画` (depth=0)
    /// 示例: `/*  if (x > 0)` (depth=1)
    /// </summary>
    public const string BesDisabledPrefix = "/*";

    /// <summary>
    /// BES 注释行前缀: `//`[content]
    /// 示例: `//离场动画`
    /// </summary>
    public const string BesCommentPrefix = "//";

    // ========== GUID 引用编解码 ==========
    // Canvas GUID 引用两套原版格式：旧版 guid:xxx / 新版 ==guid:xxx==。
    // 前缀 guid: 与脚本头字段同源（GuidPrefix）；此区为 GUID 引用编解码唯一权威。

    internal const string NewRefPrefix = "==" + GuidPrefix;
    internal const string NewRefSuffix = "==";

    /// <summary>
    /// 是否为旧版 GUID 引用（guid: + 32 位小写十六进制）。
    /// </summary>
    internal static bool IsLegacyRef(string? text)
    {
        return text is not null
            && text.StartsWith(GuidPrefix, StringComparison.Ordinal)
            && text.Length == GuidPrefix.Length + GuidText.HexLength
            && GuidText.Validate(text[GuidPrefix.Length..]);
    }

    /// <summary>
    /// 是否为新版 GUID 引用（==guid: + 32 位小写十六进制 + ==）。
    /// </summary>
    internal static bool IsNewRef(string? text)
    {
        return text is not null
            && text.StartsWith(NewRefPrefix, StringComparison.Ordinal)
            && text.EndsWith(NewRefSuffix, StringComparison.Ordinal)
            && text.Length == NewRefPrefix.Length + GuidText.HexLength + NewRefSuffix.Length
            && GuidText.Validate(text[NewRefPrefix.Length..^NewRefSuffix.Length]);
    }

    /// <summary>
    /// 是否为任意版本的 GUID 引用。
    /// </summary>
    internal static bool IsAnyRef(string? text) => IsLegacyRef(text) || IsNewRef(text);

    /// <summary>
    /// 从 GUID 引用文本提取纯 GUID，无法识别时返回 null。
    /// </summary>
    internal static string? TryExtractGuid(string? text)
    {
        if (text is null) return null;
        if (IsLegacyRef(text)) return text[GuidPrefix.Length..];
        if (IsNewRef(text)) return text[NewRefPrefix.Length..^NewRefSuffix.Length];
        return null;
    }

    /// <summary>
    /// 构造旧版 GUID 引用。
    /// </summary>
    internal static string FormatLegacyRef(string guid)
    {
        if (!GuidText.Validate(guid)) throw new ArgumentException("GUID 格式无效", nameof(guid));
        return GuidPrefix + guid;
    }

    /// <summary>
    /// 构造新版 GUID 引用。
    /// </summary>
    internal static string FormatNewRef(string guid)
    {
        if (!GuidText.Validate(guid)) throw new ArgumentException("GUID 格式无效", nameof(guid));
        return NewRefPrefix + guid + NewRefSuffix;
    }

    // ========== TS 格式 ==========

    /// <summary>
    /// TS 禁用壳开始: `/* `[content]` */`
    /// 示例: `/* if (x > 0) */`
    /// </summary>
    public const string TsDisabledStart = "/* ";

    /// <summary>
    /// TS 禁用壳结束: `/* `[content]` */`
    /// 示例: `/* if (x > 0) */`
    /// </summary>
    public const string TsDisabledEnd = " */";

    /// <summary>
    /// TS 普通注释行前缀: `//[content]`
    /// 示例: `//This is a comment`
    /// </summary>
    public const string TsCommentPrefix = "//";

    // ========== 其他 ==========

    /// <summary>
    /// 缩进单位（2个空格）
    /// </summary>
    public const int IndentSize = 2;
}
