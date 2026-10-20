namespace Toolkit.Core.Bescript;

public enum BescriptVarType
{
    Void,
    Float,
    Datetime,
    UI,
    Vector3,
    Bool,
    String,
    Enum,
    UISpine,
    UICircle,
    UIRect,
    UIButton,
    UILabel,
    Color,
    Device,
    UIInput,
    UILine,
    List,
    Dict,
    Any,
    UIAvatar,
    UIRawImage,
    Mech,
    Player,
    Icon,
    Long,
    Number,
    Struct,
    Canvas,
    Bytes,
    Mesh
}

/// <summary>
/// 仅用于类型字符串校验，保持与原 VarType 的名称集合一致，
/// 不参与运行时逻辑，仅作为 parse/lint 的辅助。
/// </summary>
public static class BescriptVarTypeHelper
{
    private static readonly string[] Names = Enum.GetNames(typeof(BescriptVarType));

    public static BescriptVarType? TryParse(string typeName)
    {
        for (int i = 0; i < Names.Length; i++)
        {
            if (string.Equals(Names[i], typeName, StringComparison.Ordinal))
            {
                return (BescriptVarType)i;
            }
        }

        return null;
    }

    /// <summary>
    /// 将 BeScript 类型名转换为 TS 类型名（仅去除 Be 前缀）。
    /// </summary>
    public static string ConvertToTsTypeName(string typeName)
    {
        string converted = typeName;

        // 去除 Be 前缀
        if (converted.StartsWith("Be", StringComparison.Ordinal) && converted.Length > 2)
        {
            converted = converted[2..];
        }

        return converted;
    }

    /// <summary>
    /// 尝试解析 BeScript 类型名为 BescriptVarType（支持 Be 前缀，大小写严格匹配）。
    /// </summary>
    public static BescriptVarType? TryParseBescriptVarType(string typeName)
    {
        string convertedType = ConvertToTsTypeName(typeName);
        return TryParse(convertedType);
    }

    /// <summary>
    /// 生成允许的变量类型列表文本（带 Be 前缀），用于错误消息。
    /// </summary>
    internal static string BuildAllowedTypesText(Func<BescriptVarType, bool> filter)
    {
        List<string> names = [];
        foreach (BescriptVarType t in Enum.GetValues(typeof(BescriptVarType)))
        {
            if (filter(t)) names.Add("Be" + Enum.GetName(typeof(BescriptVarType), t));
        }
        return string.Join(", ", names);
    }

    internal static bool IsSupportedGlobalVarType(BescriptVarType type)
    {
        return type is BescriptVarType.Float
            or BescriptVarType.Bool
            or BescriptVarType.Vector3
            or BescriptVarType.Color
            or BescriptVarType.String
            or BescriptVarType.Player
            or BescriptVarType.Long
            or BescriptVarType.UIButton
            or BescriptVarType.UILabel
            or BescriptVarType.UISpine
            or BescriptVarType.UIRect
            or BescriptVarType.UILine
            or BescriptVarType.UIInput
            or BescriptVarType.List
            or BescriptVarType.Dict
            or BescriptVarType.Struct
            or BescriptVarType.Device
            or BescriptVarType.Mech
            or BescriptVarType.Canvas
            or BescriptVarType.UIRawImage
            or BescriptVarType.UIAvatar
            or BescriptVarType.Datetime;
    }
}
