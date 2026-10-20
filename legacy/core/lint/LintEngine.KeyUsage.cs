namespace Toolkit.Core.Lint;

public static partial class LintEngine
{
    // 统一的类型前缀白名单 - 唯一真相源
    private static readonly HashSet<string> ValidTypePrefixes = new(StringComparer.Ordinal)
    {
        "BOOL", "BOOLEAN",
        "LONG", "INT", "INTEGER",
        "FLOAT", "DOUBLE",
        "STR", "STRING",
        "BYTES", "BINARY",
        "VECTOR3", "VEC3",
        "COLOR",
        "DICT", "MAP",
        "LIST", "ARRAY",
        "STRUCT",
        "TIME", "TIMESTAMP", "DATE", "DATETIME",
        "MECH",
        "DEVICE",
        "PLAYER",
        "CANVAS",
        "ICON"
    };

    /// <summary>
    /// 从键名提取前缀并标准化为类型名。
    /// 返回 null 表示无有效类型前缀。
    /// </summary>
    internal static string? NormalizeKeyPrefix(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        int idx = key.IndexOf('_');
        if (idx <= 0)
            return null;

        string prefix = key[..idx];
        if (!ValidTypePrefixes.Contains(prefix))
            return null;

        // 返回标准化名称
        return prefix switch
        {
            "BOOL" or "BOOLEAN" => "Bool",
            "LONG" or "INT" or "INTEGER" => "Long",
            "FLOAT" or "DOUBLE" => "Float",
            "STR" or "STRING" => "String",
            "BYTES" or "BINARY" => "Bytes",
            "VECTOR3" or "VEC3" => "Vector3",
            "COLOR" => "Color",
            "DICT" or "MAP" => "Dict",
            "LIST" or "ARRAY" => "List",
            "STRUCT" => "Struct",
            "TIME" or "TIMESTAMP" or "DATE" or "DATETIME" => "Datetime",
            "MECH" => "Mech",
            "DEVICE" => "Device",
            "PLAYER" => "Player",
            "CANVAS" => "Canvas",
            "ICON" => "Icon",
            _ => null
        };
    }
}
