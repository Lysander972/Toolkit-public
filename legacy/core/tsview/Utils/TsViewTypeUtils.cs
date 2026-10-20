using System.Text;
using System.Text.Json;
using Toolkit.Core.Bescript;
using Toolkit.Core.Canvas;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.TsView;

/// <summary>
/// TsView 类型转换工具类。
///
/// 【职责边界】
/// - BEScript 类型名 ↔ TS 类型名的双向转换
/// - 类型名规范化
/// - 字符串字面量生成
/// </summary>
internal static class TsViewTypeUtils
{
    /// <summary>
    /// 对字符串进行 TS 字面量转义，可控制是否额外转义控制字符。
    /// </summary>
    internal static string EscapeTsString(string text, char quote, bool escapeControlChars)
    {
        StringBuilder sb = new(text.Length);
        foreach (char c in text)
        {
            switch (c)
            {
                case '"':
                    {
                        if (quote == '"')
                        {
                            sb.Append("\\\"");
                            break;
                        }
                        goto default;
                    }
                case '\'':
                    {
                        if (quote == '\'')
                        {
                            sb.Append("\\'");
                            break;
                        }
                        goto default;
                    }
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '“':
                    sb.Append("\\u201c");
                    break;
                case '”':
                    sb.Append("\\u201d");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (escapeControlChars && c < 0x20)
                    {
                        sb.Append("\\x").Append(((int)c).ToString("X2"));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 将字符串值转换为 TS 字符串字面量（带引号和转义）。
    /// </summary>
    internal static string ToTsStringLiteral(string value)
    {
        string escaped = EscapeTsString(value, '"', escapeControlChars: true);
        return $"\"{escaped}\"";
    }

    /// <summary>
    /// 将原始名称转换为 TS 标识符。
    /// 委托给 TsViewIdentifierMap.ToTsIdentifier。
    /// </summary>
    internal static string ToTsIdentifier(string name) => TsViewIdentifierMap.ToTsIdentifier(name);

    /// <summary>
    /// 将 BeScript 类型名转换为 TS 类型名（去除 Be 前缀，首字母大写）。
    /// </summary>
    internal static string ConvertToTsTypeName(string typeName)
    {
        return BescriptVarTypeHelper.ConvertToTsTypeName(typeName);
    }

    /// <summary>
    /// 将 BescriptVarType 转换为 TS 类型名（带 Be 前缀）。
    /// </summary>
    internal static string ToTsBeTypeName(BescriptVarType varType)
    {
        return "Be" + Enum.GetName(typeof(BescriptVarType), varType);
    }

    /// <summary>
    /// 尝试解析 BEScript 类型名为 BescriptVarType。
    /// 委托给 BescriptVarTypeHelper（已下沉到 bescript 层）。
    /// </summary>
    internal static BescriptVarType? TryParseBescriptVarType(string typeName)
    {
        if (TryParseBeEnumTypeArgument(typeName) is string enumTypeName &&
            BuiltinEnumTypes.ByTsTypeName.ContainsKey(enumTypeName))
            return BescriptVarType.Enum;

        if (string.Equals(typeName, "BeEnum", StringComparison.Ordinal))
            return null;
        return BescriptVarTypeHelper.TryParseBescriptVarType(typeName);
    }

    internal static MethodEnumType ResolveMethodEnumTypeFromTsTypeName(string tsTypeName)
    {
        if (TryParseBeEnumTypeArgument(tsTypeName) is not string enumTypeName)
        {
            throw new InvalidOperationException($"TS 类型 {tsTypeName} 不是具体枚举类型 BeEnum<具体分组>。");
        }

        return BuiltinEnumTypes.ByTsTypeName[enumTypeName];
    }

    /// <summary>
    /// 从 TS 类型名解析 BEScript 类型名。
    /// </summary>
    internal static string? TryParseBescriptTypeNameFromTsTypeName(string? tsTypeName)
    {
        if (string.IsNullOrWhiteSpace(tsTypeName))
            return null;

        if (TryParseBeEnumTypeArgument(tsTypeName) is string enumTypeName &&
            BuiltinEnumTypes.ByTsTypeName.ContainsKey(enumTypeName))
            return nameof(BescriptVarType.Enum);

        if (string.Equals(tsTypeName, "BeEnum", StringComparison.Ordinal))
        {
            return null;
        }

        if (tsTypeName.StartsWith("Be", StringComparison.Ordinal) && tsTypeName.Length > 2)
            return tsTypeName[2..];
        return tsTypeName;
    }

    private static string? TryParseBeEnumTypeArgument(string typeName)
    {
        BeEnumTypeArgumentResult? result = JsonSerializer.Deserialize<BeEnumTypeArgumentResult>(
            TsCompilerHost.ParseBeEnumTypeArgument(typeName),
            BodyWritebackJson.Options);
        return result?.EnumTypeName;
    }

    private sealed record BeEnumTypeArgumentResult(string? EnumTypeName);

    /// <summary>
    /// 检测文本是否为合法的 Canvas GUID 形态（TS 视图格式：guid: + 32 位十六进制 或 ==guid: + 32 位十六进制 + ==）。
    /// </summary>
    internal static bool IsCanvasGuid(string text)
    {
        return BescriptSyntax.IsAnyRef(text);
    }

    /// <summary>
    /// 检测文本是否为合法的 Canvas 引用（TS 视图格式：canvas:... 或 guid:xxx）。
    /// </summary>
    internal static bool IsCanvasReference(string text)
    {
        if (CanvasInlineArg.Parse(text) is not null)
        {
            return true;
        }

        return IsCanvasGuid(text);
    }
}
